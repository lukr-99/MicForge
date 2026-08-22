using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using MicForge.ViewModels;

namespace MicForge;

/// <summary>
/// A loopback control endpoint that lets the Relay agent (the phone-as-StreamDeck companion) drive
/// MicForge and mirror its live state back onto the deck. Same machine only: it listens on a named
/// pipe (<c>\\.\pipe\MicForge.DeckControl</c>), never a network socket, so it is unreachable from the
/// LAN. Messages are newline-delimited JSON — the "Deck Control Contract" (protocol 1, additive):
///
///   client → us : {"op":"getState"}
///                 {"op":"set","target":"mute|bypass|running","value":true}
///                 {"op":"toggle","target":"mute|bypass|running"}
///                 {"op":"preset","name":"…"}   |   {"op":"preset","dir":"next|prev"}
///                 {"op":"stage","id":"<processorId>"[,"value":true]}   toggle/set a DSP stage
///                 {"op":"param","key":"<stageId>|<label>","value":12.5}  set a DSP parameter
///                 {"op":"meter","enabled":true}                         (un)subscribe to input level
///   us → client : {"type":"hello","app":"MicForge","version":"…","protocol":1}
///                 {"type":"state", mute, bypass, running, preset, presets:[…],
///                                  stages:[{id,title,enabled,canToggle}],
///                                  params:[{key,stage,label,value,min,max,step,unit}]}
///                 {"type":"meter","in":0.0}    input level, ~10 Hz while a client is subscribed
///
/// The whole thing is best-effort and fully isolated: every path is wrapped so a bridge failure can
/// never touch MicForge's audio path or UI.
/// </summary>
public sealed class DeckBridge : IDisposable
{
    public const string PipeName = "MicForge.DeckControl";
    public const int Protocol = 1;

    private readonly MainViewModel _vm;
    private readonly Dispatcher _dispatcher;
    private readonly CancellationTokenSource _cts = new();
    private readonly object _clientsLock = new();
    private readonly List<StreamWriter> _clients = new();
    private readonly HashSet<StreamWriter> _meterClients = new();
    private readonly List<StageViewModel> _watchedStages = new();
    private DispatcherTimer _meterTimer;
    private volatile bool _started;

    public DeckBridge(MainViewModel vm)
    {
        _vm = vm;
        _dispatcher = Application.Current != null ? Application.Current.Dispatcher : Dispatcher.CurrentDispatcher;
    }

    public void Start()
    {
        if (_started) return;
        _started = true;
        _vm.PropertyChanged += OnVmPropertyChanged;
        WatchStages();
        _vm.Stages.CollectionChanged += (_, _) => WatchStages();
        // Throttled input-level push (~10 Hz) while any client has subscribed via {"op":"meter"}.
        _meterTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(100), DispatcherPriority.Normal,
            (_, _) => MeterTick(), _dispatcher);
        _meterTimer.Start();
        var t = new Thread(AcceptLoop) { IsBackground = true, Name = "MicForge-DeckBridge" };
        t.Start();
        Log.Info(@"Deck bridge listening on \\.\pipe\" + PipeName + ".");
    }

    // ── accept + per-client loops ─────────────────────────────────────────────────────────────
    private void AcceptLoop()
    {
        while (!_cts.IsCancellationRequested)
        {
            NamedPipeServerStream server = null;
            try
            {
                server = new NamedPipeServerStream(PipeName, PipeDirection.InOut,
                    NamedPipeServerStream.MaxAllowedServerInstances,
                    PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
                server.WaitForConnection();          // blocks until a client connects (or dispose pokes it)
                if (_cts.IsCancellationRequested) { try { server.Dispose(); } catch { } break; }

                var pipe = server;
                var t = new Thread(() => ClientLoop(pipe)) { IsBackground = true, Name = "MicForge-DeckBridge-Client" };
                t.Start();
            }
            catch (ObjectDisposedException) { break; }
            catch (Exception ex)
            {
                Log.Error("Deck bridge accept failed", ex);
                try { if (server != null) server.Dispose(); } catch { }
                Thread.Sleep(1000);
            }
        }
    }

    private void ClientLoop(NamedPipeServerStream pipe)
    {
        StreamWriter writer = null;
        try
        {
            var reader = new StreamReader(pipe, new UTF8Encoding(false));
            writer = new StreamWriter(pipe, new UTF8Encoding(false)) { AutoFlush = true };
            lock (_clientsLock) _clients.Add(writer);
            Log.Info("Deck bridge: Relay agent connected.");

            // Push the initial snapshot, THEN read. (Writing before both sides read would deadlock the
            // pipe — the client goes straight to reading on its side.)
            Send(writer, HelloJson());
            _dispatcher.Invoke(() => Send(writer, StateJson()));

            string line;
            while (!_cts.IsCancellationRequested && (line = reader.ReadLine()) != null)
                HandleCommand(line, writer);
        }
        catch (IOException) { /* client disconnected */ }
        catch (ObjectDisposedException) { }
        catch (Exception ex) { Log.Error("Deck bridge client error", ex); }
        finally
        {
            if (writer != null) lock (_clientsLock) { _clients.Remove(writer); _meterClients.Remove(writer); }
            try { pipe.Dispose(); } catch { }
        }
    }

    // ── commands ──────────────────────────────────────────────────────────────────────────────
    private void HandleCommand(string line, StreamWriter writer)
    {
        if (string.IsNullOrWhiteSpace(line)) return;
        JsonElement root;
        try { root = JsonDocument.Parse(line).RootElement; }
        catch { return; }
        if (root.ValueKind != JsonValueKind.Object) return;

        string op = GetStr(root, "op");
        if (string.IsNullOrEmpty(op)) return;

        // Mutations touch the view-model / audio engine — marshal onto the UI thread.
        _dispatcher.Invoke(() =>
        {
            try
            {
                switch (op)
                {
                    case "getState": break;   // reply below
                    case "set":
                        ApplySet(GetStr(root, "target"), BoolOrNull(root, "value") ?? false);
                        break;
                    case "toggle":
                        ApplyToggle(GetStr(root, "target"));
                        break;
                    case "preset":
                        if (root.TryGetProperty("dir", out var d) && d.ValueKind == JsonValueKind.String)
                            CyclePreset(d.GetString());
                        else
                            SelectPreset(GetStr(root, "name"));
                        break;
                    case "stage":
                        ApplyStage(GetStr(root, "id"), BoolOrNull(root, "value"));   // null = toggle
                        break;
                    case "meter":
                        SetMeter(writer, BoolOrNull(root, "enabled") ?? true);
                        break;
                    case "param":
                        ApplyParam(GetStr(root, "key"), DoubleOrNull(root, "value"));
                        return;   // no full-state reply — param drags are frequent
                }
            }
            catch (Exception ex) { Log.Error("Deck bridge command failed", ex); }

            // Reply to the requester with the resulting state (a mutation also broadcasts to all).
            Send(writer, StateJson());
        });
    }

    /// <summary>Sets a DSP parameter identified by "stageId|label" to an absolute value (clamped).</summary>
    private void ApplyParam(string key, double? value)
    {
        if (string.IsNullOrEmpty(key) || value is not { } v) return;
        var sep = key.IndexOf('|');
        if (sep <= 0) return;
        var stageId = key.Substring(0, sep);
        var label = key.Substring(sep + 1);
        var stage = _vm.Stages.FirstOrDefault(s => string.Equals(s.ProcessorId, stageId, StringComparison.OrdinalIgnoreCase));
        var prm = stage?.Params.FirstOrDefault(p => string.Equals(p.Label, label, StringComparison.Ordinal));
        if (prm == null) return;
        prm.Value = Math.Clamp(v, prm.Min, prm.Max);
    }

    /// <summary>Enable/disable a DSP stage by its processor id. A null <paramref name="value"/> toggles.</summary>
    private void ApplyStage(string id, bool? value)
    {
        if (string.IsNullOrEmpty(id)) return;
        var st = _vm.Stages.FirstOrDefault(s => string.Equals(s.ProcessorId, id, StringComparison.OrdinalIgnoreCase));
        if (st == null || !st.CanToggle || !st.ToggleEnabled) return;
        st.Enabled = value ?? !st.Enabled;
    }

    /// <summary>Subscribe/unsubscribe a client to the throttled input-level stream.</summary>
    private void SetMeter(StreamWriter writer, bool enabled)
    {
        lock (_clientsLock)
        {
            if (enabled) _meterClients.Add(writer);
            else _meterClients.Remove(writer);
        }
    }

    private void MeterTick()
    {
        StreamWriter[] targets;
        lock (_clientsLock)
        {
            if (_meterClients.Count == 0) return;
            targets = _meterClients.ToArray();
        }
        double level = _vm.IsRunning ? _vm.InLevel : 0.0;
        string json = JsonSerializer.Serialize(new { type = "meter", @in = level });
        foreach (var w in targets) Send(w, json);
    }

    private void ApplySet(string target, bool value)
    {
        switch (target)
        {
            case "mute": _vm.Muted = value; break;
            case "bypass": _vm.Bypassed = value; break;
            case "running": if (value != _vm.IsRunning) _vm.StartStopCommand.Execute(null); break;
        }
    }

    private void ApplyToggle(string target)
    {
        switch (target)
        {
            case "mute": _vm.Muted = !_vm.Muted; break;
            case "bypass": _vm.Bypassed = !_vm.Bypassed; break;
            case "running": _vm.StartStopCommand.Execute(null); break;
        }
    }

    private void SelectPreset(string name)
    {
        if (string.IsNullOrEmpty(name)) return;
        var item = _vm.Presets.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
        if (item != null) _vm.SelectedPreset = item;
    }

    private void CyclePreset(string dir)
    {
        if (_vm.Presets.Count == 0) return;
        int idx = _vm.SelectedPreset != null ? _vm.Presets.IndexOf(_vm.SelectedPreset) : -1;
        int delta = string.Equals(dir, "prev", StringComparison.OrdinalIgnoreCase) ? -1 : 1;
        int n = _vm.Presets.Count;
        int next = ((idx + delta) % n + n) % n;
        _vm.SelectedPreset = _vm.Presets[next];
    }

    // ── state push ──────────────────────────────────────────────────────────────────────────────
    private void OnVmPropertyChanged(object sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(MainViewModel.Muted):
            case nameof(MainViewModel.Bypassed):
            case nameof(MainViewModel.IsRunning):
            case nameof(MainViewModel.SelectedPreset):
                BroadcastState();
                break;
        }
    }

    /// <summary>Track each stage's Enabled so a stage toggled anywhere (MicForge UI, a preset load) is
    /// mirrored back to the deck. Re-subscribes when the stage set is rebuilt.</summary>
    private void WatchStages()
    {
        foreach (var s in _watchedStages) s.PropertyChanged -= OnStagePropertyChanged;
        _watchedStages.Clear();
        foreach (var s in _vm.Stages)
        {
            s.PropertyChanged += OnStagePropertyChanged;
            _watchedStages.Add(s);
        }
        BroadcastState();
    }

    private void OnStagePropertyChanged(object sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(StageViewModel.Enabled)) BroadcastState();
    }

    /// <summary>Push the current state to every connected client. Called on the UI thread (either
    /// from <see cref="OnVmPropertyChanged"/> or a dispatched command), so reading the VM is safe.</summary>
    private void BroadcastState()
    {
        string json = StateJson();
        StreamWriter[] snapshot;
        lock (_clientsLock) snapshot = _clients.ToArray();
        foreach (var w in snapshot) Send(w, json);
    }

    private string StateJson()
    {
        var obj = new
        {
            type = "state",
            mute = _vm.Muted,
            bypass = _vm.Bypassed,
            running = _vm.IsRunning,
            preset = _vm.SelectedPreset != null ? _vm.SelectedPreset.Name : "",
            presets = _vm.Presets.Select(p => p.Name).ToArray(),
            stages = _vm.Stages
                .Where(s => !string.IsNullOrEmpty(s.ProcessorId))
                .Select(s => new { id = s.ProcessorId, title = s.Title, enabled = s.Enabled, canToggle = s.CanToggle && s.ToggleEnabled })
                .ToArray(),
            @params = _vm.Stages
                .Where(s => !string.IsNullOrEmpty(s.ProcessorId))
                .SelectMany(s => s.Params.Select(p => new
                {
                    key = s.ProcessorId + "|" + p.Label,
                    stage = s.Title,
                    label = p.Label,
                    value = p.Value,
                    min = p.Min,
                    max = p.Max,
                    step = p.Step,
                    unit = p.Unit,
                }))
                .ToArray(),
        };
        return JsonSerializer.Serialize(obj);
    }

    private static string HelloJson()
    {
        var ver = Assembly.GetExecutingAssembly().GetName().Version;
        var obj = new { type = "hello", app = "MicForge", version = ver != null ? ver.ToString() : "0", protocol = Protocol };
        return JsonSerializer.Serialize(obj);
    }

    private static void Send(StreamWriter w, string json)
    {
        if (w == null) return;
        try { lock (w) w.WriteLine(json); }
        catch { /* client gone; its reader loop will remove it */ }
    }

    private static string GetStr(JsonElement e, string name)
        => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : "";

    /// <summary>A JSON bool field, or null if absent/not a boolean.</summary>
    private static bool? BoolOrNull(JsonElement e, string name)
        => e.TryGetProperty(name, out var v) && (v.ValueKind == JsonValueKind.True || v.ValueKind == JsonValueKind.False)
            ? v.GetBoolean() : (bool?)null;

    /// <summary>A JSON number field, or null if absent/not a number.</summary>
    private static double? DoubleOrNull(JsonElement e, string name)
        => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : (double?)null;

    public void Dispose()
    {
        try { _cts.Cancel(); } catch { }
        try { _vm.PropertyChanged -= OnVmPropertyChanged; } catch { }
        try { _meterTimer?.Stop(); } catch { }
        try { foreach (var s in _watchedStages) s.PropertyChanged -= OnStagePropertyChanged; _watchedStages.Clear(); } catch { }
        // Unblock the accept thread's WaitForConnection with a throwaway client connection.
        try
        {
            using var poke = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut);
            poke.Connect(200);
        }
        catch { }
        lock (_clientsLock)
        {
            foreach (var w in _clients) try { w.Dispose(); } catch { }
            _clients.Clear();
        }
        try { _cts.Dispose(); } catch { }
    }
}

using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using MicForge.Audio;
using NAudio.CoreAudioApi;

namespace MicForge.UI;

public sealed class MainForm : Form
{
    private readonly AudioEngine _engine = new();
    private readonly string _settingsPath =
        Path.Combine(AppContext.BaseDirectory, "micforge.json");

    private ComboBox _cboInput;
    private ComboBox _cboOutput;
    private Button _btnStart;
    private ProgressBar _pbIn;
    private ProgressBar _pbOut;
    private Label _lblGr;
    private FlowLayoutPanel _flpStages;
    private readonly System.Windows.Forms.Timer _meterTimer = new() { Interval = 33 };

    private List<MMDevice> _inputs = new();
    private List<MMDevice> _outputs = new();

    public MainForm()
    {
        Text = "MicForge";
        ClientSize = new Size(940, 720);
        MinimumSize = new Size(640, 480);
        StartPosition = FormStartPosition.CenterScreen;

        BuildUi();
        LoadDevices();

        var saved = Settings.Load(_settingsPath);
        saved?.ApplyTo(_engine.Chain);
        SelectDefaultDevices(saved);

        BuildStages();

        _meterTimer.Tick += MeterTick;
        _meterTimer.Start();

        FormClosing += (_, _) =>
        {
            _meterTimer.Stop();
            SaveSettings(_settingsPath);
            _engine.Dispose();
        };
    }

    // ---- top bar ----------------------------------------------------------

    private void BuildUi()
    {
        var top = new Panel { Dock = DockStyle.Top, Height = 96 };

        top.Controls.Add(new Label { Text = "Input", Location = new Point(8, 12), AutoSize = true });
        _cboInput = new ComboBox
        {
            Location = new Point(56, 8),
            Width = 250,
            DropDownStyle = ComboBoxStyle.DropDownList,
            DisplayMember = "FriendlyName"
        };
        top.Controls.Add(_cboInput);

        top.Controls.Add(new Label { Text = "Output", Location = new Point(318, 12), AutoSize = true });
        _cboOutput = new ComboBox
        {
            Location = new Point(372, 8),
            Width = 250,
            DropDownStyle = ComboBoxStyle.DropDownList,
            DisplayMember = "FriendlyName"
        };
        top.Controls.Add(_cboOutput);

        _btnStart = new Button { Text = "Start", Location = new Point(636, 7), Width = 96, Height = 26 };
        _btnStart.Click += (_, _) => ToggleRun();
        top.Controls.Add(_btnStart);

        top.Controls.Add(new Label { Text = "In", Location = new Point(8, 44), AutoSize = true });
        _pbIn = new ProgressBar { Location = new Point(32, 42), Width = 180, Height = 14, Maximum = 100 };
        top.Controls.Add(_pbIn);

        top.Controls.Add(new Label { Text = "Out", Location = new Point(224, 44), AutoSize = true });
        _pbOut = new ProgressBar { Location = new Point(252, 42), Width = 180, Height = 14, Maximum = 100 };
        top.Controls.Add(_pbOut);

        _lblGr = new Label { Text = "GR 0.0 dB", Location = new Point(444, 44), AutoSize = true };
        top.Controls.Add(_lblGr);

        var btnRefresh = new Button { Text = "Refresh", Location = new Point(8, 64), Width = 90, Height = 26 };
        btnRefresh.Click += (_, _) => { LoadDevices(); SelectDefaultDevices(null); };
        top.Controls.Add(btnRefresh);

        var btnSave = new Button { Text = "Save preset", Location = new Point(104, 64), Width = 100, Height = 26 };
        btnSave.Click += (_, _) => SavePresetDialog();
        top.Controls.Add(btnSave);

        var btnLoad = new Button { Text = "Load preset", Location = new Point(210, 64), Width = 100, Height = 26 };
        btnLoad.Click += (_, _) => LoadPresetDialog();
        top.Controls.Add(btnLoad);

        top.Controls.Add(new Label
        {
            Text = "Tip: set Output to \"CABLE Input\", then pick \"CABLE Output\" as your mic in Discord/OBS.",
            Location = new Point(320, 68), AutoSize = true, ForeColor = SystemColors.GrayText
        });

        _flpStages = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
            AutoScroll = true,
            Padding = new Padding(6)
        };

        Controls.Add(_flpStages);
        Controls.Add(top);
    }

    // ---- devices ----------------------------------------------------------

    private void LoadDevices()
    {
        _inputs = AudioEngine.InputDevices();
        _outputs = AudioEngine.OutputDevices();

        _cboInput.DataSource = null;
        _cboInput.DisplayMember = "FriendlyName";
        _cboInput.DataSource = _inputs;

        _cboOutput.DataSource = null;
        _cboOutput.DisplayMember = "FriendlyName";
        _cboOutput.DataSource = _outputs;
    }

    private void SelectDefaultDevices(Settings saved)
    {
        // Input: saved -> system default communications mic -> first.
        if (!SelectById(_cboInput, _inputs, saved?.InputDeviceId))
            SelectById(_cboInput, _inputs, AudioEngine.DefaultInputId());

        // Output: saved -> a VB-CABLE input -> leave whatever is default.
        if (!SelectById(_cboOutput, _outputs, saved?.OutputDeviceId))
        {
            var cable = _outputs.FirstOrDefault(d =>
                d.FriendlyName.IndexOf("CABLE Input", StringComparison.OrdinalIgnoreCase) >= 0);
            if (cable != null) _cboOutput.SelectedItem = cable;
        }
    }

    private static bool SelectById(ComboBox combo, List<MMDevice> list, string id)
    {
        if (string.IsNullOrEmpty(id)) return false;
        var match = list.FirstOrDefault(d => d.ID == id);
        if (match == null) return false;
        combo.SelectedItem = match;
        return true;
    }

    // ---- run --------------------------------------------------------------

    private void ToggleRun()
    {
        if (_engine.Running)
        {
            _engine.Stop();
            _btnStart.Text = "Start";
            _cboInput.Enabled = _cboOutput.Enabled = true;
            return;
        }

        if (_cboInput.SelectedItem is not MMDevice inDev ||
            _cboOutput.SelectedItem is not MMDevice outDev)
        {
            MessageBox.Show("Select an input and an output device first.", "MicForge");
            return;
        }

        try
        {
            _engine.Start(inDev, outDev);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Could not start audio");
            return;
        }

        _btnStart.Text = "Stop";
        _cboInput.Enabled = _cboOutput.Enabled = false;
    }

    private void MeterTick(object sender, EventArgs e)
    {
        _pbIn.Value = ToMeter(_engine.Chain.InputPeak);
        _pbOut.Value = ToMeter(_engine.Chain.OutputPeak);
        _lblGr.Text = $"GR {_engine.Chain.Compressor.GainReductionDb:0.0} dB";
    }

    private static int ToMeter(float peak)
    {
        if (peak <= 0.00001f) return 0;
        double db = 20 * Math.Log10(peak);           // ~ -100..0
        int v = (int)((db + 60) / 60 * 100);         // map -60..0 dB -> 0..100
        return Math.Clamp(v, 0, 100);
    }

    // ---- stage panel ------------------------------------------------------

    private void BuildStages()
    {
        var c = _engine.Chain;
        _flpStages.SuspendLayout();
        _flpStages.Controls.Clear();

        var gInput = NewStage("Input", out var colInput);
        AddSlider(colInput, "Gain dB", -24, 24, 0.5, () => c.InputGain.GainDb, v => c.InputGain.GainDb = v, "0.0");
        _flpStages.Controls.Add(gInput);

        var gHp = NewStage("High-Pass", out var colHp);
        AddEnable(colHp, c.HighPass);
        AddSlider(colHp, "Freq Hz", 20, 300, 5, () => c.HighPass.Frequency, v => c.HighPass.Frequency = v, "0");
        _flpStages.Controls.Add(gHp);

        var gNs = NewStage("Noise Suppression", out var colNs);
        AddEnable(colNs, c.Suppressor,
            c.Suppressor.Available ? "Enabled" : "Enabled  (drop rnnoise.dll next to the .exe)");
        _flpStages.Controls.Add(gNs);

        var gGate = NewStage("Noise Gate", out var colGate);
        AddEnable(colGate, c.Gate);
        AddSlider(colGate, "Thresh dB", -80, 0, 1, () => c.Gate.ThresholdDb, v => c.Gate.ThresholdDb = v, "0");
        AddSlider(colGate, "Attack ms", 0.1, 50, 0.1, () => c.Gate.AttackMs, v => c.Gate.AttackMs = v, "0.0");
        AddSlider(colGate, "Hold ms", 0, 500, 5, () => c.Gate.HoldMs, v => c.Gate.HoldMs = v, "0");
        AddSlider(colGate, "Release ms", 20, 1000, 10, () => c.Gate.ReleaseMs, v => c.Gate.ReleaseMs = v, "0");
        AddSlider(colGate, "Range dB", -90, 0, 2, () => c.Gate.RangeDb, v => c.Gate.RangeDb = v, "0");
        _flpStages.Controls.Add(gGate);

        var gEq = NewStage("Equalizer", out var colEq);
        AddEnable(colEq, c.Eq);
        AddSlider(colEq, "Low sh dB", -18, 18, 0.5, () => c.Eq.Bands[0].GainDb, v => { c.Eq.Bands[0].GainDb = v; c.Eq.UpdateAll(); }, "0.0");
        AddSlider(colEq, "P1 Hz", 100, 1000, 10, () => c.Eq.Bands[1].Freq, v => { c.Eq.Bands[1].Freq = v; c.Eq.UpdateAll(); }, "0");
        AddSlider(colEq, "P1 dB", -18, 18, 0.5, () => c.Eq.Bands[1].GainDb, v => { c.Eq.Bands[1].GainDb = v; c.Eq.UpdateAll(); }, "0.0");
        AddSlider(colEq, "P2 Hz", 500, 5000, 50, () => c.Eq.Bands[2].Freq, v => { c.Eq.Bands[2].Freq = v; c.Eq.UpdateAll(); }, "0");
        AddSlider(colEq, "P2 dB", -18, 18, 0.5, () => c.Eq.Bands[2].GainDb, v => { c.Eq.Bands[2].GainDb = v; c.Eq.UpdateAll(); }, "0.0");
        AddSlider(colEq, "P3 Hz", 2000, 12000, 100, () => c.Eq.Bands[3].Freq, v => { c.Eq.Bands[3].Freq = v; c.Eq.UpdateAll(); }, "0");
        AddSlider(colEq, "P3 dB", -18, 18, 0.5, () => c.Eq.Bands[3].GainDb, v => { c.Eq.Bands[3].GainDb = v; c.Eq.UpdateAll(); }, "0.0");
        AddSlider(colEq, "High sh dB", -18, 18, 0.5, () => c.Eq.Bands[4].GainDb, v => { c.Eq.Bands[4].GainDb = v; c.Eq.UpdateAll(); }, "0.0");
        _flpStages.Controls.Add(gEq);

        var gComp = NewStage("Compressor", out var colComp);
        AddEnable(colComp, c.Compressor);
        AddSlider(colComp, "Thresh dB", -60, 0, 1, () => c.Compressor.ThresholdDb, v => c.Compressor.ThresholdDb = v, "0");
        AddSlider(colComp, "Ratio :1", 1, 20, 0.5, () => c.Compressor.Ratio, v => c.Compressor.Ratio = v, "0.0");
        AddSlider(colComp, "Attack ms", 0.1, 100, 0.5, () => c.Compressor.AttackMs, v => c.Compressor.AttackMs = v, "0.0");
        AddSlider(colComp, "Release ms", 20, 1000, 10, () => c.Compressor.ReleaseMs, v => c.Compressor.ReleaseMs = v, "0");
        AddSlider(colComp, "Knee dB", 0, 24, 1, () => c.Compressor.KneeDb, v => c.Compressor.KneeDb = v, "0");
        AddSlider(colComp, "Makeup dB", 0, 24, 0.5, () => c.Compressor.MakeupDb, v => c.Compressor.MakeupDb = v, "0.0");
        _flpStages.Controls.Add(gComp);

        var gDe = NewStage("De-Esser", out var colDe);
        AddEnable(colDe, c.DeEsser);
        AddSlider(colDe, "Freq Hz", 3000, 10000, 100, () => c.DeEsser.Frequency, v => c.DeEsser.Frequency = v, "0");
        AddSlider(colDe, "Thresh dB", -60, 0, 1, () => c.DeEsser.ThresholdDb, v => c.DeEsser.ThresholdDb = v, "0");
        AddSlider(colDe, "Ratio :1", 1, 10, 0.5, () => c.DeEsser.Ratio, v => c.DeEsser.Ratio = v, "0.0");
        _flpStages.Controls.Add(gDe);

        var gLim = NewStage("Limiter", out var colLim);
        AddEnable(colLim, c.Limiter);
        AddSlider(colLim, "Ceiling dB", -12, 0, 0.1, () => c.Limiter.CeilingDb, v => c.Limiter.CeilingDb = v, "0.0");
        AddSlider(colLim, "Release ms", 10, 500, 5, () => c.Limiter.ReleaseMs, v => c.Limiter.ReleaseMs = v, "0");
        _flpStages.Controls.Add(gLim);

        var gOut = NewStage("Output", out var colOut);
        AddSlider(colOut, "Gain dB", -24, 24, 0.5, () => c.OutputGain.GainDb, v => c.OutputGain.GainDb = v, "0.0");
        _flpStages.Controls.Add(gOut);

        _flpStages.ResumeLayout();
    }

    private static GroupBox NewStage(string title, out FlowLayoutPanel col)
    {
        col = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            WrapContents = false,
            Location = new Point(8, 18)
        };
        var gb = new GroupBox
        {
            Text = title,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Margin = new Padding(6),
            MinimumSize = new Size(300, 0)
        };
        gb.Controls.Add(col);
        return gb;
    }

    private static void AddEnable(FlowLayoutPanel col, IAudioProcessor proc, string text = "Enabled")
    {
        var chk = new CheckBox { Text = text, Checked = proc.Enabled, AutoSize = true, Margin = new Padding(0, 0, 0, 2) };
        if (proc is NoiseSuppressor ns && !ns.Available)
        {
            chk.Enabled = false;
            chk.Checked = false;
        }
        chk.CheckedChanged += (_, _) => proc.Enabled = chk.Checked;
        col.Controls.Add(chk);
    }

    private static void AddSlider(FlowLayoutPanel col, string label, double min, double max, double step,
        Func<double> get, Action<double> set, string fmt)
    {
        var row = new Panel { Width = 288, Height = 30, Margin = new Padding(0) };

        var lbl = new Label { Text = label, Location = new Point(0, 8), Width = 72, AutoSize = false };

        int steps = Math.Max(1, (int)Math.Round((max - min) / step));
        var tb = new TrackBar
        {
            Location = new Point(72, 0),
            Width = 156,
            Height = 30,
            Minimum = 0,
            Maximum = steps,
            TickStyle = TickStyle.None,
            SmallChange = 1,
            LargeChange = Math.Max(1, steps / 10)
        };
        tb.Value = Math.Clamp((int)Math.Round((get() - min) / step), 0, steps);

        var val = new Label { Location = new Point(230, 8), Width = 56, AutoSize = false, Text = get().ToString(fmt) };

        tb.Scroll += (_, _) =>
        {
            double v = min + tb.Value * step;
            set(v);
            val.Text = v.ToString(fmt);
        };

        row.Controls.Add(lbl);
        row.Controls.Add(tb);
        row.Controls.Add(val);
        col.Controls.Add(row);
    }

    // ---- presets ----------------------------------------------------------

    private void SaveSettings(string path)
    {
        var s = Settings.CaptureFrom(_engine.Chain);
        s.InputDeviceId = (_cboInput.SelectedItem as MMDevice)?.ID;
        s.OutputDeviceId = (_cboOutput.SelectedItem as MMDevice)?.ID;
        try { s.Save(path); } catch { /* ignore */ }
    }

    private void SavePresetDialog()
    {
        using var dlg = new SaveFileDialog { Filter = "MicForge preset (*.json)|*.json", FileName = "preset.json" };
        if (dlg.ShowDialog(this) == DialogResult.OK)
            SaveSettings(dlg.FileName);
    }

    private void LoadPresetDialog()
    {
        using var dlg = new OpenFileDialog { Filter = "MicForge preset (*.json)|*.json" };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;

        var s = Settings.Load(dlg.FileName);
        if (s == null)
        {
            MessageBox.Show("Could not read that preset.", "MicForge");
            return;
        }
        s.ApplyTo(_engine.Chain);
        SelectDefaultDevices(s);
        BuildStages();
    }
}

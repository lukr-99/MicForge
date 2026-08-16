using System;
using System.Runtime.InteropServices;

namespace MicForge.Audio;

/// <summary>
/// AI noise suppression via RNNoise, loaded dynamically at runtime. Point it at a
/// 64-bit rnnoise.dll (exporting rnnoise_create / rnnoise_destroy / rnnoise_process_frame)
/// or drop one next to the exe. If none is loaded, <see cref="Available"/> is false and
/// this stage is a transparent pass-through.
///
/// RNNoise works on 480-sample frames at 48 kHz mono, scaled to the int16 range. A small
/// output FIFO primed with one frame of silence keeps the stream continuous (~10 ms).
/// </summary>
public sealed class NoiseSuppressor : IAudioProcessor
{
    private const int Frame = 480;
    private const float Scale = 32768f;

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate IntPtr CreateDel(IntPtr model);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void DestroyDel(IntPtr state);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate float ProcessDel(IntPtr state, float[] output, float[] input);

    private IntPtr _lib;
    private IntPtr _state;
    private CreateDel _create;
    private DestroyDel _destroy;
    private ProcessDel _process;

    private readonly float[] _in = new float[Frame];
    private readonly float[] _out = new float[Frame];
    private int _fill;

    private readonly float[] _fifo = new float[Frame * 4];
    private int _read, _write, _fifoCount;

    public NoiseSuppressor()
    {
        TryLoad("rnnoise");   // rnnoise.dll next to the exe / on PATH
        Prime();
    }

    public string Name => "Noise Suppression (RNNoise)";
    public bool Available { get; private set; }
    public bool Enabled { get; set; }
    public string LoadedPath { get; private set; }

    /// <summary>Load an rnnoise library from a name ("rnnoise") or a full .dll path.</summary>
    public bool TryLoad(string path)
    {
        Unload();
        if (string.IsNullOrWhiteSpace(path)) { Available = false; return false; }

        try
        {
            if (!NativeLibrary.TryLoad(path, out _lib)) { Available = false; Enabled = false; return false; }
            _create = Marshal.GetDelegateForFunctionPointer<CreateDel>(NativeLibrary.GetExport(_lib, "rnnoise_create"));
            _destroy = Marshal.GetDelegateForFunctionPointer<DestroyDel>(NativeLibrary.GetExport(_lib, "rnnoise_destroy"));
            _process = Marshal.GetDelegateForFunctionPointer<ProcessDel>(NativeLibrary.GetExport(_lib, "rnnoise_process_frame"));
            _state = _create(IntPtr.Zero);
            Available = _state != IntPtr.Zero;
            LoadedPath = Available ? path : null;
            if (!Available) Enabled = false;
            Prime();
            return Available;
        }
        catch
        {
            Unload();
            Available = false;
            Enabled = false;
            return false;
        }
    }

    private void Unload()
    {
        try { if (_state != IntPtr.Zero && _destroy != null) _destroy(_state); } catch { }
        _state = IntPtr.Zero;
        try { if (_lib != IntPtr.Zero) NativeLibrary.Free(_lib); } catch { }
        _lib = IntPtr.Zero;
        _create = null; _destroy = null; _process = null;
        LoadedPath = null;
    }

    private void Prime()
    {
        Array.Clear(_fifo, 0, _fifo.Length);
        _read = 0; _write = Frame; _fifoCount = Frame; _fill = 0;
    }

    public void Process(float[] buffer, int offset, int count)
    {
        if (!Enabled || !Available) return;

        for (int i = offset; i < offset + count; i++)
        {
            _in[_fill++] = buffer[i] * Scale;
            if (_fill == Frame)
            {
                _process(_state, _out, _in);
                for (int j = 0; j < Frame; j++)
                {
                    _fifo[_write] = _out[j] / Scale;
                    _write = (_write + 1) % _fifo.Length;
                    if (_fifoCount < _fifo.Length) _fifoCount++;
                }
                _fill = 0;
            }

            if (_fifoCount > 0)
            {
                buffer[i] = _fifo[_read];
                _read = (_read + 1) % _fifo.Length;
                _fifoCount--;
            }
        }
    }

    public void Reset()
    {
        if (Available) Prime();
    }
}

using System;
using System.Runtime.InteropServices;

namespace MicForge.Audio;

/// <summary>
/// AI noise suppression via RNNoise. Optional: if rnnoise.dll isn't next to the exe,
/// <see cref="Available"/> is false and this stage is a transparent pass-through.
///
/// RNNoise works on 480-sample frames at 48 kHz mono, with samples scaled to the
/// int16 range. We keep the stream continuous with a small output FIFO primed with
/// one frame of silence (adds ~10 ms latency only while enabled).
/// </summary>
public sealed class NoiseSuppressor : IAudioProcessor
{
    private const int Frame = 480;
    private const float Scale = 32768f;

    private readonly IntPtr _state;
    private readonly float[] _in = new float[Frame];
    private readonly float[] _out = new float[Frame];
    private int _fill;

    private readonly float[] _fifo = new float[Frame * 4];
    private int _read, _write, _fifoCount;

    public NoiseSuppressor()
    {
        try
        {
            _state = rnnoise_create(IntPtr.Zero);
            Available = _state != IntPtr.Zero;
        }
        catch (DllNotFoundException) { Available = false; }
        catch (BadImageFormatException) { Available = false; } // wrong bitness

        if (!Available) Enabled = false;
        Prime();
    }

    public string Name => "Noise Suppression (RNNoise)";
    public bool Available { get; }
    public bool Enabled { get; set; }

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
                rnnoise_process_frame(_state, _out, _in);
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
            // else: leave sample as-is (only happens on the very first frame)
        }
    }

    public void Reset()
    {
        if (Available) Prime();
    }

    [DllImport("rnnoise", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr rnnoise_create(IntPtr model);

    [DllImport("rnnoise", CallingConvention = CallingConvention.Cdecl)]
    private static extern void rnnoise_destroy(IntPtr state);

    [DllImport("rnnoise", CallingConvention = CallingConvention.Cdecl)]
    private static extern float rnnoise_process_frame(IntPtr state, float[] output, float[] input);
}

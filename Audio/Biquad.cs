using System;

namespace MicForge.Audio;

/// <summary>
/// A single biquad filter (RBJ audio-EQ cookbook), transposed direct form II.
/// One instance holds the state for one mono channel.
/// </summary>
public sealed class Biquad
{
    public enum FilterType { LowPass, HighPass, BandPass, Notch, Peaking, LowShelf, HighShelf }

    private double _b0 = 1, _b1, _b2, _a1, _a2;
    private double _z1, _z2;

    public void Set(FilterType type, double freq, double sampleRate, double q, double gainDb = 0)
    {
        // Clamp to sane range so knobs can't blow up the filter.
        if (freq < 10) freq = 10;
        if (freq > sampleRate * 0.49) freq = sampleRate * 0.49;
        if (q < 0.05) q = 0.05;

        double a = Math.Pow(10, gainDb / 40.0);
        double w0 = 2 * Math.PI * freq / sampleRate;
        double cos = Math.Cos(w0), sin = Math.Sin(w0);
        double alpha = sin / (2 * q);

        double b0, b1, b2, a0, a1, a2;
        switch (type)
        {
            case FilterType.LowPass:
                b0 = (1 - cos) / 2; b1 = 1 - cos; b2 = (1 - cos) / 2;
                a0 = 1 + alpha; a1 = -2 * cos; a2 = 1 - alpha;
                break;
            case FilterType.HighPass:
                b0 = (1 + cos) / 2; b1 = -(1 + cos); b2 = (1 + cos) / 2;
                a0 = 1 + alpha; a1 = -2 * cos; a2 = 1 - alpha;
                break;
            case FilterType.BandPass: // constant 0 dB peak gain
                b0 = alpha; b1 = 0; b2 = -alpha;
                a0 = 1 + alpha; a1 = -2 * cos; a2 = 1 - alpha;
                break;
            case FilterType.Notch:
                b0 = 1; b1 = -2 * cos; b2 = 1;
                a0 = 1 + alpha; a1 = -2 * cos; a2 = 1 - alpha;
                break;
            case FilterType.Peaking:
                b0 = 1 + alpha * a; b1 = -2 * cos; b2 = 1 - alpha * a;
                a0 = 1 + alpha / a; a1 = -2 * cos; a2 = 1 - alpha / a;
                break;
            case FilterType.LowShelf:
            {
                double sq = 2 * Math.Sqrt(a) * alpha;
                b0 = a * ((a + 1) - (a - 1) * cos + sq);
                b1 = 2 * a * ((a - 1) - (a + 1) * cos);
                b2 = a * ((a + 1) - (a - 1) * cos - sq);
                a0 = (a + 1) + (a - 1) * cos + sq;
                a1 = -2 * ((a - 1) + (a + 1) * cos);
                a2 = (a + 1) + (a - 1) * cos - sq;
                break;
            }
            case FilterType.HighShelf:
            {
                double sq = 2 * Math.Sqrt(a) * alpha;
                b0 = a * ((a + 1) + (a - 1) * cos + sq);
                b1 = -2 * a * ((a - 1) + (a + 1) * cos);
                b2 = a * ((a + 1) + (a - 1) * cos - sq);
                a0 = (a + 1) - (a - 1) * cos + sq;
                a1 = 2 * ((a - 1) - (a + 1) * cos);
                a2 = (a + 1) - (a - 1) * cos - sq;
                break;
            }
            default:
                b0 = 1; b1 = 0; b2 = 0; a0 = 1; a1 = 0; a2 = 0;
                break;
        }

        _b0 = b0 / a0; _b1 = b1 / a0; _b2 = b2 / a0;
        _a1 = a1 / a0; _a2 = a2 / a0;
    }

    public float Process(float x)
    {
        double y = _b0 * x + _z1;
        _z1 = _b1 * x - _a1 * y + _z2;
        _z2 = _b2 * x - _a2 * y;
        return (float)y;
    }

    public void Reset() => _z1 = _z2 = 0;
}

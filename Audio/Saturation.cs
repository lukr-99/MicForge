using System;

namespace MicForge.Audio;

/// <summary>
/// Soft-clip saturation for warmth/character. Drives the signal into a tanh curve
/// (adding gentle harmonics), normalised so level stays roughly constant, then blends
/// with the dry signal by Mix.
/// </summary>
public sealed class Saturation : IAudioProcessor
{
    public string Name => "Saturation";
    public bool Enabled { get; set; }
    public double DriveDb { get; set; } = 6;   // pre-gain into the curve
    public double Mix { get; set; } = 35;       // percent wet

    public void Process(float[] buffer, int offset, int count)
    {
        if (!Enabled) return;

        double g = Math.Pow(10, DriveDb / 20.0);
        double norm = Math.Tanh(g);
        if (norm < 1e-6) norm = 1e-6;
        double mix = Math.Clamp(Mix / 100.0, 0, 1);

        for (int i = offset; i < offset + count; i++)
        {
            double x = buffer[i];
            double wet = Math.Tanh(x * g) / norm;
            buffer[i] = (float)(x * (1 - mix) + wet * mix);
        }
    }

    public void Reset() { }
}

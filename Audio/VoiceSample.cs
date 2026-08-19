using System;
using System.Collections.Generic;
using System.IO;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace MicForge.Audio;

/// <summary>
/// Provides the looping mono preview sample for Crafting. Prefers a real recorded voice
/// (a bundled CC BY-NC Harvard-sentences excerpt, or a user-supplied WAV override), and
/// falls back to a synthesised vowel babble if neither is available.
/// </summary>
public static class VoiceSample
{
    /// <summary>Load the preview voice as mono float at the given rate; fall back to synthesis.</summary>
    public static float[] LoadOrGenerate(int sampleRate)
    {
        // 1) user override, 2) the bundled clip next to the exe.
        var candidates = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MicForge", "voice-sample.wav"),
            Path.Combine(AppContext.BaseDirectory, "voice-sample.wav"),
        };
        foreach (var path in candidates)
        {
            try
            {
                if (File.Exists(path))
                {
                    var s = LoadWav(path, sampleRate);
                    if (s is { Length: > 0 }) return s;
                }
            }
            catch { /* fall through */ }
        }
        return Generate(sampleRate);
    }

    private static float[] LoadWav(string path, int sampleRate)
    {
        using var reader = new AudioFileReader(path);
        ISampleProvider sp = reader;
        if (reader.WaveFormat.Channels == 2) sp = new StereoToMonoSampleProvider(sp);
        if (reader.WaveFormat.SampleRate != sampleRate) sp = new WdlResamplingSampleProvider(sp, sampleRate);

        var list = new List<float>(sampleRate * 12);
        var buf = new float[sampleRate];
        int n;
        while ((n = sp.Read(buf, 0, buf.Length)) > 0)
        {
            for (int i = 0; i < n; i++) list.Add(buf[i]);
            if (list.Count > sampleRate * 30) break;   // cap at 30 s
        }
        return list.ToArray();
    }

    // Vowel formant tables (F1, F2, F3 in Hz): a, e, i, o, u.
    private static readonly double[][] Vowels =
    {
        new[] { 700.0, 1220, 2600 },
        new[] { 530.0, 1840, 2480 },
        new[] { 270.0, 2290, 3010 },
        new[] { 570.0, 840, 2410 },
        new[] { 300.0, 870, 2240 },
    };

    public static float[] Generate(int sampleRate)
    {
        double dur = 4.0;
        int n = (int)(sampleRate * dur);
        var outp = new float[n];

        var f1 = new Biquad(); var f2 = new Biquad(); var f3 = new Biquad();
        void SetVowel(int idx)
        {
            f1.Set(Biquad.FilterType.BandPass, Vowels[idx][0], sampleRate, 8);
            f2.Set(Biquad.FilterType.BandPass, Vowels[idx][1], sampleRate, 10);
            f3.Set(Biquad.FilterType.BandPass, Vowels[idx][2], sampleRate, 12);
        }

        var rng = new Random(1234);
        const double sylLen = 0.34;   // seconds per syllable (incl. a short gap)
        const double onLen = 0.26;    // voiced portion of each syllable
        int vi = 0, lastSi = -1;
        double phase = 0, f0 = 110;

        SetVowel(0);
        for (int i = 0; i < n; i++)
        {
            double t = i / (double)sampleRate;
            int si = (int)(t / sylLen);
            if (si != lastSi)
            {
                lastSi = si;
                vi = (vi + 1) % Vowels.Length;
                SetVowel(vi);
                f0 = 100 + rng.NextDouble() * 45;   // vary pitch per syllable
            }

            // Glottal sawtooth source with a little vibrato.
            double vib = 1.0 + 0.02 * Math.Sin(2 * Math.PI * 5 * t);
            phase += f0 * vib / sampleRate;
            if (phase >= 1) phase -= 1;
            double src = 2 * phase - 1;

            double voiced = f1.Process((float)src) + 0.7 * f2.Process((float)src) + 0.4 * f3.Process((float)src);

            // Per-syllable Hann amplitude envelope (silent in the gap).
            double sp = t - si * sylLen;
            double env = sp < onLen ? 0.5 - 0.5 * Math.Cos(2 * Math.PI * (sp / onLen)) : 0.0;

            outp[i] = (float)(voiced * env);
        }

        // Normalise to about -12 dBFS.
        float peak = 1e-6f;
        for (int i = 0; i < n; i++) peak = Math.Max(peak, Math.Abs(outp[i]));
        float g = (float)(Math.Pow(10, -12 / 20.0) / peak);
        for (int i = 0; i < n; i++) outp[i] *= g;
        return outp;
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace MicForge.Audio;

/// <summary>
/// Preview voices for Crafting: bundled recorded clips, user-added WAVs (dropped in or
/// imported), and a synthesised fallback. Loads any of them as mono float at the engine rate.
/// </summary>
public static class VoiceSample
{
    public static string UserFolder
    {
        get
        {
            var d = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MicForge", "samples");
            try { Directory.CreateDirectory(d); } catch { }
            return d;
        }
    }

    private static string BuiltInFolder => Path.Combine(AppContext.BaseDirectory, "samples");

    /// <summary>Available preview voices: bundled first, then the user's, then synthesised.</summary>
    public static List<PreviewSample> List()
    {
        var list = new List<PreviewSample>();

        void AddFrom(string dir, bool user)
        {
            try
            {
                if (!Directory.Exists(dir)) return;
                foreach (var f in Directory.GetFiles(dir, "*.wav"))
                    list.Add(new PreviewSample(Path.GetFileNameWithoutExtension(f) + (user ? " (yours)" : ""), f));
            }
            catch { }
        }

        AddFrom(BuiltInFolder, false);
        AddFrom(UserFolder, true);
        list.Add(new PreviewSample("Synthesized", null));
        return list;
    }

    /// <summary>Load a preview voice as mono float at the given rate (synthesises on failure).</summary>
    public static float[] LoadFor(PreviewSample sample, int sampleRate)
    {
        if (sample != null && !sample.IsSynth)
        {
            try
            {
                var s = LoadFile(sample.Path, sampleRate);
                if (s is { Length: > 0 }) return s;
            }
            catch { /* fall back to synthesis */ }
        }
        return Generate(sampleRate);
    }

    public static float[] LoadFile(string path, int sampleRate)
    {
        using var reader = new AudioFileReader(path);
        ISampleProvider sp = reader;
        if (reader.WaveFormat.Channels == 2) sp = new StereoToMonoSampleProvider(sp);
        if (reader.WaveFormat.SampleRate != sampleRate) sp = new WdlResamplingSampleProvider(sp, sampleRate);

        var list = new List<float>(sampleRate * 16);
        var buf = new float[sampleRate];
        int n;
        while ((n = sp.Read(buf, 0, buf.Length)) > 0)
        {
            for (int i = 0; i < n; i++) list.Add(buf[i]);
            if (list.Count > sampleRate * 40) break;   // cap at 40 s
        }
        return list.ToArray();
    }

    // ---- synthesised fallback voice (formant vowel babble) ----
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
        const double sylLen = 0.34;
        const double onLen = 0.26;
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
                f0 = 100 + rng.NextDouble() * 45;
            }

            double vib = 1.0 + 0.02 * Math.Sin(2 * Math.PI * 5 * t);
            phase += f0 * vib / sampleRate;
            if (phase >= 1) phase -= 1;
            double src = 2 * phase - 1;

            double voiced = f1.Process((float)src) + 0.7 * f2.Process((float)src) + 0.4 * f3.Process((float)src);

            double sp = t - si * sylLen;
            double env = sp < onLen ? 0.5 - 0.5 * Math.Cos(2 * Math.PI * (sp / onLen)) : 0.0;
            outp[i] = (float)(voiced * env);
        }

        float peak = 1e-6f;
        for (int i = 0; i < n; i++) peak = Math.Max(peak, Math.Abs(outp[i]));
        float g = (float)(Math.Pow(10, -12 / 20.0) / peak);
        for (int i = 0; i < n; i++) outp[i] *= g;
        return outp;
    }
}

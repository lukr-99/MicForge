using System;
using System.Collections.Generic;
using System.Linq;

namespace MicForge.Audio;

/// <summary>
/// Measures perceived loudness (LUFS, ITU-R BS.1770 K-weighting) and, when auto-level is
/// on, slowly adjusts gain to hold a target loudness. Runs last in the chain. K-weighting
/// is measurement-only; it never colours the audio. Also reports integrated LUFS (gated),
/// loudness range (LRA) and true-peak (dBTP) for the analyzer.
/// </summary>
public sealed class LoudnessProcessor : IAudioProcessor
{
    private readonly double _sr;
    private readonly Biquad _stage1 = new();   // BS.1770 pre-filter (high shelf)
    private readonly Biquad _stage2 = new();   // BS.1770 RLB high-pass

    private double _msMomentary, _msShort;     // running mean-square of K-weighted signal
    private double _gainDb;                     // current auto-level gain

    // Integrated (gated over 400 ms blocks) + LRA (from 3 s short-term samples).
    private readonly int _blockLen, _stLen;
    private double _blockSum; private int _blockN;
    private int _stN;
    private readonly List<double> _blockMs = new();
    private readonly List<double> _stValues = new();

    // True-peak: 4x-oversampled inter-sample peak on the final output.
    private float _p1, _p2, _p3;
    private double _tp;

    public LoudnessProcessor(double sampleRate)
    {
        _sr = sampleRate;
        _blockLen = (int)(_sr * 0.4);
        _stLen = (int)(_sr * 0.1);
        // Fixed BS.1770 coefficients for 48 kHz (the engine's internal rate).
        _stage1.SetRaw(1.53512485958697, -2.69169618940638, 1.19839281085285, -1.69065929318241, 0.73248077421585);
        _stage2.SetRaw(1.0, -2.0, 1.0, -1.99004745483398, 0.99007225036621);
    }

    public string Name => "Loudness";
    public bool Enabled { get; set; } = true;   // always measures; AutoLevel controls gain

    public bool AutoLevel { get; set; }
    public double TargetLufs { get; set; } = -16;
    public double MaxGainDb { get; set; } = 12;

    public double MomentaryLufs { get; private set; } = -70;
    public double ShortTermLufs { get; private set; } = -70;
    public double IntegratedLufs { get; private set; } = -70;
    public double LoudnessRange { get; private set; }   // LU
    public double TruePeakDb { get; private set; } = -120;

    public void Process(float[] buffer, int offset, int count)
    {
        if (!Enabled) return;

        double aM = Math.Exp(-1.0 / (_sr * 0.4));   // 400 ms momentary
        double aS = Math.Exp(-1.0 / (_sr * 3.0));   // 3 s short-term
        double gCoef = Math.Exp(-1.0 / (_sr * 1.5)); // gain glide ~1.5 s
        double tpDecay = Math.Exp(-1.0 / (_sr * 1.5)); // true-peak hold ~1.5 s

        for (int i = offset; i < offset + count; i++)
        {
            double x = buffer[i];

            double k = _stage2.Process(_stage1.Process((float)x));
            double sq = k * k;
            _msMomentary = sq + (_msMomentary - sq) * aM;
            _msShort = sq + (_msShort - sq) * aS;

            double stLufs = -0.691 + 10 * Math.Log10(_msShort + 1e-12);

            double targetDb;
            if (AutoLevel && stLufs > -50)                       // only chase while speech is present
                targetDb = Math.Clamp(TargetLufs - stLufs, -MaxGainDb, MaxGainDb);
            else
                targetDb = AutoLevel ? _gainDb : 0;              // hold on silence; unity when off

            _gainDb = targetDb + (_gainDb - targetDb) * gCoef;
            if (AutoLevel) buffer[i] = (float)(x * Math.Pow(10, _gainDb / 20.0));

            // --- true peak on the final output (4x Catmull-Rom oversample) ---
            float outv = AutoLevel ? buffer[i] : (float)x;
            double inst = Math.Abs(outv);
            inst = Math.Max(inst, Math.Max(
                Math.Abs(Cubic(_p1, _p2, _p3, outv, 0.25)),
                Math.Max(Math.Abs(Cubic(_p1, _p2, _p3, outv, 0.5)),
                         Math.Abs(Cubic(_p1, _p2, _p3, outv, 0.75)))));
            _p1 = _p2; _p2 = _p3; _p3 = outv;
            _tp = inst > _tp ? inst : _tp * tpDecay;

            // --- integrated: accumulate 400 ms blocks ---
            _blockSum += sq;
            if (++_blockN >= _blockLen)
            {
                _blockMs.Add(_blockSum / _blockN);
                if (_blockMs.Count > 6000) _blockMs.RemoveAt(0);
                _blockSum = 0; _blockN = 0;
                RecomputeIntegrated();
            }

            // --- LRA: sample short-term loudness every 100 ms ---
            if (++_stN >= _stLen)
            {
                _stN = 0;
                if (stLufs > -70)
                {
                    _stValues.Add(stLufs);
                    if (_stValues.Count > 6000) _stValues.RemoveAt(0);
                    RecomputeLra();
                }
            }
        }

        MomentaryLufs = -0.691 + 10 * Math.Log10(_msMomentary + 1e-12);
        ShortTermLufs = -0.691 + 10 * Math.Log10(_msShort + 1e-12);
        TruePeakDb = 20 * Math.Log10(_tp + 1e-9);
    }

    // Catmull-Rom interpolation of the segment between b and c (neighbours a, d).
    private static double Cubic(double a, double b, double c, double d, double t)
    {
        return 0.5 * (2 * b + (-a + c) * t
            + (2 * a - 5 * b + 4 * c - d) * t * t
            + (-a + 3 * b - 3 * c + d) * t * t * t);
    }

    private void RecomputeIntegrated()
    {
        if (_blockMs.Count == 0) { IntegratedLufs = -70; return; }

        // Absolute gate at -70 LUFS.
        double sumAbs = 0; int nAbs = 0;
        foreach (var ms in _blockMs)
            if (-0.691 + 10 * Math.Log10(ms + 1e-12) >= -70) { sumAbs += ms; nAbs++; }
        if (nAbs == 0) { IntegratedLufs = -70; return; }

        // Relative gate at -10 LU below the ungated mean.
        double relThresh = (-0.691 + 10 * Math.Log10(sumAbs / nAbs)) - 10;
        double sumRel = 0; int nRel = 0;
        foreach (var ms in _blockMs)
        {
            double l = -0.691 + 10 * Math.Log10(ms + 1e-12);
            if (l >= -70 && l >= relThresh) { sumRel += ms; nRel++; }
        }
        IntegratedLufs = nRel > 0 ? -0.691 + 10 * Math.Log10(sumRel / nRel) : -70;
    }

    private void RecomputeLra()
    {
        if (_stValues.Count < 4) { LoudnessRange = 0; return; }
        double mean = _stValues.Average();
        var gated = _stValues.Where(v => v >= mean - 20).OrderBy(v => v).ToList();
        if (gated.Count < 4) { LoudnessRange = 0; return; }
        LoudnessRange = Percentile(gated, 0.95) - Percentile(gated, 0.10);
    }

    private static double Percentile(List<double> sorted, double p)
    {
        double idx = p * (sorted.Count - 1);
        int lo = (int)idx;
        int hi = Math.Min(lo + 1, sorted.Count - 1);
        return sorted[lo] + (sorted[hi] - sorted[lo]) * (idx - lo);
    }

    /// <summary>Clear the integrated / LRA / true-peak measurement (leaves live meters running).</summary>
    public void ResetMeasurement()
    {
        _blockMs.Clear(); _stValues.Clear();
        _blockSum = 0; _blockN = 0; _stN = 0; _tp = 0;
        IntegratedLufs = -70; LoudnessRange = 0; TruePeakDb = -120;
    }

    public void Reset()
    {
        _msMomentary = _msShort = 0;
        _gainDb = 0;
        _stage1.Reset();
        _stage2.Reset();
        _p1 = _p2 = _p3 = 0;
        MomentaryLufs = ShortTermLufs = -70;
        ResetMeasurement();
    }
}

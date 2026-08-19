namespace MicForge.Audio;

/// <summary>
/// A one-pole envelope follower with independent attack and release coefficients. Feed it a
/// value (usually <c>|sample|</c> or a level in dB) each sample; it rises toward the value at
/// the attack coefficient and falls at the release coefficient. Pass <c>attack = 0</c> for an
/// instant-attack peak follower.
/// </summary>
public sealed class EnvelopeFollower
{
    private double _env;

    public EnvelopeFollower(double initial = 0) => _env = initial;

    public double Value => _env;

    /// <summary>Process one input value with the given attack/release smoothing coefficients.</summary>
    public double Process(double value, double attackCoef, double releaseCoef)
    {
        double coef = value > _env ? attackCoef : releaseCoef;
        _env = value + (_env - value) * coef;
        return _env;
    }

    public void Reset(double value = 0) => _env = value;
}

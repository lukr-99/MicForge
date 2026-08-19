namespace MicForge.Audio;

/// <summary>
/// Base class for DSP chain stages. Implements the <see cref="IAudioProcessor.Enabled"/> gate
/// once so each effect only has to write <see cref="ProcessBlock"/> (called only while
/// enabled). Subclasses set their default <see cref="Enabled"/> state and <see cref="Name"/>.
/// </summary>
public abstract class AudioProcessorBase : IAudioProcessor
{
    public abstract string Name { get; }
    public bool Enabled { get; set; }

    public void Process(float[] buffer, int offset, int count)
    {
        if (!Enabled) { WhenDisabled(); return; }
        ProcessBlock(buffer, offset, count);
    }

    /// <summary>Process one buffer in place. Invoked only when <see cref="Enabled"/> is true.</summary>
    protected abstract void ProcessBlock(float[] buffer, int offset, int count);

    /// <summary>Optional hook to clear metering/readouts while the stage is disabled.</summary>
    protected virtual void WhenDisabled() { }

    public virtual void Reset() { }
}

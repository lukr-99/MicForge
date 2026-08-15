namespace MicForge.Audio;

/// <summary>One stage in the DSP chain. Processes a mono float buffer in place.</summary>
public interface IAudioProcessor
{
    string Name { get; }
    bool Enabled { get; set; }
    void Process(float[] buffer, int offset, int count);
    void Reset();
}

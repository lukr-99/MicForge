namespace MicForge.Audio;

/// <summary>One selectable Crafting preview voice (a bundled/user WAV, or the synthesised one).</summary>
public sealed class PreviewSample
{
    public PreviewSample(string name, string path) { Name = name; Path = path; }
    public string Name { get; }
    public string Path { get; }          // null = synthesised
    public bool IsSynth => Path == null;
    public override string ToString() => Name;
}

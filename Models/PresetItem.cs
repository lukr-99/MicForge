namespace MicForge;

/// <summary>One entry in the preset dropdown: a built-in preset (<see cref="Path"/> null) or a saved .json file.</summary>
public sealed class PresetItem
{
    public PresetItem(string name, string path) { Name = name; Path = path; }
    public string Name { get; }
    public string Path { get; }          // null = built-in
    public bool IsBuiltIn => Path == null;
    public override string ToString() => Name;
}

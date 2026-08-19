using System;
using System.Collections.Generic;
using System.IO;
using MicForge.Audio;

namespace MicForge;

/// <summary>One entry in the preset dropdown: a built-in preset or a saved .json file.</summary>
public sealed class PresetItem
{
    public PresetItem(string name, string path) { Name = name; Path = path; }
    public string Name { get; }
    public string Path { get; }          // null = built-in
    public bool IsBuiltIn => Path == null;
    public override string ToString() => Name;
}

/// <summary>
/// The user preset store: <c>%AppData%\MicForge\presets\*.json</c>. Presets saved here are
/// picked up automatically on startup and shown in the dropdown alongside the built-ins.
/// </summary>
public static class PresetLibrary
{
    public static string Folder
    {
        get
        {
            var d = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MicForge", "presets");
            try { Directory.CreateDirectory(d); } catch { }
            return d;
        }
    }

    public static List<PresetItem> List()
    {
        var list = new List<PresetItem>();
        foreach (var n in BuiltInPresets.Names) list.Add(new PresetItem(n, null));
        try
        {
            foreach (var f in Directory.GetFiles(Folder, "*.json"))
                list.Add(new PresetItem(Path.GetFileNameWithoutExtension(f), f));
        }
        catch { }
        return list;
    }
}

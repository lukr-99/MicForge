using System;
using System.Collections.Generic;
using System.IO;
using MicForge.Audio;

namespace MicForge;

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

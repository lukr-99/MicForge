using System;
using System.IO;

namespace MicForge.Audio;

/// <summary>
/// The built-in voice presets. Each is a full chain snapshot stored as JSON in
/// <c>assets/presets/*.json</c> (bundled next to the exe under <c>presets/</c>) and applied
/// via <see cref="Settings.ApplyTo"/> — so this is data, not code. The JSON is generated from
/// a live chain (see <c>PresetGenerator</c>); edit the values there, not here.
/// </summary>
public static class BuiltInPresets
{
    /// <summary>Display names, in dropdown order. Each maps to <c>presets/&lt;name&gt;.json</c>.</summary>
    public static readonly string[] Names =
    {
        "Default (flat)", "Broadcast Voice", "Podcast Warm", "Streaming",
        "Gaming Clarity", "Crisp HD", "Clean Comms"
    };

    private static string Folder => Path.Combine(AppContext.BaseDirectory, "presets");

    /// <summary>Apply the named built-in preset (a full chain snapshot) to the chain.</summary>
    public static void Apply(string name, DspChain chain)
    {
        try
        {
            var path = Path.Combine(Folder, name + ".json");
            if (File.Exists(path))
                Settings.FromJson(File.ReadAllText(path))?.ApplyTo(chain);
        }
        catch { /* a missing/corrupt preset file just no-ops */ }
    }
}

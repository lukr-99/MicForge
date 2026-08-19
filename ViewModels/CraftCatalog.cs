using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace MicForge.ViewModels;

/// <summary>
/// Loads the Crafting card definitions from <c>%AppData%\MicForge\craftcards.json</c>. The
/// file is user-editable; if it's missing, the built-in defaults are written there. Built-in
/// cards are refreshed to the current shipped tuning when the catalog version increases
/// (user-added cards — those with unknown ids — are always preserved).
/// </summary>
public static class CraftCatalog
{
    private const int CatalogVersion = 2;   // bump when re-tuning the built-in cards

    private static string Dir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MicForge");
    public static string FilePath => Path.Combine(Dir, "craftcards.json");
    private static string VersionPath => Path.Combine(Dir, "craftcards.version");

    public static List<CraftCardConfig> Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var list = JsonSerializer.Deserialize<List<CraftCardConfig>>(File.ReadAllText(FilePath));
                if (list != null && list.Count > 0)
                {
                    list = Normalize(list);

                    if (ReadVersion() < CatalogVersion)
                    {
                        // Refresh built-ins to the shipped tuning; keep user-added cards.
                        var builtinIds = new HashSet<string>(Defaults().Select(d => d.Id));
                        var custom = list.Where(c => !builtinIds.Contains(c.Id)).ToList();
                        var merged = Defaults().Concat(custom).ToList();
                        Save(merged);
                        return merged;
                    }

                    // Same version: merge in any newly-shipped defaults the file lacks.
                    var have = new HashSet<string>(list.Select(c => c.Id));
                    bool added = false;
                    foreach (var d in Defaults())
                        if (!have.Contains(d.Id)) { list.Add(d); added = true; }
                    if (added) Save(list);
                    return list;
                }
            }
        }
        catch { /* fall through to defaults */ }

        var def = Defaults();
        Save(def);
        return def;
    }

    public static void EnsureFile()
    {
        if (!File.Exists(FilePath)) Save(Defaults());
    }

    private static void Save(List<CraftCardConfig> list)
    {
        try
        {
            Directory.CreateDirectory(Dir);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(list, new JsonSerializerOptions { WriteIndented = true }));
            File.WriteAllText(VersionPath, CatalogVersion.ToString());
        }
        catch { /* non-fatal */ }
    }

    private static int ReadVersion()
    {
        try { if (File.Exists(VersionPath) && int.TryParse(File.ReadAllText(VersionPath).Trim(), out var v)) return v; }
        catch { }
        return 0;
    }

    private static List<CraftCardConfig> Normalize(List<CraftCardConfig> list)
    {
        int i = 0;
        foreach (var c in list)
        {
            if (string.IsNullOrEmpty(c.Id)) c.Id = "card" + i;
            if (string.IsNullOrEmpty(c.Category)) c.Category = CategoryFor(c.Id);
            if (c.Eq == null || c.Eq.Length != 5)
            {
                var eq = new double[5];
                if (c.Eq != null) Array.Copy(c.Eq, eq, Math.Min(5, c.Eq.Length));
                c.Eq = eq;
            }
            i++;
        }
        return list;
    }

    /// <summary>Ordered category names for the Crafting filter (plus an implicit "All").</summary>
    public static readonly string[] Categories = { "Tone", "Polish", "Fun", "FX" };

    private static readonly Dictionary<string, string> Cat = new()
    {
        ["bass"] = "Tone", ["thin"] = "Tone", ["warm"] = "Tone", ["bright"] = "Tone",
        ["pres"] = "Tone", ["air"] = "Tone", ["pod"] = "Tone", ["boomy"] = "Tone", ["edgy"] = "Tone",
        ["announcer"] = "Polish", ["narrator"] = "Polish", ["asmr"] = "Polish", ["crisp"] = "Polish",
        ["deepradio"] = "Polish", ["sparkle"] = "Polish", ["hd"] = "Polish", ["silky"] = "Polish",
        ["crystal"] = "Polish", ["bcpro"] = "Polish",
        ["deep"] = "Fun", ["chip"] = "Fun", ["robot"] = "Fun", ["whisp"] = "Fun", ["giant"] = "Fun",
        ["goblin"] = "Fun", ["alien"] = "Fun", ["monster"] = "Fun", ["helium"] = "Fun",
        ["radio"] = "FX", ["phone"] = "FX", ["mega"] = "FX", ["water"] = "FX",
        ["vintage"] = "FX", ["nasal"] = "FX", ["cave"] = "FX", ["intercom"] = "FX",
    };

    public static string CategoryFor(string id) =>
        id != null && Cat.TryGetValue(id, out var cat) ? cat : "Tone";

    private static string DefaultsPath =>
        Path.Combine(AppContext.BaseDirectory, "craftcards.default.json");

    /// <summary>
    /// The built-in cards, loaded from the bundled <c>craftcards.default.json</c> shipped next to
    /// the exe — data, not code. Regenerate that file from a live catalog rather than hand-editing
    /// values here. Category is baked into the file; any card missing one is classified by id.
    /// </summary>
    public static List<CraftCardConfig> Defaults()
    {
        try
        {
            var list = JsonSerializer.Deserialize<List<CraftCardConfig>>(File.ReadAllText(DefaultsPath));
            if (list != null && list.Count > 0)
            {
                foreach (var card in list)
                    if (string.IsNullOrEmpty(card.Category)) card.Category = CategoryFor(card.Id);
                return list;
            }
        }
        catch (Exception ex) { Log.Error("Failed to load bundled craft catalog defaults", ex); }
        return new List<CraftCardConfig>();
    }
}

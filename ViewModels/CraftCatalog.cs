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

    private static CraftCardConfig C(string id, string icon, string title, string blurb, string explanation,
        double pitch, double[] eq, double drive, double exciter = 0, double lowCut = 0, double highCut = 0) =>
        new()
        {
            Id = id, Icon = icon, Title = title, Blurb = blurb, Explanation = explanation,
            Pitch = pitch, Eq = eq, Drive = drive, Exciter = exciter, LowCut = lowCut, HighCut = highCut
        };

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

    // Crafting EQ bands sit at fixed frequencies: [0]=low shelf 120, [1]=low-mid 500,
    // [2]=mid/honk 1700, [3]=presence 4000, [4]=air (high shelf 10k) or the low-pass cutoff.
    public static List<CraftCardConfig> Defaults()
    {
        var list = new List<CraftCardConfig>
        {
            // ---- Tone ----
            C("bass", "🔊", "Bass Boost", "Fuller, deeper low end.",
                "Lifts the low shelf so your voice sits bigger and warmer. Great for thin mics; too much gets muddy.",
                0, new[]{ 6.0, 0, 0, 0, 0 }, 0),
            C("thin", "🍃", "Bass Cut", "Lighter, thinner — less rumble.",
                "Cuts the low shelf and rolls off the very bottom to clear mud and proximity boom.",
                0, new[]{ -6.0, 0, 0, 0, 0 }, 0, 0, 120),
            C("warm", "🔥", "Warm", "Cozy, radio-warm tone.",
                "A gentle low lift with the highs eased back and a touch of tube warmth.",
                0, new[]{ 2.0, 1, 0, -1, 0 }, 1),
            C("bright", "✨", "Bright", "Crisp and clear up top.",
                "Adds presence and air so consonants cut through. Back off if it starts to sound sibilant.",
                0, new[]{ 0.0, 0, 0, 3, 4 }, 0),
            C("pres", "🎯", "Presence", "Voice pushed forward.",
                "Boosts the upper-mids where intelligibility lives, making you sound closer and clearer.",
                0, new[]{ 0.0, 0, 2, 3, 0 }, 0),
            C("air", "💨", "Air", "Open, airy sheen.",
                "A high-shelf lift above 10 kHz for a breezy, expensive-sounding top end.",
                0, new[]{ 0.0, 0, 0, 0, 5 }, 0),
            C("pod", "🎙️", "Podcast", "Smooth, full, professional.",
                "A balanced lift — body, presence and a little air, plus gentle sheen — for a polished spoken tone.",
                0, new[]{ 3.0, 1, 0, 1, 1 }, 0, 12),
            C("boomy", "🥁", "Boomy", "Chesty and thick.",
                "Heavy low and low-mid lift for a big, chesty broadcast weight. Use sparingly.",
                0, new[]{ 5.0, 3, 0, 0, 0 }, 0),
            C("edgy", "🔪", "Edgy", "Aggressive and cutting.",
                "Hard presence with saturation and exciter for a raw, in-your-face delivery.",
                0, new[]{ 0.0, 0, 3, 3, 0 }, 4, 25),

            // ---- Polish ----
            C("announcer", "📣", "Announcer", "Big movie-trailer voice.",
                "Drops your pitch, adds deep chest body and forward presence with a little weight and sheen — larger than life.",
                -3, new[]{ 5.0, 1, 1, 3, 1 }, 2, 15),
            C("narrator", "📖", "Narrator", "Warm storyteller.",
                "Slightly lower pitch, rich warm lows and gentle presence with a hint of tape warmth — relaxed and engaging.",
                -1, new[]{ 4.0, 1, 0, 1, 0 }, 3, 8),
            C("asmr", "🌙", "ASMR", "Ultra-soft and close.",
                "Trims the low rumble and pushes lots of delicate high-frequency detail with heavy exciter — intimate and tingly.",
                0, new[]{ -2.0, 0, -1, 1, 7 }, 0, 60, 120),
            C("crisp", "🧊", "Crisp", "Clean and articulate.",
                "A tidy presence-and-air lift with a touch less low-mid and some exciter for a clear, modern sound.",
                0, new[]{ -1.0, -2, 1, 3, 3 }, 0, 20),
            C("deepradio", "🎚️", "Deep Radio", "Smooth late-night DJ.",
                "A slight pitch drop with rich lows, controlled presence and a little sheen — the classic FM night-show voice.",
                -2, new[]{ 4.0, 1, 0, 1, -1 }, 2, 10),
            C("sparkle", "🌟", "Sparkle", "Shimmering high-end sheen.",
                "Uses the harmonic exciter to add brand-new sparkle up top — brighter and livelier than a plain treble boost.",
                0, new[]{ 0.0, 0, 0, 1, 2 }, 0, 45),
            C("hd", "💎", "HD Voice", "Crisp, modern, hi-fi.",
                "Presence and air plus a good dose of exciter for a clean, detailed, high-definition sound.",
                0, new[]{ 0.0, 0, 1, 2, 3 }, 0, 32),
            C("silky", "🧵", "Silky", "Smooth but detailed.",
                "A little low-end body with gentle exciter sheen — smooth yet articulate.",
                0, new[]{ 1.0, 0, 0, 0, 1 }, 0, 22),
            C("crystal", "🔮", "Crystal", "Ultra-clear and bright.",
                "Strong presence, air and exciter for a glassy, crystal-clear voice. Ease off if it gets fizzy.",
                0, new[]{ 0.0, 0, 2, 3, 3 }, 0, 40),
            C("bcpro", "📡", "Broadcast Pro", "Polished on-air sound.",
                "Full body, forward presence and a hint of exciter — a finished, broadcast-ready tone.",
                0, new[]{ 3.0, 0, 1, 2, 1 }, 1, 20),

            // ---- Fun ----
            C("deep", "🧛", "Deep Voice", "Lower, bigger, villainous.",
                "Shifts your pitch down a few semitones with a low-end lift — instant movie-villain voice.",
                -4, new[]{ 3.0, 0, 0, 0, 0 }, 0),
            C("chip", "🐿️", "Chipmunk", "High and squeaky.",
                "Shifts your pitch way up for a cartoon-critter voice.",
                5, new[]{ 0.0, 0, 0, 0, 0 }, 0),
            C("robot", "🤖", "Robot", "Gritty machine voice.",
                "A small downward pitch shift plus heavy saturation and a mid push for a mechanical tone.",
                -2, new[]{ 0.0, 0, 2, 1, 0 }, 8),
            C("whisp", "👻", "Whisper", "Soft, breathy, ghostly.",
                "Strips out the body and rumble, pushes lots of breathy air and exciter, and rolls off the bottom so almost none of the voiced fullness is left — a hushed whisper.",
                0, new[]{ -9.0, -3, -2, 2, 7 }, 0, 55, 320),
            C("giant", "🗿", "Giant", "Huge and slow.",
                "A big downward pitch shift with a heavy low-end lift and a darker top — you become a towering giant.",
                -7, new[]{ 5.0, 0, -1, 0, -2 }, 0, 0, 0, 6000),
            C("goblin", "👺", "Goblin", "Small, raspy, mischievous.",
                "Pitches up and adds grit and nasal mids for a scrappy little creature.",
                3, new[]{ -2.0, 0, 3, 2, 0 }, 6),
            C("alien", "👽", "Alien", "Otherworldly and hollow.",
                "An odd scoop with a metallic presence peak, grit and a pitch shift for an off-world tone.",
                2, new[]{ -4.0, 0, -3, 4, 2 }, 3),
            C("monster", "😈", "Monster", "Deep, growly, menacing.",
                "A deep pitch drop with heavy saturation, low-mid weight and a throaty, rolled-off top — a proper monster growl.",
                -6, new[]{ 4.0, 2, 0, 0, -2 }, 9, 0, 0, 5000),
            C("helium", "🎈", "Helium", "Party-balloon squeak.",
                "An extreme upward pitch shift for a silly, high, squeaky voice.",
                8, new[]{ 0.0, 0, 1, 1, 0 }, 0),

            // ---- FX ----
            C("radio", "📻", "Radio", "Old-school broadcast band.",
                "Band-limits to the midrange like an AM radio and adds grit — that classic tinny, distorted broadcast sound.",
                0, new[]{ 0.0, 0, 3, 1, 0 }, 6, 0, 300, 5000),
            C("phone", "☎️", "Telephone", "Tinny call-quality voice.",
                "Hard-limits the sound to the ~300 Hz–3.4 kHz phone band with a mid push — a genuine telephone/comms effect.",
                0, new[]{ 0.0, 1, 4, 1, 0 }, 2, 0, 350, 3400),
            C("mega", "📢", "Megaphone", "Loud-hailer honk.",
                "Cuts the lows and highs to a narrow band, cranks a resonant nasal midrange and drives it into distortion — a real bullhorn.",
                0, new[]{ 0.0, 3, 9, 2, 0 }, 8, 0, 500, 3800),
            C("water", "🌊", "Underwater", "Muffled and submerged.",
                "Rolls everything above ~600 Hz right off and thickens the lows so you sound properly submerged.",
                0, new[]{ 3.0, 2, -3, 0, 0 }, 0, 0, 0, 600),
            C("vintage", "📼", "Vintage", "Warm tape character.",
                "Band-limits the extremes and adds tape-style saturation for a nostalgic, lo-fi warmth.",
                0, new[]{ 1.0, 1, 0, -2, 0 }, 5, 0, 150, 5500),
            C("nasal", "👃", "Nasal", "Honky midrange.",
                "Cranks a narrow 1.7 kHz honk for a deliberately nasal, pinched effect.",
                0, new[]{ 0.0, 2, 7, 0, 0 }, 0),
            C("cave", "🕳️", "Cave", "Distant and boomy.",
                "Boosts the lows and rolls off the top so you sound far away in a big, dark stone room.",
                -1, new[]{ 4.0, 0, -2, -2, 0 }, 0, 0, 0, 3500),
            C("intercom", "🛰️", "Intercom", "PA / announcement system.",
                "A band-limited, gritty midrange like a store or spaceship intercom.",
                0, new[]{ 0.0, 1, 4, 1, 0 }, 4, 0, 400, 3500),
        };

        foreach (var card in list) card.Category = CategoryFor(card.Id);
        return list;
    }
}

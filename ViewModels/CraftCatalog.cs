using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace MicForge.ViewModels;

/// <summary>Serializable definition of one Crafting card (edited via craftcards.json).</summary>
public sealed class CraftCardConfig
{
    public string Id { get; set; }
    public string Icon { get; set; }
    public string Title { get; set; }
    public string Category { get; set; }             // Tone / Polish / Fun / FX
    public string Blurb { get; set; }
    public string Explanation { get; set; }
    public double Pitch { get; set; }
    public double[] Eq { get; set; } = new double[5];   // low shelf, low-mid, mid, presence, air
    public double Drive { get; set; }
    public double Exciter { get; set; }                  // harmonic exciter amount (percent)
}

/// <summary>
/// Loads the Crafting card definitions from <c>%AppData%\MicForge\craftcards.json</c>. The
/// file is user-editable; if it's missing, the built-in defaults are written there so it can
/// be tweaked or extended.
/// </summary>
public static class CraftCatalog
{
    public static string FilePath
    {
        get
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MicForge");
            return Path.Combine(dir, "craftcards.json");
        }
    }

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
                    // Merge in any newly-shipped default cards the user's file doesn't have yet.
                    var have = new HashSet<string>();
                    foreach (var c in list) have.Add(c.Id);
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
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
            File.WriteAllText(FilePath,
                JsonSerializer.Serialize(list, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { /* non-fatal */ }
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
        double pitch, double[] eq, double drive, double exciter = 0) =>
        new() { Id = id, Icon = icon, Title = title, Blurb = blurb, Explanation = explanation, Pitch = pitch, Eq = eq, Drive = drive, Exciter = exciter };

    /// <summary>Ordered category names for the Crafting filter (plus an implicit "All").</summary>
    public static readonly string[] Categories = { "Tone", "Polish", "Fun", "FX" };

    // Built-in card → category. Used to categorise defaults and to backfill older files.
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

    public static List<CraftCardConfig> Defaults()
    {
        var list = new List<CraftCardConfig>
        {
        C("bass", "🔊", "Bass Boost", "Fuller, deeper low end.",
            "Lifts the low shelf so your voice sits bigger and warmer. Great for thin mics; too much gets muddy.",
            0, new[]{ 6.0, 0, 0, 0, 0 }, 0),
        C("thin", "🍃", "Bass Cut", "Lighter, thinner — less rumble.",
            "Pulls the low shelf down to clear mud and proximity boom. Handy when you're right up on the mic.",
            0, new[]{ -6.0, 0, 0, 0, 0 }, 0),
        C("warm", "🔥", "Warm", "Cozy, radio-warm tone.",
            "A gentle low-end lift with the highs eased back for a smooth, intimate sound.",
            0, new[]{ 2.0, 1, 0, -1, 0 }, 0),
        C("bright", "✨", "Bright", "Crisp and clear up top.",
            "Adds presence and air so consonants cut through. Back off if it starts to sound sibilant.",
            0, new[]{ 0.0, 0, 0, 3, 4 }, 0),
        C("pres", "🎯", "Presence", "Voice pushed forward.",
            "Boosts the upper-mids where intelligibility lives, making you sound closer and clearer.",
            0, new[]{ 0.0, 0, 2, 3, 0 }, 0),
        C("air", "💨", "Air", "Open, airy sheen.",
            "A high-shelf lift above 10 kHz for a breezy, expensive-sounding top end.",
            0, new[]{ 0.0, 0, 0, 0, 5 }, 0),
        C("radio", "📻", "Radio", "Old-school broadcast band.",
            "Scoops the extremes and pushes the mids, with a little grit — classic AM-radio character.",
            0, new[]{ -6.0, -1, 3, 1, -6 }, 6),
        C("phone", "☎️", "Telephone", "Tinny call-quality voice.",
            "Hard band-limits to the midrange like a phone line — great for a comms/robotic effect.",
            0, new[]{ -14.0, 0, 5, 2, -14 }, 0),
        C("mega", "📢", "Megaphone", "Loud-hailer honk.",
            "A nasal midrange bump plus saturation for a bullhorn shout.",
            0, new[]{ -8.0, 0, 6, 2, -4 }, 5),
        C("water", "🌊", "Underwater", "Muffled and submerged.",
            "Rolls off the top so you sound like you're talking underwater or through a wall.",
            0, new[]{ 2.0, 0, -4, -8, -10 }, 0),
        C("deep", "🧛", "Deep Voice", "Lower, bigger, villainous.",
            "Shifts your pitch down a few semitones with a low-end lift — instant movie-trailer voice.",
            -4, new[]{ 3.0, 0, 0, 0, 0 }, 0),
        C("chip", "🐿️", "Chipmunk", "High and squeaky.",
            "Shifts your pitch way up for a cartoon-critter voice.",
            5, new[]{ 0.0, 0, 0, 0, 0 }, 0),
        C("robot", "🤖", "Robot", "Gritty machine voice.",
            "A small downward pitch shift plus heavy saturation and a mid push for a mechanical tone.",
            -2, new[]{ 0.0, 0, 2, 1, 0 }, 8),
        C("whisp", "👻", "Whisper", "Soft, breathy, ghostly.",
            "Thins the body and lifts the air for a hushed, breathy character.",
            0, new[]{ -3.0, 0, 0, 2, 4 }, 0),
        C("pod", "🎙️", "Podcast", "Smooth, full, professional.",
            "A balanced lift — a little body, a little presence, a little air — for a polished spoken-word tone.",
            0, new[]{ 3.0, 1, 0, 1, 1 }, 0),
        C("announcer", "📣", "Announcer", "Big movie-trailer voice.",
            "Deepens the pitch slightly and adds body and presence for an authoritative, larger-than-life delivery.",
            -2, new[]{ 4.0, 1, 1, 2, 0 }, 0),
        C("giant", "🗿", "Giant", "Huge and slow.",
            "A big downward pitch shift with a heavy low-end lift — you become a towering giant.",
            -7, new[]{ 5.0, 0, -1, 0, -2 }, 0),
        C("goblin", "👺", "Goblin", "Small, raspy, mischievous.",
            "Pitches up a little and adds grit and nasal mids for a scrappy creature voice.",
            3, new[]{ -2.0, 0, 3, 2, 0 }, 6),
        C("alien", "👽", "Alien", "Otherworldly and hollow.",
            "An odd scoop with a metallic presence peak and a light pitch shift for an off-world tone.",
            2, new[]{ -4.0, 0, -3, 4, 2 }, 3),
        C("monster", "😈", "Monster", "Deep, growly, menacing.",
            "A deep pitch drop with saturation and low-mid weight — a proper monster growl.",
            -6, new[]{ 4.0, 2, 0, 0, -2 }, 9),
        C("narrator", "📖", "Narrator", "Warm storyteller.",
            "Rich lows and gentle presence for a relaxed, engaging read-aloud voice.",
            -1, new[]{ 3.0, 1, 0, 1, 0 }, 0),
        C("asmr", "🌙", "ASMR", "Ultra-soft and close.",
            "Cuts the low rumble and lifts the delicate air for an intimate, tingly whisper.",
            0, new[]{ -2.0, 0, -1, 1, 5 }, 0),
        C("vintage", "📼", "Vintage", "Warm tape character.",
            "Rolls off the extremes and adds tape-style saturation for a nostalgic, lo-fi warmth.",
            0, new[]{ 1.0, 1, 0, -2, -4 }, 5),
        C("crisp", "🧊", "Crisp", "Clean and articulate.",
            "A tidy presence-and-air lift with a touch less low-mid for a clear, modern sound.",
            0, new[]{ -1.0, -2, 1, 3, 3 }, 0),
        C("boomy", "🥁", "Boomy", "Chesty and thick.",
            "Heavy low and low-mid lift for a big, chesty broadcast weight. Use sparingly.",
            0, new[]{ 5.0, 3, 0, 0, 0 }, 0),
        C("nasal", "👃", "Nasal", "Honky midrange.",
            "Pushes the 1–2 kHz honk for a deliberately nasal, pinched effect.",
            0, new[]{ 0.0, 2, 6, 0, -2 }, 0),
        C("cave", "🕳️", "Cave", "Distant and boomy.",
            "Boosts the lows and cuts the top so you sound far away in a big stone room.",
            -1, new[]{ 4.0, 0, -2, -4, -6 }, 0),
        C("intercom", "🛰️", "Intercom", "PA / announcement system.",
            "Band-limited mids with grit, like a store or spaceship intercom.",
            0, new[]{ -10.0, 0, 4, 1, -8 }, 4),
        C("helium", "🎈", "Helium", "Party-balloon squeak.",
            "An extreme upward pitch shift for a silly, high, squeaky voice.",
            8, new[]{ 0.0, 0, 1, 1, 0 }, 0),
        C("deepradio", "🎚️", "Deep Radio", "Smooth late-night DJ.",
            "A slight pitch drop with rich lows and controlled presence — the classic FM night-show voice.",
            -2, new[]{ 4.0, 1, 0, 1, -1 }, 2),
        C("sparkle", "🌟", "Sparkle", "Shimmering high-end sheen.",
            "Uses the harmonic exciter to add brand-new sparkle up top — brighter and livelier than a plain treble boost.",
            0, new[]{ 0.0, 0, 0, 1, 2 }, 0, 40),
        C("hd", "💎", "HD Voice", "Crisp, modern, hi-fi.",
            "Presence and air plus a touch of exciter for a clean, detailed, high-definition sound.",
            0, new[]{ 0.0, 0, 1, 2, 3 }, 0, 30),
        C("silky", "🧵", "Silky", "Smooth but detailed.",
            "A little low-end body with gentle exciter sheen — smooth yet articulate.",
            0, new[]{ 1.0, 0, 0, 0, 1 }, 0, 20),
        C("crystal", "🔮", "Crystal", "Ultra-clear and bright.",
            "Strong presence, air and exciter for a glassy, crystal-clear voice. Ease off if it gets fizzy.",
            0, new[]{ 0.0, 0, 2, 3, 3 }, 0, 35),
        C("bcpro", "📡", "Broadcast Pro", "Polished on-air sound.",
            "Full body, forward presence and a hint of exciter — a finished, broadcast-ready tone.",
            0, new[]{ 3.0, 0, 1, 2, 1 }, 0, 20),
        C("edgy", "🔪", "Edgy", "Aggressive and cutting.",
            "Hard presence with saturation and exciter for a raw, in-your-face delivery.",
            0, new[]{ 0.0, 0, 3, 3, 0 }, 4, 30),
        };
        foreach (var card in list) card.Category = CategoryFor(card.Id);
        return list;
    }
}

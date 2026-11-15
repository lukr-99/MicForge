using System;
using System.Collections.Generic;
using System.Linq;

namespace MicForge;

/// <summary>One external link on the About page.</summary>
public sealed record AboutLink(string Label, string Detail, string Url);

/// <summary>One fact about the app on the About page.</summary>
public sealed record AboutFact(string Label, string Value);

/// <summary>One credit or licence line on the About page.</summary>
public sealed record AboutCredit(string Name, string Role, string License, string Url);

/// <summary>
/// The single place for the About page's text, author details and links. Change the author's
/// name or any link here and the page follows.
/// </summary>
public static class AboutInfo
{
    public const string AppName = "MicForge";
    public const string Tagline = "A real-time microphone processor for Windows.";
    public const string Description =
        "MicForge cleans and shapes your mic with a configurable chain of DSP stages and sends the result " +
        "to a virtual microphone, so any app can use it.";

    public const string AuthorName = "Lukáš Krejčí";
    public const string AuthorLine = "Made by " + AuthorName;
    public const string FeedbackLine = "Feedback is welcome: what works, what does not, what is missing.";

    public static readonly AboutLink Instagram =
        new("Instagram", "@lukas9krejci", "https://www.instagram.com/lukas9krejci/");
    public static readonly AboutLink GitHub =
        new("GitHub", "lukr-99", "https://github.com/lukr-99");
    public static readonly AboutLink Issues =
        new("Issues", "Report or suggest", "https://github.com/lukr-99/MicForge/issues");

    public static IReadOnlyList<AboutLink> Links { get; } = new[] { Instagram, GitHub, Issues };

    /// <summary>Short facts about the app, as label and value pairs.</summary>
    public static IReadOnlyList<AboutFact> Facts(string version) => new[]
    {
        new AboutFact("Version", "v" + version),
        new AboutFact("Processing", "23 DSP stages"),
        new AboutFact("Audio format", "Mono, 32-bit float, 48 kHz"),
        new AboutFact("Runs on", "Windows 10 and 11, x64"),
    };

    /// <summary>Licences and credits for what ships with the app.</summary>
    public static IReadOnlyList<AboutCredit> Credits { get; } = new[]
    {
        new AboutCredit("MicForge", "This app", "PolyForm Noncommercial 1.0.0",
            "https://polyformproject.org/licenses/noncommercial/1.0.0"),
        new AboutCredit("RNNoise", "AI noise suppression (rnnoise.dll)", "BSD, Xiph.Org and contributors",
            "https://github.com/xiph/rnnoise"),
        new AboutCredit("NAudio", "WASAPI capture and playback", "MIT", "https://github.com/naudio/NAudio"),
        new AboutCredit("CommunityToolkit.Mvvm", "View-model source generators", "MIT",
            "https://github.com/CommunityToolkit/dotnet"),
        new AboutCredit("Microsoft.Extensions.DependencyInjection", "Service wiring", "MIT",
            "https://github.com/dotnet/runtime"),
        new AboutCredit("DotNetLib", "Tray, update and settings kit", "By the same author",
            "https://github.com/lukr-99"),
        new AboutCredit("Harvard sentences clip", "Crafting preview voice, by Philippa Demonte, University of Salford",
            "CC BY-NC 4.0", "https://doi.org/10.17866/rd.salford.7857770.v1"),
    };

    /// <summary>True only for links the About page itself offers, so a click can never open anything else.</summary>
    public static bool IsKnownUrl(string url) =>
        !string.IsNullOrEmpty(url) &&
        (Links.Any(l => l.Url == url) || Credits.Any(c => c.Url == url));
}

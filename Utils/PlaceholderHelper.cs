using System.Text.RegularExpressions;
using SwiftlyS2.Shared.Players;
using TagSystem.Models;

namespace TagSystem.Utils;

/// <summary>
/// {PLACEHOLDER} degistirici. Tek geciste (single-pass) calisir: bir placeholder'in degeri
/// tekrar taranmaz. Boylece oyuncu ismine/mesajina "{STEAMID64}" yazilarak enjeksiyon yapilamaz.
/// Bilinmeyen placeholder'lar oldugu gibi birakilir.
/// </summary>
public static class PlaceholderHelper
{
    private static readonly Regex TokenRegex = new(@"\{(?<k>[A-Z0-9_]+)\}", RegexOptions.Compiled);
    private static readonly Regex EffectRegex = new(@"\{(?<effect>[A-Za-z0-9_]+):(?<text>[^}]+)\}", RegexOptions.Compiled);

    /// <summary>Estetik cerceve ve sembol ciftleri.</summary>
    public static readonly Dictionary<string, (string Left, string Right)> FrameSymbols = new(StringComparer.OrdinalIgnoreCase)
    {
        { "crown",      ("👑 ", " 👑") },
        { "tac",        ("👑 ", " 👑") },
        { "flame",      ("🔥 ", " 🔥") },
        { "alev",       ("🔥 ", " 🔥") },
        { "star",       ("★ ", " ★") },
        { "yildiz",     ("★ ", " ★") },
        { "wings",      ("꧁ ", " ꧂") },
        { "kanat",      ("꧁ ", " ꧂") },
        { "diamond",    ("◆ ", " ◆") },
        { "elmas",      ("◆ ", " ◆") },
        { "lightning",  ("⚡ ", " ⚡") },
        { "simsek",     ("⚡ ", " ⚡") },
        { "sparkle",    ("✨ ", " ✨") },
        { "parilti",    ("✨ ", " ✨") },
        { "sword",      ("⚔️ ", " ⚔️") },
        { "kilic",      ("⚔️ ", " ⚔️") },
        { "shield",     ("🛡️ ", " 🛡️") },
        { "kalkan",     ("🛡️ ", " 🛡️") },
        { "heart",      ("❤ ", " ❤") },
        { "kalp",       ("❤ ", " ❤") },
        { "cross",      ("† ", " †") },
        { "hac",        ("† ", " †") },
        { "skull",      ("💀 ", " 💀") },
        { "kurukafa",   ("💀 ", " 💀") },
    };

    public static readonly (string Key, string Name, string Symbol)[] AvailableFrames =
    {
        ("none", "Yok (Standart)", "-"),
        ("crown", "Taç (👑)", "👑"),
        ("flame", "Alev (🔥)", "🔥"),
        ("star", "Yıldız (★)", "★"),
        ("wings", "Kanatlar (꧁ ꧂)", "꧁"),
        ("diamond", "Elmas (◆)", "◆"),
        ("lightning", "Şimşek (⚡)", "⚡"),
        ("sparkle", "Işıltı (✨)", "✨"),
        ("sword", "Kılıç (⚔️)", "⚔️"),
        ("shield", "Kalkan (🛡️)", "🛡️"),
        ("heart", "Kalp (❤)", "❤"),
        ("cross", "Haç (†)", "†"),
        ("skull", "Kuru Kafa (💀)", "💀"),
    };

    public static bool IsFrame(string? name) =>
        !string.IsNullOrWhiteSpace(name) && FrameSymbols.ContainsKey(name);

    public static string ApplyFrame(string text, string frameName)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;
        if (FrameSymbols.TryGetValue(frameName, out var f))
            return $"{f.Left}{text}{f.Right}";
        return text;
    }

    /// <summary>Oyuncu tabanli placeholder degerlerini olusturur (lazy degil; chat basina bir kez cagrilir).</summary>
    public static Dictionary<string, string> BuildPlayerValues(IPlayer? player, ResolvedTag? tag)
    {
        ulong sid = PlayerHelper.GetSteamId64(player);
        var values = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["NAME"] = ColorHelper.StripControlChars(PlayerHelper.GetName(player)),
            ["STEAMID64"] = sid == 0 ? "-" : sid.ToString(),
            ["STEAMID2"] = sid == 0 ? "-" : SteamIdHelper.ToSteam2(sid),
            ["STEAMID3"] = sid == 0 ? "-" : SteamIdHelper.ToSteam3(sid),
            ["TEAM"] = PlayerHelper.GetTeam(player),
            ["PING"] = PlayerHelper.GetPing(player).ToString(),
            ["SCORE"] = PlayerHelper.GetScore(player).ToString(),
            ["KILLS"] = PlayerHelper.GetKills(player).ToString(),
            ["DEATHS"] = PlayerHelper.GetDeaths(player).ToString(),
            ["MVPS"] = PlayerHelper.GetMvps(player).ToString(),
            ["TAG_ID"] = tag?.SourceId ?? string.Empty,
        };

        if (PlayerHelper.IsUsable(player))
        {
            values["SLOT"] = player!.Slot.ToString();
            values["PLAYERID"] = player.PlayerID.ToString();
            values["USERID"] = player.UserID.ToString();
        }
        else
        {
            values["SLOT"] = values["PLAYERID"] = values["USERID"] = "-1";
        }

        return values;
    }

    public static string Apply(string? input, IReadOnlyDictionary<string, string> values)
    {
        if (string.IsNullOrEmpty(input)) return string.Empty;
        var replaced = TokenRegex.Replace(input, m => values.TryGetValue(m.Groups["k"].Value, out var v) ? v : m.Value);

        // Nested efekt ve cerceve destegi ({GALAXY:{CROWN:[KRAL]}} vb. icin 3 tura kadar)
        int iterations = 0;
        while (replaced.Contains(':') && iterations < 3)
        {
            var prev = replaced;
            replaced = EffectRegex.Replace(replaced, m =>
            {
                var effect = m.Groups["effect"].Value;
                var text = m.Groups["text"].Value;
                if (IsFrame(effect))
                    return ApplyFrame(text, effect);
                if (ColorHelper.IsGradient(effect))
                    return ColorHelper.ApplyGradient(text, effect);
                return m.Value;
            });
            if (prev == replaced) break;
            iterations++;
        }
        return replaced;
    }
}

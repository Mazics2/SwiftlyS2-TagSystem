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
        return TokenRegex.Replace(input, m => values.TryGetValue(m.Groups["k"].Value, out var v) ? v : m.Value);
    }
}

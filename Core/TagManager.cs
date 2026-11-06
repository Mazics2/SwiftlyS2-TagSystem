using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Players;
using TagSystem.Config;
using TagSystem.Models;
using TagSystem.Utils;

namespace TagSystem.Core;

/// <summary>
/// Bir oyuncu icin config'te ayarlanan hangi etiketin gecerli oldugunu belirler:
///  1) Uyan tum [[players]] + [[groups]] tanimlari toplanir (enabled, takim filtresi, yetki/uyelik).
///  2) En yuksek 'priority' degerine sahip tanim secilir (esitlikte [[players]] onceliklidir).
///  3) Hicbiri uymazsa [default] uygulanir.
/// Sonuclar slot bazinda kisa sureli cache'lenir (yetki kontrolu her chat mesajinda tekrarlanmasin).
/// </summary>
public class TagManager
{
    private const long CacheTtlMs = 60_000;

    private readonly ISwiftlyCore _core;
    private PluginConfig _config;

    private readonly Dictionary<int, CacheEntry> _cache = new();

    private sealed record CacheEntry(ulong SteamId, string Team, ResolvedTag Tag, long ExpiresAt);

    public TagManager(ISwiftlyCore core, PluginConfig config)
    {
        _core = core;
        _config = config;
    }

    public void UpdateConfig(PluginConfig config)
    {
        _config = config;
        InvalidateAll();
    }

    public void InvalidateAll() => _cache.Clear();
    public void Invalidate(int slot) => _cache.Remove(slot);

    public ResolvedTag BuildResolvedTag(TagEntry t) => Build(t);

    public ResolvedTag Resolve(IPlayer? player)
    {
        if (!PlayerHelper.IsUsable(player)) return Build(_config.Default);

        var slot = player!.Slot;
        var steamId = PlayerHelper.GetSteamId64(player);
        var team = PlayerHelper.GetTeam(player);
        var now = Environment.TickCount64;

        if (_cache.TryGetValue(slot, out var c) && c.SteamId == steamId && c.Team == team && c.ExpiresAt > now)
            return c.Tag;

        var matches = CollectMatches(steamId, team);
        var tag = Build(matches[0]);
        _cache[slot] = new CacheEntry(steamId, team, tag, now + CacheTtlMs);
        return tag;
    }

    /// <summary>Oyuncunun su an eslestigi tum etiket tanimlari (oncelik sirasina gore, default en sonda).</summary>
    public List<TagEntry> GetMatchingEntries(IPlayer player)
    {
        var steamId = PlayerHelper.GetSteamId64(player);
        var team = PlayerHelper.GetTeam(player);
        return CollectMatches(steamId, team);
    }

    private List<TagEntry> CollectMatches(ulong steamId, string team)
    {
        var list = new List<TagEntry>();

        if (steamId != 0)
        {
            foreach (var p in _config.Players)
                if (p.Enabled && p.SteamId64 == steamId && p.MatchesTeam(team))
                    list.Add(p);
        }

        foreach (var g in _config.Groups)
            if (g.Enabled && g.MatchesTeam(team) && IsInGroup(steamId, g))
                list.Add(g);

        // Stabil siralama: ayni oncelikte config'teki sira korunur (players once).
        list = list.OrderByDescending(x => x.Priority).ToList();

        if (_config.Default.Enabled)
            list.Add(_config.Default);
        else
            list.Add(new TagEntry { Id = "none", ChatTag = "", ScoreboardTag = "", Priority = int.MinValue, Enabled = false });

        return list;
    }

    private bool IsInGroup(ulong steamId, GroupData group)
    {
        if (steamId == 0) return false;
        if (group.Members.Contains(steamId)) return true;
        if (!_config.Settings.UsePermissions) return false;

        foreach (var perm in group.Permissions)
        {
            try
            {
                if (_core.Permission.PlayerHasPermission(steamId, perm)) return true;
            }
            catch
            {
                // Permission servisi hata verirse grubu atla
            }
        }
        return false;
    }

    private ResolvedTag Build(TagEntry t)
    {
        var d = _config.Default;
        var s = _config.Settings;
        return new ResolvedTag
        {
            SourceId = t.Id,
            Label = t.Label,
            ChatTag = t.Enabled ? (t.ChatTag ?? d.ChatTag ?? string.Empty) : string.Empty,
            ScoreboardTag = t.Enabled ? (t.ScoreboardTag ?? d.ScoreboardTag ?? string.Empty) : string.Empty,
            Frame = t.Frame ?? d.Frame,
            ChatTagColor = t.ChatTagColor ?? d.ChatTagColor ?? string.Empty,
            NameColor = t.NameColor ?? d.NameColor ?? "teamcolor",
            MessageColor = t.MessageColor ?? d.MessageColor ?? "default",
            UseTagColor = t.UseTagColor ?? d.UseTagColor ?? true,
            ChatFormat = t.ChatFormat ?? d.ChatFormat ?? s.ChatFormat,
            ScoreboardFormat = t.ScoreboardFormat ?? d.ScoreboardFormat ?? s.ScoreboardFormat,
            Priority = t.Priority,
            Enabled = t.Enabled,
        };
    }
}

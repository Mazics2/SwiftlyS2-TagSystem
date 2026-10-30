using System.Text;
using Microsoft.Extensions.Logging;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Misc;
using SwiftlyS2.Shared.Players;
using TagSystem.Config;
using TagSystem.Models;
using TagSystem.Utils;

namespace TagSystem.Core;

/// <summary>Cozulmus etiketleri oyuna uygular: scoreboard (clan tag) ve chat formatlama.</summary>
public class TagService
{
    private readonly ISwiftlyCore _core;
    private readonly TagManager _manager;
    private PluginConfig _config;

    /// <summary>Oyuncunun eklenti dokunmadan onceki (Steam grubu) clan tag'i. Unload'da geri yuklenir.</summary>
    private readonly Dictionary<int, string> _originalClan = new();

    public TagService(ISwiftlyCore core, PluginConfig config, TagManager manager)
    {
        _core = core;
        _config = config;
        _manager = manager;
    }

    public void UpdateConfig(PluginConfig config) => _config = config;

    // ============================== SCOREBOARD ==============================

    public void ApplyScoreboard(IPlayer? player)
    {
        if (!_config.Settings.EnableScoreboardTag) return;
        if (!ShouldHandle(player)) return;

        var controller = PlayerHelper.GetController(player);
        if (controller == null) return;

        try
        {
            var slot = player!.Slot;
            var current = controller.Clan ?? string.Empty;
            if (!_originalClan.ContainsKey(slot)) _originalClan[slot] = current;

            var tag = _manager.Resolve(player);
            var target = BuildScoreboardText(player, tag);

            // Etiket bossa oyuncunun kendi Steam grup clan tag'ini koru.
            if (string.IsNullOrEmpty(target)) target = _originalClan[slot];

            if (!string.Equals(current, target, StringComparison.Ordinal))
            {
                controller.Clan = target;
                controller.ClanUpdated();
            }
        }
        catch (Exception ex)
        {
            if (_config.Settings.Debug) _core.Logger.LogWarning(ex, "[TagSystem] Scoreboard uygulanamadı.");
        }
    }

    public void ApplyScoreboardAll()
    {
        foreach (var p in _core.PlayerManager.GetAllPlayers())
            ApplyScoreboard(p);
    }

    public string BuildScoreboardText(IPlayer? player, ResolvedTag tag)
    {
        if (!tag.Enabled) return string.Empty;
        var values = PlaceholderHelper.BuildPlayerValues(player, tag);
        var tagText = ColorHelper.StripAll(PlaceholderHelper.Apply(tag.ScoreboardTag, values));
        values["SCOREBOARD_TAG"] = tagText;
        var formatted = ColorHelper.StripAll(PlaceholderHelper.Apply(tag.ScoreboardFormat, values)).Trim();
        return TruncateUtf8(formatted, _config.Settings.ScoreboardMaxLength, 31);
    }

    /// <summary>Eklentinin degistirdigi clan tag'i eski haline getirir (unload / ozellik kapatilinca).</summary>
    public void RestoreScoreboard(IPlayer? player)
    {
        if (!PlayerHelper.IsUsable(player)) return;
        if (!_originalClan.TryGetValue(player!.Slot, out var original)) return;
        var controller = PlayerHelper.GetController(player);
        if (controller == null) return;
        try
        {
            controller.Clan = original;
            controller.ClanUpdated();
        }
        catch { /* yoksay */ }
    }

    public void RestoreScoreboardAll()
    {
        foreach (var p in _core.PlayerManager.GetAllPlayers())
            RestoreScoreboard(p);
    }

    public void OnPlayerDisconnected(int slot)
    {
        _originalClan.Remove(slot);
        _manager.Invalidate(slot);
    }

    public void OnMapChanged()
    {
        _originalClan.Clear();
        _manager.InvalidateAll();
    }

    // ================================= CHAT =================================

    public HookResult HandleChat(int playerId, string text, bool teamOnly)
    {
        if (!_config.Settings.EnableChatTag) return HookResult.Continue;

        var sender = _core.PlayerManager.GetPlayer(playerId);
        if (!ShouldHandle(sender)) return HookResult.Continue;

        var message = (text ?? string.Empty).Trim();
        if (message.Length >= 2 && message[0] == '"' && message[^1] == '"') message = message[1..^1].Trim();

        if (message.Length == 0)
            return _config.Chat.BlockEmptyMessages ? HookResult.Stop : HookResult.Continue;

        // Komutlar (!tagreload, /ban ...) SwiftlyS2'nin komut sistemine dokunulmadan birakilir.
        foreach (var prefix in _config.Chat.IgnorePrefixes)
            if (!string.IsNullOrEmpty(prefix) && message.StartsWith(prefix, StringComparison.Ordinal))
                return HookResult.Continue;

        try
        {
            var tag = _manager.Resolve(sender);
            if (!tag.Enabled) return HookResult.Continue;

            var final = BuildChatLine(sender!, tag, message, teamOnly, out var senderTeam, out var senderAlive);
            if (string.IsNullOrWhiteSpace(final)) return HookResult.Continue;

            foreach (var r in _core.PlayerManager.GetAllPlayers())
            {
                if (!PlayerHelper.IsUsable(r) || r.IsFakeClient) continue;
                if (teamOnly && PlayerHelper.GetTeam(r) != senderTeam) continue;
                if (_config.Chat.DeadTalkOnlyToDead && !senderAlive && PlayerHelper.IsAlive(r)) continue;
                r.SendChat(final);
            }

            if (_config.Chat.LogToConsole)
            {
                var sid = PlayerHelper.GetSteamId64(sender);
                _core.Logger.LogInformation("[Chat]{Team} {Name} ({SteamId}): {Message}",
                    teamOnly ? $"[{senderTeam}]" : string.Empty,
                    PlayerHelper.GetName(sender), sid == 0 ? "-" : SteamIdHelper.ToSteam2(sid), message);
            }

            // Orijinal mesaji iptal et, ama diger eklentilerin chat hook'lari calismaya devam etsin.
            return HookResult.CancelOriginal;
        }
        catch (Exception ex)
        {
            // Herhangi bir hata durumunda mesaj kaybolmasin: orijinali gecir.
            _core.Logger.LogError(ex, "[TagSystem] Chat formatlanırken hata oluştu.");
            return HookResult.Continue;
        }
    }

    /// <summary>Gonderilecek, renk kodlari islenmis tam chat satirini olusturur.</summary>
    public string BuildChatLine(IPlayer sender, ResolvedTag tag, string rawMessage, bool teamOnly, out string senderTeam, out bool senderAlive)
    {
        senderTeam = PlayerHelper.GetTeam(sender);
        senderAlive = PlayerHelper.IsAlive(sender);

        var values = PlaceholderHelper.BuildPlayerValues(sender, tag);

        // Etiket metni (placeholder + renk)
        var chatTag = ColorHelper.NormalizeTags(PlaceholderHelper.Apply(tag.ChatTag, values), senderTeam).Trim();
        if (chatTag.Length > 0 && tag.UseTagColor)
        {
            var colorTag = ColorHelper.ToChatTag(tag.ChatTagColor, senderTeam);
            if (colorTag.Length > 0) chatTag = $"{colorTag}{chatTag}[default]";
        }

        values["CHAT_TAG"] = chatTag;
        values["NAME_COLOR"] = ColorHelper.ToChatTag(tag.NameColor, senderTeam);
        values["MESSAGE_COLOR"] = ColorHelper.ToChatTag(tag.MessageColor, senderTeam);
        values["MESSAGE"] = ColorHelper.Sanitize(rawMessage);
        values["DEAD"] = !senderAlive && senderTeam is "T" or "CT" ? _config.Chat.DeadPrefix : string.Empty;
        values["TEAMCHAT"] = !teamOnly ? string.Empty : senderTeam switch
        {
            "T" => _config.Chat.TeamPrefixT,
            "CT" => _config.Chat.TeamPrefixCt,
            _ => _config.Chat.TeamPrefixSpec
        };

        var format = tag.ChatFormat;
        if (chatTag.Length == 0) format = format.Replace("{CHAT_TAG} ", string.Empty).Replace("{CHAT_TAG}", string.Empty);

        // Once format'taki (config kaynakli, guvenilir) renk etiketlerini normalize et,
        // sonra placeholder'lari tek geciste doldur. Oyuncu kaynakli degerler zaten sanitize edildi.
        var line = PlaceholderHelper.Apply(ColorHelper.NormalizeTags(format, senderTeam), values).TrimStart();
        return Helper.Colored(ColorHelper.NormalizeTags(line, senderTeam));
    }

    // ================================ ORTAK =================================

    /// <summary>Eklenti adina oyuncuya on ekli, renkli mesaj gonderir.</summary>
    public void Reply(IPlayer? player, string text)
    {
        var line = Helper.Colored(ColorHelper.NormalizeTags($"{_config.Settings.Prefix} {text}"));
        if (PlayerHelper.IsUsable(player)) player!.SendChat(line);
    }

    private bool ShouldHandle(IPlayer? player)
    {
        if (!PlayerHelper.IsUsable(player)) return false;
        if (player!.IsFakeClient && !_config.Settings.ApplyToBots) return false;
        return true;
    }

    private static string TruncateUtf8(string text, int maxChars, int maxBytes)
    {
        if (text.Length > maxChars) text = text[..maxChars];
        while (text.Length > 0 && Encoding.UTF8.GetByteCount(text) > maxBytes) text = text[..^1];
        if (text.Length > 0 && char.IsHighSurrogate(text[^1])) text = text[..^1];
        return text;
    }
}

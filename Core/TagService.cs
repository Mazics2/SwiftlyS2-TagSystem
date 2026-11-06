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

    /// <summary>Oyuncuya eklenti tarafindan en son basariyla uygulanan clan tag. Gereksiz ag selini ve CS2 clan desync dongusunu onler.</summary>
    private readonly Dictionary<int, string> _appliedClan = new();

    /// <summary>Chat flood / spam korumasi: oyuncunun en son sohbet mesaji gonderdigi zaman (Environment.TickCount64).</summary>
    private readonly Dictionary<int, long> _lastChatTime = new();
    private const long ChatCooldownMs = 500;

    public TagService(ISwiftlyCore core, PluginConfig config, TagManager manager)
    {
        _core = core;
        _config = config;
        _manager = manager;
    }

    public void UpdateConfig(PluginConfig config) => _config = config;

    // ============================== SCOREBOARD ==============================

    public void ApplyScoreboard(IPlayer? player, bool force = false)
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

            // Eger oyuncuya zaten bu etiket uygulandiysa ve zorlama (force) yoksa,
            // ag selini (network flood), sunucu lagini ve client-server clan tag ping-pong dongusunu onlemek icin cik!
            if (!force && _appliedClan.TryGetValue(slot, out var applied) && string.Equals(applied, target, StringComparison.Ordinal))
                return;

            _appliedClan[slot] = target;

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

    public void ApplyScoreboardAll(bool force = false)
    {
        foreach (var p in _core.PlayerManager.GetAllPlayers())
            ApplyScoreboard(p, force);
    }

    public string BuildScoreboardText(IPlayer? player, ResolvedTag tag)
    {
        if (!tag.Enabled) return string.Empty;
        var values = PlaceholderHelper.BuildPlayerValues(player, tag);
        var tagText = ColorHelper.StripAll(PlaceholderHelper.Apply(tag.ScoreboardTag, values));

        // Skorboard clan tag'inde otomatik parantez olusmasini onle ([VIP] -> VIP, (VIP) -> VIP)
        tagText = StripOuterBrackets(tagText);
        values["SCOREBOARD_TAG"] = tagText;

        var formatted = ColorHelper.StripAll(PlaceholderHelper.Apply(tag.ScoreboardFormat, values)).Trim();
        formatted = StripOuterBrackets(formatted);

        // CS2 Panorama Scoreboard yerelleştirme (localization) düzeltmesi:
        // Eğer clan tag metni '#' ile başlıyorsa (örn: "#{PLAYERID}" -> "#1"),
        // CS2 Panorama motoru bunu yerelleştirme anahtarı sanarak baştaki '#' işaretini siler!
        // Başına görünmez sıfır genişlikli boşluk (\u200B) ekleyerek '#' işaretinin
        // skorboardda tam olarak görünmesini sağlıyoruz.
        if (formatted.StartsWith('#'))
            formatted = "\u200B" + formatted;

        return TruncateUtf8(formatted, _config.Settings.ScoreboardMaxLength, 31);
    }

    public static string StripOuterBrackets(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return string.Empty;
        var t = s.Trim();
        while ((t.StartsWith('[') && t.EndsWith(']')) || (t.StartsWith('(') && t.EndsWith(')')))
        {
            if (t.Length <= 2) return string.Empty;
            t = t[1..^1].Trim();
        }
        return t;
    }

    /// <summary>Eklentinin degistirdigi clan tag'i eski haline getirir (unload / ozellik kapatilinca).</summary>
    public void RestoreScoreboard(IPlayer? player)
    {
        if (!PlayerHelper.IsUsable(player)) return;
        var slot = player!.Slot;
        _appliedClan.Remove(slot);
        if (!_originalClan.TryGetValue(slot, out var original)) return;
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
        _appliedClan.Remove(slot);
        _lastChatTime.Remove(slot);
        _manager.Invalidate(slot);
    }

    public void OnMapChanged()
    {
        _originalClan.Clear();
        _appliedClan.Clear();
        _lastChatTime.Clear();
        _manager.InvalidateAll();
    }

    // ================================= CHAT =================================

    public HookResult HandleChat(int playerId, string text, bool teamOnly)
    {
        if (!_config.Settings.EnableChatTag) return HookResult.Continue;

        var sender = _core.PlayerManager.GetPlayer(playerId);
        if (!ShouldHandle(sender)) return HookResult.Continue;

        var message = ColorHelper.StripControlChars(text ?? string.Empty).Trim();
        if (message.Length >= 2 && message[0] == '"' && message[^1] == '"')
        {
            var inner = message[1..^1].Trim();
            if (inner.Length > 0) message = inner;
        }

        if (message.Length == 0)
            return _config.Chat.BlockEmptyMessages ? HookResult.CancelOriginal : HookResult.Continue;

        // Sessiz komutlar (/ ile baslayanlar) sohbette asla gorunmemeli; sessizce motora gecmelidir.
        if (message.StartsWith('/'))
            return HookResult.CancelOriginal;

        // Sadece config'te ozellikle gizlenmesi (sessiz kalmasi) istenen on ekler varsa sohbette gosterme
        foreach (var prefix in _config.Chat.IgnorePrefixes)
            if (!string.IsNullOrEmpty(prefix) && message.StartsWith(prefix, StringComparison.Ordinal))
                return HookResult.CancelOriginal;

        // Chat flood / spam korumasi: reliable buffer overflow ve istemci kicklenmelerini engeller
        var now = Environment.TickCount64;
        if (_lastChatTime.TryGetValue(playerId, out var lastTime) && (now - lastTime) < ChatCooldownMs)
        {
            return HookResult.CancelOriginal;
        }
        _lastChatTime[playerId] = now;

        try
        {
            var tag = _manager.Resolve(sender);
            if (!tag.Enabled) return HookResult.Continue;

            // Komut yazarken de (!ws, !tagmenu vb.) etiket gorunmeye devam eder.
            var final = BuildChatLine(sender!, tag, message, teamOnly, out var senderTeam, out var senderAlive);
            if (string.IsNullOrWhiteSpace(final)) return HookResult.Continue;

            foreach (var r in _core.PlayerManager.GetAllPlayers())
            {
                if (!PlayerHelper.IsUsable(r) || r.IsFakeClient) continue;
                if (teamOnly && PlayerHelper.GetTeam(r) != senderTeam) continue;
                if (_config.Chat.DeadTalkOnlyToDead && !senderAlive && PlayerHelper.IsAlive(r)) continue;
                r.SendChat(final);
            }

            // Orijinal etiketsiz CS2 mesajini iptal et; SwiftlyS2 komut sistemi ve diger eklentilerin
            // chat hook'lari CancelOriginal sayesinde engellenmeden calismaya devam eder.
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
        var safeName = values["NAME"];
        var safeMessage = ColorHelper.StripControlChars(rawMessage);

        // Etiket metni (placeholder + cerceve/sembol + renk)
        var rawTagText = ColorHelper.StripControlChars(PlaceholderHelper.Apply(tag.ChatTag, values)).Trim();
        if (rawTagText.Length > 0 && !string.IsNullOrWhiteSpace(tag.Frame) && PlaceholderHelper.IsFrame(tag.Frame))
        {
            rawTagText = PlaceholderHelper.ApplyFrame(rawTagText, tag.Frame);
        }
        bool protectTagLiteral = ColorHelper.IsSingleBracketTag(rawTagText);
        var chatTagBody = protectTagLiteral ? "{RAW_CHAT_TAG}" : ColorHelper.NormalizeTags(rawTagText, senderTeam);

        var chatTag = chatTagBody;
        if (rawTagText.Length > 0 && tag.UseTagColor)
        {
            if (ColorHelper.IsGradient(tag.ChatTagColor))
            {
                chatTag = ColorHelper.ApplyGradient(rawTagText, tag.ChatTagColor!);
            }
            else
            {
                var colorTag = ColorHelper.ToChatTag(tag.ChatTagColor, senderTeam);
                if (colorTag.Length > 0) chatTag = $"{colorTag}{chatTagBody}[default]";
            }
        }

        // 1. Adim: Renk kodlarini iceren sablon placeholder'larini yerlestir;
        // oyuncu ismi, mesaji ve tekli etiket metnini Helper.Colored sonrasina birak.
        values["CHAT_TAG"] = chatTag;
        values["NAME_COLOR"] = ColorHelper.IsGradient(tag.NameColor) ? string.Empty : ColorHelper.ToChatTag(tag.NameColor, senderTeam);
        values["MESSAGE_COLOR"] = ColorHelper.IsGradient(tag.MessageColor) ? string.Empty : ColorHelper.ToChatTag(tag.MessageColor, senderTeam);
        values["NAME"] = "{NAME}";
        values["MESSAGE"] = "{MESSAGE}";
        values["DEAD"] = !senderAlive && senderTeam is "T" or "CT" ? _config.Chat.DeadPrefix : string.Empty;
        values["TEAMCHAT"] = !teamOnly ? string.Empty : senderTeam switch
        {
            "T" => _config.Chat.TeamPrefixT,
            "CT" => _config.Chat.TeamPrefixCt,
            _ => _config.Chat.TeamPrefixSpec
        };

        var format = tag.ChatFormat;
        if (rawTagText.Length == 0) format = format.Replace("{CHAT_TAG} ", string.Empty).Replace("{CHAT_TAG}", string.Empty);

        var templateLine = PlaceholderHelper.Apply(ColorHelper.NormalizeTags(format, senderTeam), values).TrimStart();
        var coloredLine = Helper.Colored(ColorHelper.NormalizeTags(templateLine, senderTeam));
        if (coloredLine.Length > 0 && coloredLine[0] != ' ' && coloredLine.Any(ch => ch < 0x20))
            coloredLine = " " + coloredLine;

        // 2. Adim: Oyuncu kaynakli metinleri tek geciste yerlestir.
        // Gradyan efekt secilmisse, Helper.Colored ile ikili CS2 renk baytlarina cevrilmis sekilde yerlestir.
        var formattedName = ColorHelper.IsGradient(tag.NameColor)
            ? Helper.Colored(ColorHelper.ApplyGradient(safeName, tag.NameColor!))
            : safeName;

        var formattedMessage = ColorHelper.IsGradient(tag.MessageColor)
            ? Helper.Colored(ColorHelper.ApplyGradient(safeMessage, tag.MessageColor!))
            : safeMessage;

        var literalValues = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["RAW_CHAT_TAG"] = rawTagText,
            ["NAME"] = formattedName,
            ["MESSAGE"] = formattedMessage
        };
        return PlaceholderHelper.Apply(coloredLine, literalValues);
    }

    /// <summary>
    /// Verilen oyuncu veya etiket tanımı için örnek chat satırı önizlemesi üretir.
    /// stripped=true ise renk ve kontrol kodları temizlenmiş düz metin döner (Panorama HUD uyumlu).
    /// </summary>
    public string BuildPreviewChatLine(IPlayer? player, TagEntry? entry = null, string sample = "Merhaba Dünya!", bool stripped = false)
    {
        var resolved = entry != null ? _manager.BuildResolvedTag(entry) : _manager.Resolve(player);
        if (PlayerHelper.IsUsable(player))
        {
            var line = BuildChatLine(player!, resolved, sample, false, out _, out _);
            return stripped ? ColorHelper.StripAll(line).Trim() : line;
        }

        var tagText = resolved.ChatTag ?? string.Empty;
        if (tagText.Length > 0 && !string.IsNullOrWhiteSpace(resolved.Frame) && PlaceholderHelper.IsFrame(resolved.Frame))
        {
            tagText = PlaceholderHelper.ApplyFrame(tagText, resolved.Frame);
        }
        var name = "Oyuncu";
        if (stripped)
        {
            return string.IsNullOrWhiteSpace(tagText)
                ? $"{name}: {sample}"
                : $"{tagText} {name}: {sample}";
        }

        var tagColor = ColorHelper.ToChatTag(resolved.ChatTagColor, "CT");
        var nameColor = ColorHelper.ToChatTag(resolved.NameColor, "CT");
        var msgColor = ColorHelper.ToChatTag(resolved.MessageColor, "CT");

        var formattedTag = ColorHelper.IsGradient(resolved.ChatTagColor)
            ? ColorHelper.ApplyGradient(tagText, resolved.ChatTagColor!)
            : $"{tagColor}{tagText}[default]";

        var formattedName = ColorHelper.IsGradient(resolved.NameColor)
            ? ColorHelper.ApplyGradient(name, resolved.NameColor!)
            : $"{nameColor}{name}[default]";

        var formattedMsg = ColorHelper.IsGradient(resolved.MessageColor)
            ? ColorHelper.ApplyGradient(sample, resolved.MessageColor!)
            : $"{msgColor}{sample}[default]";

        return Helper.Colored($"{formattedTag} {formattedName}: {formattedMsg}");
    }

    /// <summary>Verilen oyuncu veya etiket tanımı için Scoreboard (Clan Tag) önizlemesi üretir.</summary>
    public string BuildPreviewScoreboard(IPlayer? player, TagEntry? entry = null)
    {
        var resolved = entry != null ? _manager.BuildResolvedTag(entry) : _manager.Resolve(player);
        return BuildScoreboardText(player, resolved);
    }

    /// <summary>Oyuncuya kendi etiketinin ya da verilen etiketin gerçek renkli chat önizleme mesajını basar.</summary>
    public void SendTestChat(IPlayer player, TagEntry? entry = null, string sample = "Test mesajı: TagSystem canlı önizleme!")
    {
        if (!PlayerHelper.IsUsable(player)) return;
        var resolved = entry != null ? _manager.BuildResolvedTag(entry) : _manager.Resolve(player);
        var line = BuildChatLine(player, resolved, sample, false, out _, out _);
        player.SendChat(line);
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

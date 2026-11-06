using System.Globalization;
using System.Text;
using Tomlyn;
using Tomlyn.Model;
using TagSystem.Models;
using TagSystem.Utils;

namespace TagSystem.Config;

/// <summary>
/// tagconfig.toml dosyasini okur / kaydeder / yoksa yorumlu varsayilan dosyayi olusturur.
/// Hatali dosyada exception firlatir (cagiran taraf eski config'i korur).
/// </summary>
public class ConfigLoader
{
    public string ConfigPath { get; }

    /// <summary>Son yuklemede olusan uyarilar (gecersiz SteamID, bilinmeyen renk vb.).</summary>
    public List<string> Warnings { get; } = new();

    public ConfigLoader(string configDirectory)
    {
        Directory.CreateDirectory(configDirectory);
        ConfigPath = Path.Combine(configDirectory, "tagconfig.toml");
    }

    public PluginConfig Load()
    {
        Warnings.Clear();

        if (!File.Exists(ConfigPath))
        {
            File.WriteAllText(ConfigPath, DefaultConfigTemplate.Content, new UTF8Encoding(false));
        }

        var content = File.ReadAllText(ConfigPath, Encoding.UTF8);
        return Parse(content);
    }

    /// <summary>Bellekteki guncel PluginConfig nesnesini okunabilir ve yorumlu TOML olarak diske yazar.</summary>
    public void Save(PluginConfig config)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# ==============================================================================");
        sb.AppendLine("#  TagSystem - Scoreboard (clan tag) & Chat etiket sistemi  (SwiftlyS2 / CS2)");
        sb.AppendLine("#  Oyun içi admin menüsü: !tagmenu");
        sb.AppendLine("#  Hızlı komutlar: !tagver, !ozeltag, !tagsil, !grupekle, !tagreload, !tagcheck");
        sb.AppendLine("# ==============================================================================");
        sb.AppendLine();

        var st = config.Settings;
        sb.AppendLine("[settings]");
        sb.AppendLine($"enable_scoreboard_tag = {B(st.EnableScoreboardTag)}");
        sb.AppendLine($"enable_chat_tag = {B(st.EnableChatTag)}");
        sb.AppendLine($"use_permissions = {B(st.UsePermissions)}");
        sb.AppendLine($"cache_on_connect = {B(st.CacheOnConnect)}");
        sb.AppendLine($"apply_to_bots = {B(st.ApplyToBots)}");
        sb.AppendLine($"scoreboard_refresh_seconds = {st.ScoreboardRefreshSeconds.ToString("0.0", CultureInfo.InvariantCulture)}");
        sb.AppendLine($"scoreboard_max_length = {st.ScoreboardMaxLength}");
        sb.AppendLine($"freeze_while_menu_open = {B(st.FreezeWhileMenuOpen)}");
        sb.AppendLine($"chat_format = {Q(st.ChatFormat)}");
        sb.AppendLine($"scoreboard_format = {Q(st.ScoreboardFormat)}");
        sb.AppendLine($"prefix = {Q(st.Prefix)}");
        sb.AppendLine($"debug = {B(st.Debug)}");
        sb.AppendLine();

        var ch = config.Chat;
        sb.AppendLine("[chat]");
        sb.AppendLine($"dead_prefix = {Q(ch.DeadPrefix)}");
        sb.AppendLine($"team_prefix_t = {Q(ch.TeamPrefixT)}");
        sb.AppendLine($"team_prefix_ct = {Q(ch.TeamPrefixCt)}");
        sb.AppendLine($"team_prefix_spec = {Q(ch.TeamPrefixSpec)}");
        sb.AppendLine($"ignore_prefixes = [{string.Join(", ", ch.IgnorePrefixes.Select(Q))}]");
        sb.AppendLine($"log_to_console = {B(ch.LogToConsole)}");
        sb.AppendLine($"dead_talk_only_to_dead = {B(ch.DeadTalkOnlyToDead)}");
        sb.AppendLine($"block_empty_messages = {B(ch.BlockEmptyMessages)}");
        sb.AppendLine();

        var cm = config.Commands;
        sb.AppendLine("[commands]");
        sb.AppendLine($"menu_command = {Q(cm.MenuCommand)}");
        sb.AppendLine($"give_group_command = {Q(cm.GiveGroupCommand)}");
        sb.AppendLine($"set_custom_tag_command = {Q(cm.SetCustomTagCommand)}");
        sb.AppendLine($"remove_tag_command = {Q(cm.RemoveTagCommand)}");
        sb.AppendLine($"add_group_command = {Q(cm.AddGroupCommand)}");
        sb.AppendLine($"reload_command = {Q(cm.ReloadCommand)}");
        sb.AppendLine($"check_command = {Q(cm.CheckCommand)}");
        sb.AppendLine($"admin_permission = {Q(cm.AdminPermission)}");
        sb.AppendLine();

        var d = config.Default;
        sb.AppendLine("[default]");
        WriteEntryFields(sb, d, isDefault: true);
        sb.AppendLine();

        if (config.Groups.Count > 0)
        {
            sb.AppendLine("# ----------------------------- YETKİ GRUPLARI ---------------------------------");
            foreach (var g in config.Groups)
            {
                sb.AppendLine("[[groups]]");
                sb.AppendLine($"id = {Q(g.Id)}");
                if (g.Permissions.Count == 1)
                    sb.AppendLine($"permission_key = {Q(g.Permissions[0])}");
                else if (g.Permissions.Count > 1)
                    sb.AppendLine($"permissions = [{string.Join(", ", g.Permissions.Select(Q))}]");

                if (g.Members.Count > 0)
                    sb.AppendLine($"members = [{string.Join(", ", g.Members.Select(m => Q(SteamIdHelper.ToSteam2(m))))}]");

                WriteEntryFields(sb, g, isDefault: false);
                sb.AppendLine();
            }
        }

        if (config.Players.Count > 0)
        {
            sb.AppendLine("# ----------------------------- ÖZEL OYUNCULAR ---------------------------------");
            foreach (var p in config.Players)
            {
                sb.AppendLine("[[players]]");
                var sid = !string.IsNullOrWhiteSpace(p.RawSteamId) ? p.RawSteamId : SteamIdHelper.ToSteam2(p.SteamId64);
                sb.AppendLine($"steamid = {Q(sid)}");
                WriteEntryFields(sb, p, isDefault: false);
                sb.AppendLine();
            }
        }

        var tmp = ConfigPath + ".tmp";
        File.WriteAllText(tmp, sb.ToString(), new UTF8Encoding(false));
        File.Move(tmp, ConfigPath, overwrite: true);
    }

    private static void WriteEntryFields(StringBuilder sb, TagEntry e, bool isDefault)
    {
        if (!string.IsNullOrWhiteSpace(e.DisplayName)) sb.AppendLine($"display_name = {Q(e.DisplayName)}");
        if (e.ChatTag != null) sb.AppendLine($"chat_tag = {Q(e.ChatTag)}");
        if (!string.IsNullOrWhiteSpace(e.Frame)) sb.AppendLine($"frame = {Q(e.Frame)}");
        if (e.ScoreboardTag != null) sb.AppendLine($"scoreboard_tag = {Q(e.ScoreboardTag)}");
        if (!string.IsNullOrWhiteSpace(e.ChatTagColor)) sb.AppendLine($"chat_tag_color = {Q(e.ChatTagColor)}");
        if (!string.IsNullOrWhiteSpace(e.NameColor)) sb.AppendLine($"name_color = {Q(e.NameColor)}");
        if (!string.IsNullOrWhiteSpace(e.MessageColor)) sb.AppendLine($"message_color = {Q(e.MessageColor)}");
        if (e.UseTagColor.HasValue) sb.AppendLine($"use_tag_color = {B(e.UseTagColor.Value)}");
        if (!string.IsNullOrWhiteSpace(e.ChatFormat)) sb.AppendLine($"chat_format = {Q(e.ChatFormat)}");
        if (!string.IsNullOrWhiteSpace(e.ScoreboardFormat)) sb.AppendLine($"scoreboard_format = {Q(e.ScoreboardFormat)}");
        if (!isDefault && e.Teams.Count > 0) sb.AppendLine($"teams = [{string.Join(", ", e.Teams.Select(Q))}]");
        sb.AppendLine($"priority = {e.Priority}");
        sb.AppendLine($"enabled = {B(e.Enabled)}");
    }

    private static string B(bool v) => v ? "true" : "false";
    private static string Q(string? s) => $"\"{(s ?? string.Empty).Replace("\\", "\\\\").Replace("\"", "\\\"")}\"";

    private PluginConfig Parse(string toml)
    {
        var syntax = Toml.Parse(toml, ConfigPath);
        if (syntax.HasErrors)
        {
            var errors = string.Join("; ", syntax.Diagnostics.Select(d => d.ToString()));
            throw new InvalidDataException("TOML sözdizimi hatası: " + errors);
        }

        var model = syntax.ToModel();
        var config = new PluginConfig();

        if (Table(model, "settings") is { } s)
        {
            var st = config.Settings;
            st.EnableScoreboardTag = Bool(s, "enable_scoreboard_tag", st.EnableScoreboardTag);
            st.EnableChatTag = Bool(s, "enable_chat_tag", st.EnableChatTag);
            st.UsePermissions = Bool(s, "use_permissions", st.UsePermissions);
            st.CacheOnConnect = Bool(s, "cache_on_connect", st.CacheOnConnect);
            st.ApplyToBots = Bool(s, "apply_to_bots", st.ApplyToBots);
            st.ScoreboardRefreshSeconds = (float)Double(s, "scoreboard_refresh_seconds", st.ScoreboardRefreshSeconds);
            st.ScoreboardMaxLength = Math.Clamp(Int(s, "scoreboard_max_length", st.ScoreboardMaxLength), 1, 31);
            st.FreezeWhileMenuOpen = Bool(s, "freeze_while_menu_open", st.FreezeWhileMenuOpen);
            st.ChatFormat = Str(s, "chat_format") ?? st.ChatFormat;
            st.ScoreboardFormat = Str(s, "scoreboard_format") ?? st.ScoreboardFormat;
            st.Prefix = Str(s, "prefix") ?? st.Prefix;
            st.Debug = Bool(s, "debug", st.Debug);
        }

        if (Table(model, "chat") is { } c)
        {
            var ch = config.Chat;
            ch.DeadPrefix = Str(c, "dead_prefix") ?? ch.DeadPrefix;
            ch.TeamPrefixT = Str(c, "team_prefix_t") ?? ch.TeamPrefixT;
            ch.TeamPrefixCt = Str(c, "team_prefix_ct") ?? ch.TeamPrefixCt;
            ch.TeamPrefixSpec = Str(c, "team_prefix_spec") ?? ch.TeamPrefixSpec;
            var rawPrefixes = StrList(c, "ignore_prefixes");
            if (rawPrefixes != null)
            {
                // Eski varsayilan ["!", "/", "."] listesi komutlarda ve ozel karakterlerde etiketin gorunmemesine
                // yol actigi icin otomatik olarak bos listeye (etiketli gosterim) gecirilir.
                if (rawPrefixes.Count == 3 && rawPrefixes.Contains("!") && rawPrefixes.Contains("/") && rawPrefixes.Contains("."))
                    ch.IgnorePrefixes = new List<string>();
                else
                    ch.IgnorePrefixes = rawPrefixes;
            }
            ch.LogToConsole = Bool(c, "log_to_console", ch.LogToConsole);
            ch.DeadTalkOnlyToDead = Bool(c, "dead_talk_only_to_dead", ch.DeadTalkOnlyToDead);
            ch.BlockEmptyMessages = Bool(c, "block_empty_messages", ch.BlockEmptyMessages);
        }

        if (Table(model, "commands") is { } cm)
        {
            var co = config.Commands;
            co.MenuCommand = Str(cm, "menu_command") ?? co.MenuCommand;
            co.GiveGroupCommand = Str(cm, "give_group_command") ?? co.GiveGroupCommand;
            co.SetCustomTagCommand = Str(cm, "set_custom_tag_command") ?? co.SetCustomTagCommand;
            co.RemoveTagCommand = Str(cm, "remove_tag_command") ?? co.RemoveTagCommand;
            co.AddGroupCommand = Str(cm, "add_group_command") ?? co.AddGroupCommand;
            co.ReloadCommand = Str(cm, "reload_command") ?? co.ReloadCommand;
            co.CheckCommand = Str(cm, "check_command") ?? co.CheckCommand;
            co.AdminPermission = Str(cm, "admin_permission") ?? co.AdminPermission;
        }

        if (Table(model, "default") is { } d)
        {
            var def = PluginConfig.CreateDefaultTag();
            ReadEntry(d, def);
            def.Id = "default";
            def.Teams.Clear();
            config.Default = def;
        }

        if (model.TryGetValue("groups", out var groupsObj) && groupsObj is TomlTableArray groups)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int index = 0;
            foreach (var gt in groups)
            {
                index++;
                var g = new GroupData();
                ReadEntry(gt, g);
                g.Id = Str(gt, "id") ?? string.Empty;

                if (string.IsNullOrWhiteSpace(g.Id))
                {
                    Warnings.Add($"[[groups]] #{index}: 'id' boş, atlandı.");
                    continue;
                }
                if (!seen.Add(g.Id))
                {
                    Warnings.Add($"[[groups]] '{g.Id}': aynı id birden fazla kez tanımlı, sonraki atlandı.");
                    continue;
                }

                if (Str(gt, "permission_key") is { Length: > 0 } single) g.Permissions.Add(single);
                if (StrList(gt, "permissions") is { } many) g.Permissions.AddRange(many.Where(x => !string.IsNullOrWhiteSpace(x)));

                if (StrList(gt, "members") is { } members)
                {
                    foreach (var m in members)
                    {
                        var id = SteamIdHelper.Parse(m);
                        if (id == 0) Warnings.Add($"[[groups]] '{g.Id}': geçersiz üye SteamID '{m}'.");
                        else g.Members.Add(id);
                    }
                }

                if (g.Permissions.Count == 0 && g.Members.Count == 0)
                    Warnings.Add($"[[groups]] '{g.Id}': ne permission ne de members tanımlı; kimseye uygulanmayacak.");

                config.Groups.Add(g);
            }
        }

        if (model.TryGetValue("players", out var playersObj) && playersObj is TomlTableArray players)
        {
            int index = 0;
            foreach (var pt in players)
            {
                index++;
                var p = new PlayerData();
                ReadEntry(pt, p);

                var raw = Str(pt, "steamid") ?? Str(pt, "steamid64") ?? Str(pt, "steamid2") ?? Str(pt, "steamid3") ?? string.Empty;
                p.RawSteamId = raw;
                p.SteamId64 = SteamIdHelper.Parse(raw);
                if (p.SteamId64 == 0)
                {
                    Warnings.Add($"[[players]] #{index}: geçersiz/boş SteamID '{raw}', atlandı.");
                    continue;
                }
                p.Id = Str(pt, "id") ?? $"player:{p.SteamId64}";
                config.Players.Add(p);
            }
        }

        ValidateColors(config);
        return config;
    }

    private void ReadEntry(TomlTable t, TagEntry e)
    {
        e.DisplayName = Str(t, "display_name") ?? e.DisplayName;
        e.ChatTag = Str(t, "chat_tag") ?? e.ChatTag;
        e.Frame = Str(t, "frame") ?? e.Frame;
        var rawScoreboardTag = Str(t, "scoreboard_tag") ?? e.ScoreboardTag;
        e.ScoreboardTag = rawScoreboardTag != null ? TagSystem.Core.TagService.StripOuterBrackets(rawScoreboardTag) : null;
        e.ChatTagColor = Str(t, "chat_tag_color") ?? e.ChatTagColor;
        e.NameColor = Str(t, "name_color") ?? e.NameColor;
        e.MessageColor = Str(t, "message_color") ?? e.MessageColor;
        e.ScoreboardTagColor = Str(t, "scoreboard_tag_color") ?? e.ScoreboardTagColor;
        if (t.ContainsKey("use_tag_color")) e.UseTagColor = Bool(t, "use_tag_color", true);
        e.ChatFormat = Str(t, "chat_format") ?? e.ChatFormat;
        e.ScoreboardFormat = Str(t, "scoreboard_format") ?? e.ScoreboardFormat;
        e.Priority = Int(t, "priority", e.Priority);
        e.Enabled = Bool(t, "enabled", e.Enabled);
        if (StrList(t, "teams") is { } teams)
            e.Teams = teams.Select(NormalizeTeam).Where(x => x.Length > 0).ToList();
    }

    private void ValidateColors(PluginConfig config)
    {
        IEnumerable<TagEntry> all = new[] { config.Default }.Concat(config.Groups).Concat(config.Players);
        foreach (var e in all)
        {
            Check(e, e.ChatTagColor, "chat_tag_color");
            Check(e, e.NameColor, "name_color");
            Check(e, e.MessageColor, "message_color");
        }

        void Check(TagEntry e, string? value, string key)
        {
            if (!string.IsNullOrWhiteSpace(value) && !ColorHelper.IsValidColor(value))
                Warnings.Add($"'{e.Id}': {key} = '{value}' tanınmadı. Geçerli renkler: {string.Join(", ", ColorHelper.AvailableColors)}, teamcolor veya #RRGGBB.");
        }
    }

    private static string NormalizeTeam(string t) => t.Trim().ToUpperInvariant() switch
    {
        "T" or "TERRORIST" or "TR" => "T",
        "CT" or "COUNTERTERRORIST" or "COUNTER-TERRORIST" => "CT",
        "SPEC" or "SPECTATOR" or "SPECTATORS" => "SPEC",
        _ => string.Empty
    };

    private static TomlTable? Table(TomlTable model, string key) =>
        model.TryGetValue(key, out var o) ? o as TomlTable : null;

    private static string? Str(TomlTable t, string key) =>
        t.TryGetValue(key, out var o) && o != null ? Convert.ToString(o, CultureInfo.InvariantCulture) : null;

    private static bool Bool(TomlTable t, string key, bool fallback)
    {
        if (!t.TryGetValue(key, out var o) || o == null) return fallback;
        if (o is bool b) return b;
        return bool.TryParse(o.ToString(), out var r) ? r : fallback;
    }

    private static int Int(TomlTable t, string key, int fallback)
    {
        if (!t.TryGetValue(key, out var o) || o == null) return fallback;
        try { return Convert.ToInt32(o, CultureInfo.InvariantCulture); } catch { return fallback; }
    }

    private static double Double(TomlTable t, string key, double fallback)
    {
        if (!t.TryGetValue(key, out var o) || o == null) return fallback;
        try { return Convert.ToDouble(o, CultureInfo.InvariantCulture); } catch { return fallback; }
    }

    private static List<string>? StrList(TomlTable t, string key)
    {
        if (!t.TryGetValue(key, out var o) || o == null) return null;
        if (o is TomlArray arr)
            return arr.Where(x => x != null).Select(x => Convert.ToString(x, CultureInfo.InvariantCulture)!).ToList();
        if (o is string s) return new List<string> { s };
        return null;
    }
}

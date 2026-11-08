using MenuApi.API;
using Microsoft.Extensions.Logging;
using PanoramaHud.Contract;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Commands;
using SwiftlyS2.Shared.Events;
using SwiftlyS2.Shared.GameEventDefinitions;
using SwiftlyS2.Shared.Misc;
using SwiftlyS2.Shared.Players;
using SwiftlyS2.Shared.Plugins;
using TagSystem.Config;
using TagSystem.Core;
using TagSystem.Models;
using TagSystem.Utils;

namespace TagSystem;

[PluginMetadata(
    Id = "tagsystem",
    Version = "2.1.0",
    Name = "TagSystem",
    Author = "CanDaysa, Devrim & SoulSnatcher",
    Description = "Yetki, grup ve oyuncu bazli otomatik Scoreboard (Clan Tag) ve Chat etiket sistemi."
)]
public class Plugin(ISwiftlyCore core) : BasePlugin(core)
{
    private PluginConfig _config = new();
    private ConfigLoader? _configLoader;
    private TagManager? _tagManager;
    private TagService? _tagService;
    private TagAdminMenu? _adminMenu;
    private IMenuApi? _menuApi;
    private IPanoramaHudApi? _hudApi;
    private IHudScope? _hudScope;
    private TagPanoramaMenu? _panoramaMenu;

    private readonly List<Guid> _commandGuids = [];
    private readonly List<Guid> _gameEventGuids = [];
    private Guid _chatHookGuid;
    private CancellationTokenSource? _refreshTimer;
    private readonly Dictionary<int, long> _lastCmdTime = new();

    public override void Load(bool hotReload)
    {
        var configDir = ResolveConfigDirectory();
        _configLoader = new ConfigLoader(configDir);

        try
        {
            _config = _configLoader.Load();
            LogConfigWarnings();
        }
        catch (Exception ex)
        {
            Core.Logger.LogError(ex, "[TagSystem] Config yüklenemedi, varsayılan ayarlar kullanılıyor.");
            _config = new PluginConfig();
        }

        _tagManager = new TagManager(Core, _config);
        _tagService = new TagService(Core, _config, _tagManager);
        _adminMenu = new TagAdminMenu(Core, _configLoader, _config, _tagManager, _tagService)
        {
            MenuApi = _menuApi
        };

        RegisterCommands();
        RegisterEvents();
        StartRefreshTimer();

        if (_hudScope != null && _hudApi != null && _panoramaMenu == null)
        {
            _panoramaMenu = new TagPanoramaMenu(Core, _hudScope, _hudApi, _configLoader, _config, _tagManager, _tagService);
            _panoramaMenu.BuildMenus();
        }

        if (hotReload)
        {
            Core.Scheduler.NextTick(() => _tagService.ApplyScoreboardAll(force: true));
        }

        Core.Logger.LogInformation(
            "[TagSystem] v2.1.0 yüklendi ({Groups} grup, {Players} özel oyuncu). Menü: !{MenuCmd}",
            _config.Groups.Count, _config.Players.Count, _config.Commands.MenuCommand);
    }

    public override void UseSharedInterface(IInterfaceManager interfaceManager)
    {
        if (interfaceManager.HasSharedInterface("MenuApi"))
        {
            try
            {
                _menuApi = interfaceManager.GetSharedInterface<IMenuApi>("MenuApi");
                if (_adminMenu != null)
                {
                    _adminMenu.MenuApi = _menuApi;
                }
                Core.Logger.LogInformation("[TagSystem] SwiftlyS2-MenuApi arayüzü bağlandı.");
            }
            catch (Exception ex)
            {
                Core.Logger.LogError(ex, "[TagSystem] SwiftlyS2-MenuApi bağlanırken hata oluştu.");
            }
        }
        else
        {
            _menuApi = null;
            if (_adminMenu != null) _adminMenu.MenuApi = null;
        }
    }

    public override void Unload()
    {
        StopRefreshTimer();
        _panoramaMenu?.Dispose();
        _hudScope?.Dispose();
        _adminMenu?.CloseAll();
        UnregisterEvents();
        UnregisterCommands();

        _tagService?.RestoreScoreboardAll();

        Core.Logger.LogInformation("[TagSystem] Eklenti kapatıldı.");
    }

    private string ResolveConfigDirectory()
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(Core.Configuration.BasePath))
                return Core.Configuration.BasePath;
        }
        catch { /* fallback */ }

        return Path.Combine(Core.PluginPath, "TagSystem");
    }

    private void LogConfigWarnings()
    {
        if (_configLoader == null) return;
        foreach (var w in _configLoader.Warnings)
            Core.Logger.LogWarning("[TagSystem] Config uyarısı: {Warning}", w);
    }

    private void SaveAndRefreshAll()
    {
        if (_configLoader == null || _tagManager == null || _tagService == null) return;
        try { _configLoader.Save(_config); }
        catch (Exception ex) { Core.Logger.LogError(ex, "[TagSystem] Config diske kaydedilemedi."); }

        _tagManager.UpdateConfig(_config);
        _tagService.UpdateConfig(_config);
        _adminMenu?.UpdateConfig(_config);
        _panoramaMenu?.UpdateConfig(_config);
        _tagService.ApplyScoreboardAll(force: true);
    }

    // ============================== EVENTS & TIMERS ==============================

    private void RegisterEvents()
    {
        _chatHookGuid = Core.Command.HookClientChat(OnClientChat);

        Core.Event.OnTick += OnTick;
        Core.Event.OnClientPutInServer += OnClientPutInServer;
        Core.Event.OnClientSteamAuthorize += OnClientSteamAuthorize;
        Core.Event.OnClientDisconnected += OnClientDisconnected;
        Core.Event.OnMapLoad += OnMapLoad;

        _gameEventGuids.Add(Core.GameEvent.HookPost<EventPlayerSpawn>(OnPlayerSpawn));
        _gameEventGuids.Add(Core.GameEvent.HookPost<EventPlayerTeam>(OnPlayerTeam));
    }

    private void UnregisterEvents()
    {
        if (_chatHookGuid != Guid.Empty)
        {
            Core.Command.UnhookClientChat(_chatHookGuid);
            _chatHookGuid = Guid.Empty;
        }

        Core.Event.OnTick -= OnTick;
        Core.Event.OnClientPutInServer -= OnClientPutInServer;
        Core.Event.OnClientSteamAuthorize -= OnClientSteamAuthorize;
        Core.Event.OnClientDisconnected -= OnClientDisconnected;
        Core.Event.OnMapLoad -= OnMapLoad;

        foreach (var g in _gameEventGuids)
            Core.GameEvent.Unhook(g);
        _gameEventGuids.Clear();
    }

    private void StartRefreshTimer()
    {
        StopRefreshTimer();
        if (_config.Settings.EnableScoreboardTag && _config.Settings.ScoreboardRefreshSeconds > 0.5f)
        {
            _refreshTimer = Core.Scheduler.RepeatBySeconds(_config.Settings.ScoreboardRefreshSeconds, () =>
            {
                _tagService?.ApplyScoreboardAll(force: false);
            });
        }
    }

    private void StopRefreshTimer()
    {
        try { _refreshTimer?.Cancel(); } catch { /* yoksay */ }
        _refreshTimer = null;
    }

    private void OnTick()
    {
        _adminMenu?.OnTick();
        _panoramaMenu?.OnTick();
    }

    private HookResult OnClientChat(int playerId, string text, bool teamOnly)
    {
        if (_panoramaMenu != null && _panoramaMenu.TryConsumeChatInput(playerId, text))
            return HookResult.CancelOriginal;

        if (_adminMenu != null && _adminMenu.TryConsumeChatInput(playerId, text))
            return HookResult.CancelOriginal;

        // Sessiz TagSystem komutu ise (/tagmenu vb.): sohbette gostermeden calistir ve orijinali iptal et.
        if (IsSilentTagSystemCommand(text) && TryHandleFallbackChatCommand(playerId, text))
            return HookResult.CancelOriginal;

        // Normal sohbet veya acik komutlar (!ws, !tagmenu vb.):
        // Komut yazarken de etiket gorunmeye devam eder (ornegin: "[VIP] Devrim: !ws").
        var result = _tagService != null
            ? _tagService.HandleChat(playerId, text ?? string.Empty, teamOnly)
            : HookResult.Continue;

        // Acik TagSystem komutu ise (!tagmenu, !tagcheck vb.):
        // Once oyuncu mesaji etiketli basilir, ardindan komut isletilir.
        if (TryHandleFallbackChatCommand(playerId, text ?? string.Empty))
            return HookResult.CancelOriginal;

        return result;
    }

    private void OnClientPutInServer(IOnClientPutInServerEvent e)
    {
        var playerId = e.PlayerId;
        Core.Scheduler.NextTick(() =>
        {
            var player = Core.PlayerManager.GetPlayer(playerId);
            if (!PlayerHelper.IsUsable(player)) return;
            if (_config.Settings.CacheOnConnect) _tagManager?.Invalidate(player!.Slot);
            _tagService?.ApplyScoreboard(player);
        });
    }

    private void OnClientSteamAuthorize(IOnClientSteamAuthorizeEvent e)
    {
        var playerId = e.PlayerId;
        Core.Scheduler.NextTick(() =>
        {
            var player = Core.PlayerManager.GetPlayer(playerId);
            if (!PlayerHelper.IsUsable(player)) return;
            _tagManager?.Invalidate(player!.Slot);
            _tagService?.ApplyScoreboard(player);
        });
    }

    private void OnClientDisconnected(IOnClientDisconnectedEvent e)
    {
        _lastCmdTime.Remove(e.PlayerId);
        _panoramaMenu?.OnPlayerDisconnected(e.PlayerId);
        _adminMenu?.OnPlayerDisconnected(e.PlayerId);
        _tagService?.OnPlayerDisconnected(e.PlayerId);
    }

    private void OnMapLoad(IOnMapLoadEvent e)
    {
        _lastCmdTime.Clear();
        _panoramaMenu?.OnMapLoad();
        _adminMenu?.CloseAll();
        _tagService?.OnMapChanged();
    }

    private HookResult OnPlayerSpawn(EventPlayerSpawn e)
    {
        var player = e.UserIdPlayer;
        if (PlayerHelper.IsUsable(player))
        {
            var slot = player!.Slot;
            Core.Scheduler.NextTick(() =>
            {
                var p = Core.PlayerManager.GetPlayer(slot);
                _tagService?.ApplyScoreboard(p);
            });
        }
        return HookResult.Continue;
    }

    private HookResult OnPlayerTeam(EventPlayerTeam e)
    {
        var player = e.UserIdPlayer;
        if (PlayerHelper.IsUsable(player))
        {
            var slot = player!.Slot;
            Core.Scheduler.NextTick(() =>
            {
                var p = Core.PlayerManager.GetPlayer(slot);
                if (!PlayerHelper.IsUsable(p)) return;
                _tagManager?.Invalidate(p!.Slot);
                _tagService?.ApplyScoreboard(p);
            });
        }
        return HookResult.Continue;
    }

    // ================================= COMMANDS =================================

    private void RegisterCommands()
    {
        UnregisterCommands();
        var c = _config.Commands;

        RegisterDual(c.MenuCommand, OnMenuCommand, "TagSystem görsel admin menüsünü açar.");
        RegisterDual("tagsmenu", OnMenuCommand, "TagSystem görsel admin menüsünü açar.");
        RegisterDual("etiketmenu", OnMenuCommand, "TagSystem görsel admin menüsünü açar.");
        RegisterDual(c.GiveGroupCommand, OnGiveGroupCommand, "Oyuncuyu bir etiket grubuna ekler/çıkarır: !tagver <hedef> <grup_id>");
        RegisterDual(c.SetCustomTagCommand, OnSetCustomTagCommand, "Oyuncuya özel etiket verir: !ozeltag <hedef> <etiket> [renk] [isim_rengi] [öncelik]");
        RegisterDual(c.RemoveTagCommand, OnRemoveTagCommand, "Oyuncunun özel etiketini/grup üyeliklerini siler: !tagsil <hedef> [grup_id]");
        RegisterDual(c.AddGroupCommand, OnAddGroupCommand, "Yeni yetki grubu ekler: !grupekle <id> <yetki> <etiket> [renk] [öncelik]");
        RegisterDual(c.ReloadCommand, OnReloadCommand, "TagSystem yapılandırmasını yeniden yükler.");
        RegisterDual(c.CheckCommand, OnCheckCommand, "Bir oyuncunun etiket durumunu inceler.");
    }

    private void RegisterDual(string? name, ICommandService.CommandListener handler, string help)
    {
        if (string.IsNullOrWhiteSpace(name)) return;
        _commandGuids.Add(Core.Command.RegisterCommand(name, handler, registerRaw: false, helpText: help));
        _commandGuids.Add(Core.Command.RegisterCommand(name, handler, registerRaw: true, helpText: help));
    }

    private void UnregisterCommands()
    {
        foreach (var g in _commandGuids)
            Core.Command.UnregisterCommand(g);
        _commandGuids.Clear();
    }

    private static bool IsSilentTagSystemCommand(string? text)
    {
        var trimmed = (text ?? string.Empty).Trim();
        if (trimmed.Length >= 2 && trimmed[0] == '"' && trimmed[^1] == '"')
            trimmed = trimmed[1..^1].Trim();
        return trimmed.Length >= 2 && trimmed[0] == '/';
    }

    private bool TryHandleFallbackChatCommand(int playerId, string text)
    {
        var trimmed = (text ?? string.Empty).Trim();
        if (trimmed.Length >= 2 && trimmed[0] == '"' && trimmed[^1] == '"')
            trimmed = trimmed[1..^1].Trim();

        if (trimmed.Length < 2 || (trimmed[0] != '!' && trimmed[0] != '/'))
            return false;

        var args = SplitArguments(trimmed[1..]);
        if (args.Count == 0) return false;

        var cmd = args[0];
        var rest = args.Skip(1).ToArray();
        var player = Core.PlayerManager.GetPlayer(playerId);
        if (!PlayerHelper.IsUsable(player)) return false;

        var c = _config.Commands;

        if (Matches(cmd, c.MenuCommand, "tagsmenu", "etiketmenu"))
        {
            ExecuteMenuCommand(player!);
            return true;
        }
        if (Matches(cmd, c.GiveGroupCommand))
        {
            ExecuteGiveGroupCommand(player, null, rest);
            return true;
        }
        if (Matches(cmd, c.SetCustomTagCommand))
        {
            ExecuteSetCustomTagCommand(player, null, rest);
            return true;
        }
        if (Matches(cmd, c.RemoveTagCommand))
        {
            ExecuteRemoveTagCommand(player, null, rest);
            return true;
        }
        if (Matches(cmd, c.AddGroupCommand))
        {
            ExecuteAddGroupCommand(player, null, rest);
            return true;
        }
        if (Matches(cmd, c.ReloadCommand))
        {
            ExecuteReloadCommand(player, null);
            return true;
        }
        if (Matches(cmd, c.CheckCommand))
        {
            ExecuteCheckCommand(player, null, rest);
            return true;
        }

        return false;
    }

    private static bool Matches(string input, params string?[] names) =>
        names.Any(n => !string.IsNullOrWhiteSpace(n) && n.Equals(input, StringComparison.OrdinalIgnoreCase));

    /// <summary>Tirnak icindeki ifadeleri ("[KRAL OYUNCU]" gibi) tek arguman olarak ayristirir.</summary>
    private static List<string> SplitArguments(string commandLine)
    {
        var list = new List<string>();
        if (string.IsNullOrWhiteSpace(commandLine)) return list;

        var cur = new System.Text.StringBuilder();
        bool inQuotes = false;

        foreach (char ch in commandLine)
        {
            if (ch == '"')
            {
                inQuotes = !inQuotes;
                continue;
            }
            if (char.IsWhiteSpace(ch) && !inQuotes)
            {
                if (cur.Length > 0) { list.Add(cur.ToString()); cur.Clear(); }
            }
            else
            {
                cur.Append(ch);
            }
        }
        if (cur.Length > 0) list.Add(cur.ToString());
        return list;
    }

    private bool CheckCallerPermission(IPlayer? caller)
    {
        if (!PlayerHelper.IsUsable(caller)) return true; // sunucu konsolu
        if (IsThrottled(caller!)) return false;
        if (!HasAdminPermission(caller!))
        {
            _tagService?.Reply(caller, "[red]Bu komutu kullanmak için yetkiniz yok.[default]");
            return false;
        }
        return true;
    }

    private bool IsThrottled(IPlayer player)
    {
        var now = Environment.TickCount64;
        if (_lastCmdTime.TryGetValue(player.Slot, out var last) && Math.Abs(now - last) < 200)
            return true;
        _lastCmdTime[player.Slot] = now;
        return false;
    }

    /// <summary>Sorgudan (SteamID2/3/64, #slot veya oyuncu ismi) SteamID64 ve gorunen isim cozer.</summary>
    private bool TryResolveTarget(string query, out ulong steamId64, out string displayName)
    {
        var parsedSid = SteamIdHelper.Parse(query);
        if (parsedSid != 0)
        {
            steamId64 = parsedSid;
            var onlineBySid = Core.PlayerManager.GetAllPlayers()
                .FirstOrDefault(p => PlayerHelper.IsUsable(p) && PlayerHelper.GetSteamId64(p) == parsedSid);
            displayName = onlineBySid != null ? PlayerHelper.GetName(onlineBySid) : SteamIdHelper.ToSteam2(parsedSid);
            return true;
        }

        steamId64 = 0;

        var all = Core.PlayerManager.GetAllPlayers().Where(PlayerHelper.IsUsable).ToList();
        var cleanSlot = query.TrimStart('#');
        var match = all.FirstOrDefault(p =>
            (int.TryParse(cleanSlot, out var slot) && p!.Slot == slot) ||
            PlayerHelper.GetName(p).Equals(query, StringComparison.OrdinalIgnoreCase))
            ?? all.FirstOrDefault(p => PlayerHelper.GetName(p).Contains(query, StringComparison.OrdinalIgnoreCase));

        if (match != null)
        {
            steamId64 = PlayerHelper.GetSteamId64(match);
            displayName = PlayerHelper.GetName(match);
            return steamId64 != 0;
        }

        displayName = string.Empty;
        return false;
    }

    // 1) !tagmenu
    private void OnMenuCommand(ICommandContext ctx)
    {
        if (!PlayerHelper.IsUsable(ctx.Sender))
        {
            ctx.Reply("[TagSystem] Görsel menü yalnızca oyun içinden (!tagmenu) açılabilir. Konsol komutları: sw_tagver, sw_ozeltag, sw_tagsil, sw_grupekle, sw_tagreload, sw_tagcheck");
            return;
        }
        ExecuteMenuCommand(ctx.Sender!);
    }

    private void ExecuteMenuCommand(IPlayer admin)
    {
        if (!CheckCallerPermission(admin)) return;
        if (_adminMenu != null && _adminMenu.MenuApi != null)
        {
            _adminMenu.Open(admin);
            return;
        }
        if (_panoramaMenu != null && _panoramaMenu.IsAvailable)
        {
            _panoramaMenu.Open(admin);
            return;
        }
        _adminMenu?.Toggle(admin);
    }

    // 2) !tagver <oyuncu/steamid> <grup_id>
    private void OnGiveGroupCommand(ICommandContext ctx) =>
        ExecuteGiveGroupCommand(ctx.Sender, ctx, SplitArguments(string.Join(" ", ctx.Args)).ToArray());

    private void ExecuteGiveGroupCommand(IPlayer? caller, ICommandContext? ctx, string[] args)
    {
        if (!CheckCallerPermission(caller)) return;

        if (args.Length < 2)
        {
            SendOutput(caller, ctx, $"Kullanım: [lime]!{_config.Commands.GiveGroupCommand} <oyuncu/steamid> <grup_id>[default] (Mevcut gruplar: {string.Join(", ", _config.Groups.Select(g => g.Id))})");
            return;
        }

        if (!TryResolveTarget(args[0], out var sid, out var targetName))
        {
            SendOutput(caller, ctx, $"[red]'{args[0]}' oyuncusu bulunamadı veya geçerli bir SteamID değil.[default]");
            return;
        }

        var group = _config.Groups.FirstOrDefault(g => g.Id.Equals(args[1], StringComparison.OrdinalIgnoreCase));
        if (group == null)
        {
            SendOutput(caller, ctx, $"[red]'{args[1]}' adında bir grup bulunamadı.[default] Mevcut gruplar: [gold]{string.Join(", ", _config.Groups.Select(g => g.Id))}[default]");
            return;
        }

        group.Members.Add(sid);
        SaveAndRefreshAll();
        SendOutput(caller, ctx, $"[lime]{targetName}[default] ({SteamIdHelper.ToSteam2(sid)}) oyuncusu [gold]{group.Id}[default] ({group.ChatTag}) grubuna eklendi ve kaydedildi.");
    }

    // 3) !ozeltag <oyuncu/steamid> <etiket> [etiket_rengi] [isim_rengi] [oncelik]
    private void OnSetCustomTagCommand(ICommandContext ctx) =>
        ExecuteSetCustomTagCommand(ctx.Sender, ctx, SplitArguments(string.Join(" ", ctx.Args)).ToArray());

    private void ExecuteSetCustomTagCommand(IPlayer? caller, ICommandContext? ctx, string[] args)
    {
        if (!CheckCallerPermission(caller)) return;

        if (args.Length < 2)
        {
            SendOutput(caller, ctx, $"Kullanım: [lime]!{_config.Commands.SetCustomTagCommand} <oyuncu/steamid> \"<etiket>\" [etiket_rengi] [isim_rengi] [öncelik][default]");
            SendOutput(caller, ctx, $"Örnek: [gold]!{_config.Commands.SetCustomTagCommand} Devrim \"[KRAL]\" gold red 1000[default]");
            return;
        }

        if (!TryResolveTarget(args[0], out var sid, out var targetName))
        {
            SendOutput(caller, ctx, $"[red]'{args[0]}' oyuncusu bulunamadı veya geçerli bir SteamID değil.[default]");
            return;
        }

        var tagText = args[1];
        var tagColor = args.Length >= 3 ? args[2] : "gold";
        var nameColor = args.Length >= 4 ? args[3] : tagColor;
        var priority = args.Length >= 5 && int.TryParse(args[4], out var p) ? p : 1000;

        var existing = _config.Players.FirstOrDefault(x => x.SteamId64 == sid);
        if (existing == null)
        {
            existing = new PlayerData
            {
                Id = $"player:{sid}",
                SteamId64 = sid,
                RawSteamId = SteamIdHelper.ToSteam2(sid),
            };
            _config.Players.Add(existing);
        }

        existing.DisplayName = targetName;
        existing.ChatTag = tagText;
        existing.ScoreboardTag = TagService.StripOuterBrackets(tagText);
        existing.ChatTagColor = tagColor;
        existing.NameColor = nameColor;
        existing.Priority = priority;
        existing.Enabled = true;

        SaveAndRefreshAll();
        SendOutput(caller, ctx, $"[lime]{targetName}[default] ({SteamIdHelper.ToSteam2(sid)}) oyuncusuna [gold]{tagText}[default] özel etiketi atandı ve kaydedildi.");
    }

    // 4) !tagsil <oyuncu/steamid> [grup_id]
    private void OnRemoveTagCommand(ICommandContext ctx) =>
        ExecuteRemoveTagCommand(ctx.Sender, ctx, SplitArguments(string.Join(" ", ctx.Args)).ToArray());

    private void ExecuteRemoveTagCommand(IPlayer? caller, ICommandContext? ctx, string[] args)
    {
        if (!CheckCallerPermission(caller)) return;

        if (args.Length < 1)
        {
            SendOutput(caller, ctx, $"Kullanım: [lime]!{_config.Commands.RemoveTagCommand} <oyuncu/steamid> [grup_id][default]");
            return;
        }

        if (!TryResolveTarget(args[0], out var sid, out var targetName))
        {
            SendOutput(caller, ctx, $"[red]'{args[0]}' oyuncusu bulunamadı veya geçerli bir SteamID değil.[default]");
            return;
        }

        if (args.Length >= 2)
        {
            var group = _config.Groups.FirstOrDefault(g => g.Id.Equals(args[1], StringComparison.OrdinalIgnoreCase));
            if (group == null || !group.Members.Remove(sid))
            {
                SendOutput(caller, ctx, $"[red]{targetName} oyuncusu '{args[1]}' grubunda bulunamadı.[default]");
                return;
            }
            SaveAndRefreshAll();
            SendOutput(caller, ctx, $"[lime]{targetName}[default] oyuncusu [gold]{group.Id}[default] grubundan çıkarıldı.");
            return;
        }

        int removedPlayers = _config.Players.RemoveAll(x => x.SteamId64 == sid);
        int removedGroups = _config.Groups.Count(g => g.Members.Remove(sid));

        if (removedPlayers == 0 && removedGroups == 0)
        {
            SendOutput(caller, ctx, $"[red]{targetName} için kayıtlı özel etiket veya grup üyeliği bulunamadı.[default]");
            return;
        }

        SaveAndRefreshAll();
        SendOutput(caller, ctx, $"[lime]{targetName}[default] oyuncusunun tüm özel etiket ve grup üyelikleri kaldırıldı.");
    }

    // 5) !grupekle <id> <yetki> <etiket> [renk] [oncelik]
    private void OnAddGroupCommand(ICommandContext ctx) =>
        ExecuteAddGroupCommand(ctx.Sender, ctx, SplitArguments(string.Join(" ", ctx.Args)).ToArray());

    private void ExecuteAddGroupCommand(IPlayer? caller, ICommandContext? ctx, string[] args)
    {
        if (!CheckCallerPermission(caller)) return;

        if (args.Length < 3)
        {
            SendOutput(caller, ctx, $"Kullanım: [lime]!{_config.Commands.AddGroupCommand} <id> <yetki> \"<etiket>\" [renk] [öncelik][default]");
            SendOutput(caller, ctx, $"Örnek: [gold]!{_config.Commands.AddGroupCommand} mod tagsystem.mod \"[MOD]\" lightblue 50[default]");
            return;
        }

        var id = args[0].Trim().ToLowerInvariant();
        var perm = args[1].Trim();
        var tagText = args[2];
        var color = args.Length >= 4 ? args[3] : "gold";
        var priority = args.Length >= 5 && int.TryParse(args[4], out var p) ? p : 50;

        var group = _config.Groups.FirstOrDefault(g => g.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
        bool isNew = group == null;
        if (group == null)
        {
            group = new GroupData { Id = id };
            _config.Groups.Add(group);
        }

        group.DisplayName = id.ToUpperInvariant();
        if (!string.IsNullOrWhiteSpace(perm) && perm != "-" && !group.Permissions.Contains(perm, StringComparer.OrdinalIgnoreCase))
            group.Permissions.Add(perm);
        group.ChatTag = tagText;
        group.ScoreboardTag = TagService.StripOuterBrackets(tagText);
        group.ChatTagColor = color;
        group.NameColor = color;
        group.Priority = priority;
        group.Enabled = true;

        SaveAndRefreshAll();
        SendOutput(caller, ctx, $"[gold]{id}[default] grubu ({tagText}, yetki: {perm}, öncelik: {priority}) {(isNew ? "oluşturuldu" : "güncellendi")} ve kaydedildi.");
    }

    // 6) !tagreload
    private void OnReloadCommand(ICommandContext ctx) => ExecuteReloadCommand(ctx.Sender, ctx);

    private void ExecuteReloadCommand(IPlayer? player, ICommandContext? ctx)
    {
        if (!CheckCallerPermission(player)) return;
        if (_configLoader == null || _tagManager == null || _tagService == null) return;

        try
        {
            var newConfig = _configLoader.Load();

            var wasScoreboardEnabled = _config.Settings.EnableScoreboardTag;
            _config = newConfig;
            _tagManager.UpdateConfig(_config);
            _tagService.UpdateConfig(_config);
            _adminMenu?.UpdateConfig(_config);
            _panoramaMenu?.UpdateConfig(_config);

            RegisterCommands();
            StartRefreshTimer();

            if (_config.Settings.EnableScoreboardTag)
                _tagService.ApplyScoreboardAll(force: true);
            else if (wasScoreboardEnabled)
                _tagService.RestoreScoreboardAll();

            LogConfigWarnings();

            var msg = $"Config yeniden yüklendi ({_config.Groups.Count} grup, {_config.Players.Count} özel oyuncu, {_configLoader.Warnings.Count} uyarı).";
            SendOutput(player, ctx, $"[lime]{msg}[default]");
            Core.Logger.LogInformation("[TagSystem] {Message}", msg);
        }
        catch (Exception ex)
        {
            var err = $"Config yüklenemedi (eski ayarlar korundu): {ex.Message}";
            SendOutput(player, ctx, $"[red]{err}[default]");
            Core.Logger.LogError(ex, "[TagSystem] Config reload hatası.");
        }
    }

    // 7) !tagcheck
    private void OnCheckCommand(ICommandContext ctx) => ExecuteCheckCommand(ctx.Sender, ctx, ctx.Args);

    private void ExecuteCheckCommand(IPlayer? caller, ICommandContext? ctx, string[] args)
    {
        if (!CheckCallerPermission(caller)) return;
        if (_tagManager == null || _tagService == null) return;

        IPlayer? target = caller;
        if (args.Length > 0)
        {
            var q = string.Join(" ", args).Trim();
            var qSid = SteamIdHelper.Parse(q);
            var all = Core.PlayerManager.GetAllPlayers().Where(PlayerHelper.IsUsable).ToList();

            target = all.FirstOrDefault(p =>
                (qSid != 0 && PlayerHelper.GetSteamId64(p) == qSid) ||
                (int.TryParse(q.TrimStart('#'), out var slot) && p!.Slot == slot) ||
                PlayerHelper.GetName(p).Contains(q, StringComparison.OrdinalIgnoreCase));
        }

        if (!PlayerHelper.IsUsable(target))
        {
            SendOutput(caller, ctx, "[red]Hedef oyuncu bulunamadı. Kullanım: !tagcheck <isim / slot / steamid>[default]");
            return;
        }

        var sid = PlayerHelper.GetSteamId64(target);
        var resolved = _tagManager.Resolve(target);
        var matches = _tagManager.GetMatchingEntries(target!);
        var sbText = _tagService.BuildScoreboardText(target, resolved);

        SendOutput(caller, ctx,
            $"Oyuncu: [lime]{PlayerHelper.GetName(target)}[default] | SteamID: [gold]{SteamIdHelper.ToSteam2(sid)}[default] ({sid}) | Takım: {PlayerHelper.GetTeam(target)}");
        SendOutput(caller, ctx,
            $"Uygulanan: [lime]{resolved.Label}[default] (id: {resolved.SourceId}, öncelik: {resolved.Priority}) | Clan: '{sbText}'");
        SendOutput(caller, ctx,
            $"Eşleşen tanımlar ({matches.Count}): " + string.Join(", ", matches.Select(e => $"{e.Id}[{e.Priority}]")));
    }

    private void SendOutput(IPlayer? caller, ICommandContext? ctx, string message)
    {
        if (PlayerHelper.IsUsable(caller))
            _tagService?.Reply(caller, message);
        else
            ctx?.Reply("[TagSystem] " + ColorHelper.StripAll(message));
    }

    private bool HasAdminPermission(IPlayer player)
    {
        var perm = _config.Commands.AdminPermission;
        if (string.IsNullOrWhiteSpace(perm)) return true;
        var sid = PlayerHelper.GetSteamId64(player);
        if (sid == 0) return false;
        try
        {
            return Core.Permission.PlayerHasPermission(sid, perm) ||
                   Core.Permission.PlayerHasPermission(sid, "admin") ||
                   Core.Permission.PlayerHasPermission(sid, "root");
        }
        catch { return false; }
    }
}

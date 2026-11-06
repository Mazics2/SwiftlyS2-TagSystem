using System.Net;
using System.Text;
using Microsoft.Extensions.Logging;
using PanoramaHud.Contract;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Players;
using TagSystem.Config;
using TagSystem.Models;
using TagSystem.Utils;

namespace TagSystem.Core;

/// <summary>
/// Panorama HUD entegrasyonu: TagSystem görsel yönetim menüsü, canlı renkli mesaj önizleme widget'ı,
/// CenterHTML canlı renkli kutusu ve onay diyalogları.
/// W/S/A/D, rakam (1-9) ve fare modlarını native olarak destekler.
/// </summary>
public sealed class TagPanoramaMenu : IDisposable
{
    public sealed record ColorInfo(string Key, string Emoji, string Hex, string TurkishName);

    public static readonly ColorInfo[] ColorPalette =
    {
        new("gold", "🟡", "#E0B84C", "Altın (Gold)"),
        new("lightred", "🔴", "#FF6B6B", "Açık Kırmızı"),
        new("red", "🔴", "#E5534B", "Kırmızı"),
        new("darkred", "🍷", "#8B0000", "Koyu Kırmızı"),
        new("lightblue", "🌐", "#58A6FF", "Açık Mavi"),
        new("blue", "🔵", "#1F6FEB", "Mavi"),
        new("darkblue", "🔷", "#093F8F", "Koyu Mavi"),
        new("lime", "🟢", "#3FB950", "Açık Yeşil (Lime)"),
        new("green", "🌲", "#238636", "Yeşil"),
        new("olive", "🫒", "#808000", "Zeytin Yeşili"),
        new("yellow", "🟡", "#D29922", "Sarı"),
        new("lightyellow", "🌕", "#F2CC60", "Açık Sarı"),
        new("orange", "🟠", "#DB6D28", "Turuncu"),
        new("purple", "🟣", "#8957E5", "Mor"),
        new("lightpurple", "🌸", "#BC8CFF", "Açık Mor"),
        new("magenta", "💖", "#D157A5", "Magenta"),
        new("grey", "⚫", "#8B949E", "Gri"),
        new("silver", "🔘", "#C9D1D9", "Gümüş"),
        new("white", "⚪", "#FFFFFF", "Beyaz"),
        new("bluegrey", "🏛️", "#64788C", "Mavi Gri"),
        new("teamcolor", "👥", "#38BDF8", "Takım Rengi"),
        // Özel Çok Renkli ve Efekt Renkleri
        new("rainbow", "🌈", "#FF6B6B", "Gökkuşağı (Rainbow)"),
        new("fire", "🔥", "#FF4500", "Ateş (Fire)"),
        new("ice", "❄️", "#00BFFF", "Buz (Ice)"),
        new("neon", "⚡", "#39FF14", "Neon"),
        new("sunset", "🌇", "#FD5E53", "Günbatımı (Sunset)"),
        new("toxic", "☣️", "#7FFF00", "Toksik (Toxic)"),
        new("police", "🚓", "#0055FF", "Polis (Police)"),
        new("blood", "🩸", "#8B0000", "Kan (Blood)"),
        new("royal", "👑", "#FFD700", "Kraliyet (Royal)"),
        new("matrix", "💾", "#00FF66", "Matrix"),
        new("pastel", "🎨", "#FFB6C1", "Pastel"),
        // Yeni Tematik Gradyanlar
        new("galaxy", "🌌", "#7928CA", "Galaksi (Galaxy)"),
        new("cyberpunk", "🤖", "#FCEE0A", "Siberpunk"),
        new("lava", "🌋", "#FF3300", "Lav (Lava)"),
        new("aurora", "🌌", "#00FFCC", "Kuzey Işıkları"),
        new("ghost", "👻", "#A0AEC0", "Hayalet (Ghost)"),
        new("nature", "🍃", "#48BB78", "Doğa (Nature)"),
        new("candy", "🍬", "#F687B3", "Pamuk Şeker"),
        new("goldrush", "💰", "#ECC94B", "Altın Avcısı"),
        // Çift Renkli (Dama) Desenler
        new("redblue", "🔴🔵", "#E53E3E", "Dama: Kırmızı-Mavi"),
        new("pinkblue", "💖🔷", "#ED64A6", "Dama: Pembe-Mavi"),
        new("goldblack", "🟡⚫", "#D69E2E", "Dama: Altın-Gri"),
        new("greenyellow", "🟢🟡", "#38A169", "Dama: Yeşil-Sarı"),
        new("blackwhite", "⚫⚪", "#718096", "Dama: Siyah-Beyaz"),
        new("redwhite", "🔴⚪", "#E53E3E", "Dama: Kırmızı-Beyaz"),
        new("default", "💬", "#E2E8F0", "Varsayılan")
    };

    public static readonly string[] ColorKeys = ColorPalette.Select(c => c.Key).ToArray();
    public static readonly string[] ColorDisplayOptions = ColorPalette.Select(c => $"{c.Emoji} {c.TurkishName}").ToArray();

    public static readonly string[] FrameKeys = PlaceholderHelper.AvailableFrames.Select(f => f.Key).ToArray();
    public static readonly string[] FrameDisplayOptions = PlaceholderHelper.AvailableFrames.Select(f => f.Name).ToArray();

    private static readonly string[][] TeamPresets =
    {
        Array.Empty<string>(),
        new[] { "T" },
        new[] { "CT" },
        new[] { "SPEC" }
    };

    private static readonly string[] TeamPresetLabels = { "Herkes", "Sadece T", "Sadece CT", "Sadece İzleyici" };

    private static readonly string[] PriorityLabels = { "0 (Varsayılan)", "20 (Düşük)", "50 (Normal)", "100 (Yüksek)", "500 (Admin)", "1000 (Özel)" };
    private static readonly int[] PriorityValues = { 0, 20, 50, 100, 500, 1000 };

    private readonly ISwiftlyCore _core;
    private readonly IHudScope _hud;
    private readonly IPanoramaHudApi _api;
    private readonly ConfigLoader _loader;
    private readonly TagManager _manager;
    private readonly TagService _service;
    private PluginConfig _config;

    private readonly Dictionary<int, (string Prompt, Action<IPlayer, string> Callback, Action<IPlayer>? Reopen)> _pendingInput = new();
    private readonly Dictionary<int, TagEntry?> _centerHtmlActive = new();
    private readonly HashSet<int> _centerHtmlDisabled = new();
    private float _lastCenterHtmlTick = 0f;

    private IMenu? _mainMenu;
    private IMenu? _onlinePlayersMenu;
    private IMenu? _customPlayersMenu;
    private IMenu? _groupsMenu;
    private IMenu? _settingsMenu;

    public bool IsAvailable => _hud != null;

    public TagPanoramaMenu(
        ISwiftlyCore core,
        IHudScope hud,
        IPanoramaHudApi api,
        ConfigLoader loader,
        PluginConfig config,
        TagManager manager,
        TagService service)
    {
        _core = core;
        _hud = hud;
        _api = api;
        _loader = loader;
        _config = config;
        _manager = manager;
        _service = service;

        _api.MenuClosed += OnMenuClosed;
    }

    public void UpdateConfig(PluginConfig config)
    {
        _config = config;
    }

    public void Dispose()
    {
        _api.MenuClosed -= OnMenuClosed;
        foreach (var slot in _centerHtmlActive.Keys)
        {
            var p = _core.PlayerManager.GetPlayer(slot);
            if (PlayerHelper.IsUsable(p)) p!.SendCenterHTML(" ", 1);
        }
        _pendingInput.Clear();
        _centerHtmlActive.Clear();
        _centerHtmlDisabled.Clear();
    }

    public void OnPlayerDisconnected(int slot)
    {
        _pendingInput.Remove(slot);
        _centerHtmlActive.Remove(slot);
        _centerHtmlDisabled.Remove(slot);
    }

    public void OnMapLoad()
    {
        _pendingInput.Clear();
        _centerHtmlActive.Clear();
    }

    private void OnMenuClosed(IPlayer player, string menuId, CloseReason reason)
    {
        if (menuId.StartsWith("tagsystem", StringComparison.OrdinalIgnoreCase))
        {
            _centerHtmlActive.Remove(player.Slot);
            player.SendCenterHTML(" ", 1);
        }
    }

    private void SaveAndApply()
    {
        try { _loader.Save(_config); }
        catch (Exception ex) { _core.Logger.LogError(ex, "[TagSystem] Config kaydedilemedi."); }

        _manager.UpdateConfig(_config);
        _service.UpdateConfig(_config);
        _service.ApplyScoreboardAll(force: true);
    }

    // ================================= TICK =================================

    public void OnTick()
    {
        if (_centerHtmlActive.Count == 0) return;

        var now = _core.Engine.GlobalVars.CurrentTime;
        if (now - _lastCenterHtmlTick < 0.25f) return;
        _lastCenterHtmlTick = now;

        foreach (var (slot, entry) in _centerHtmlActive.ToList())
        {
            if (_centerHtmlDisabled.Contains(slot)) continue;
            var player = _core.PlayerManager.GetPlayer(slot);
            if (!PlayerHelper.IsUsable(player))
            {
                _centerHtmlActive.Remove(slot);
                continue;
            }
            RenderCenterHtmlPreview(player!, entry);
        }
    }

    // ================================= MENU INITIALIZATION =================================

    public void BuildMenus()
    {
        _mainMenu = BuildMainMenu();
        _onlinePlayersMenu = BuildOnlinePlayersMenu();
        _customPlayersMenu = BuildCustomPlayersMenu();
        _groupsMenu = BuildGroupsMenu();
        _settingsMenu = BuildSettingsMenu();
    }

    public void Open(IPlayer admin)
    {
        if (_mainMenu == null) BuildMenus();
        _centerHtmlActive[admin.Slot] = null;
        RenderCenterHtmlPreview(admin, null);
        _mainMenu?.Open(admin);
    }

    private IMenu BuildMainMenu()
    {
        return _hud.Menu("tagsystem.main", "✦ TagSystem Yönetim ✦")
            .Description(p =>
            {
                var resolved = _manager.Resolve(p);
                var clan = _service.BuildScoreboardText(p, resolved);
                var clanStr = string.IsNullOrEmpty(clan) ? "(boş)" : clan;
                return $"Etiketiniz: {resolved.Label} | Clan: {clanStr}";
            })
            // Menü Butonları
            .Button("👤 Oyuncuya Etiket / Grup Ver", ctx =>
            {
                _onlinePlayersMenu?.Open(ctx.Player);
                _centerHtmlActive[ctx.Player.Slot] = null;
                RenderCenterHtmlPreview(ctx.Player, null);
            })
            .Button("⭐ Özel Oyuncu Etiketleri", ctx =>
            {
                _customPlayersMenu?.Open(ctx.Player);
                _centerHtmlActive[ctx.Player.Slot] = null;
                RenderCenterHtmlPreview(ctx.Player, null);
            })
            .Button("🛡️ Yetki Gruplarını Yönet", ctx =>
            {
                _groupsMenu?.Open(ctx.Player);
                _centerHtmlActive[ctx.Player.Slot] = null;
                RenderCenterHtmlPreview(ctx.Player, null);
            })
            .Button("➕ Yeni Yetki Grubu Oluştur", ctx => StartCreateGroupFlow(ctx.Player))
            .Button("⚙️ Varsayılan [default] Etiket", ctx =>
            {
                OpenEntryEditMenu(ctx.Player, _config.Default, "Varsayılan [default] Etiket", onBack: p => _mainMenu?.Open(p));
            })
            .Button("🔧 Genel Ayarlar", ctx =>
            {
                _settingsMenu?.Open(ctx.Player);
            })
            .Button("💬 Sohbete Canlı Test Mesajı At", ctx =>
            {
                _service.SendTestChat(ctx.Player, null, "TagSystem Panorama HUD canlı renkli chat önizlemesi!");
                _hud.Toast(ctx.Player, new ToastOptions
                {
                    Title = "Test Mesajı",
                    Text = "Renkli sohbet satırı oyun içi chat kutunuza basıldı!",
                    Kind = ToastKind.Success,
                    Key = "tagsystem.testchat"
                });
            })
            .Toggle("✨ Canlı Renkli CenterHTML Önizlemesi",
                p => !_centerHtmlDisabled.Contains(p.Slot),
                (ctx, v) =>
                {
                    if (v)
                    {
                        _centerHtmlDisabled.Remove(ctx.Player.Slot);
                        RenderCenterHtmlPreview(ctx.Player, null);
                    }
                    else
                    {
                        _centerHtmlDisabled.Add(ctx.Player.Slot);
                        ctx.Player.SendCenterHTML(" ", 1);
                    }
                    ctx.Refresh();
                })
            .Button("❌ Menüyü Kapat", ctx => ctx.Close())
            .Build();
    }

    private IMenu BuildOnlinePlayersMenu()
    {
        return _hud.Menu("tagsystem.players.online", "👤 Aktif Oyuncular")
            .Description("Etiket veya grup atamak istediğiniz oyuncuyu seçin")
            .Items((admin, b) =>
            {
                var players = _core.PlayerManager.GetAllPlayers()
                    .Where(p => PlayerHelper.IsUsable(p) && (!p!.IsFakeClient || _config.Settings.ApplyToBots))
                    .ToList();

                if (players.Count == 0)
                {
                    b.Info("Aktif oyuncu bulunamadı.");
                    return;
                }

                foreach (var target in players)
                {
                    var sid = PlayerHelper.GetSteamId64(target);
                    var name = PlayerHelper.GetName(target);
                    var resolved = _manager.Resolve(target);
                    var tagVis = GetColorVisuals(resolved.ChatTagColor);
                    var label = $"{tagVis.Emoji} {name} [{resolved.Label}]";

                    b.Button(label, ctx =>
                    {
                        if (sid == 0)
                        {
                            _hud.Toast(ctx.Player, new ToastOptions
                            {
                                Title = "Hata",
                                Text = "Bu oyuncunun SteamID doğrulaması henüz tamamlanmadı.",
                                Kind = ToastKind.Error
                            });
                            return;
                        }
                        OpenManagePlayerMenu(ctx.Player, sid, name);
                    });
                }
            })
            .Button("⬅️ Ana Menüye Dön", ctx => _mainMenu?.Open(ctx.Player))
            .Build();
    }

    private IMenu BuildCustomPlayersMenu()
    {
        return _hud.Menu("tagsystem.players.custom", "⭐ Özel Oyuncu Etiketleri")
            .Description(p => $"{_config.Players.Count} kayıtlı özel oyuncu")
            .Items((admin, b) =>
            {
                if (_config.Players.Count == 0)
                {
                    b.Info("Kayıtlı özel oyuncu bulunamadı.");
                    return;
                }

                foreach (var p in _config.Players.ToList())
                {
                    var entry = p;
                    var tagVis = GetColorVisuals(entry.ChatTagColor ?? "gold");
                    var label = !string.IsNullOrWhiteSpace(entry.DisplayName)
                        ? $"{tagVis.Emoji} {entry.DisplayName} ({entry.ChatTag ?? "-"})"
                        : $"{tagVis.Emoji} {SteamIdHelper.ToSteam2(entry.SteamId64)} ({entry.ChatTag ?? "-"})";

                    b.Button(label, ctx =>
                    {
                        OpenEntryEditMenu(ctx.Player, entry, $"Özel: {entry.DisplayName ?? entry.Id}",
                            onDelete: () =>
                            {
                                _config.Players.Remove(entry);
                                SaveAndApply();
                            },
                            onBack: adm => _customPlayersMenu?.Open(adm));
                    });
                }
            })
            .Button("⬅️ Ana Menüye Dön", ctx => _mainMenu?.Open(ctx.Player))
            .Build();
    }

    private IMenu BuildGroupsMenu()
    {
        return _hud.Menu("tagsystem.groups", "🛡️ Yetki Grupları")
            .Description("Öncelik sırasına göre listelenir")
            .Button("➕ Yeni Yetki Grubu Oluştur", ctx => StartCreateGroupFlow(ctx.Player))
            .Items((admin, b) =>
            {
                foreach (var g in _config.Groups.OrderByDescending(x => x.Priority).ToList())
                {
                    var group = g;
                    var status = group.Enabled ? "✓" : "✗";
                    var tagVis = GetColorVisuals(group.ChatTagColor ?? "gold");
                    var label = $"[{status}] {tagVis.Emoji} {group.Id} ({group.ChatTag ?? "-"}) [P:{group.Priority}]";

                    b.Button(label, ctx =>
                    {
                        OpenEntryEditMenu(ctx.Player, group, $"Grup: {group.Id}",
                            onDelete: () =>
                            {
                                _config.Groups.Remove(group);
                                SaveAndApply();
                            },
                            onBack: adm => _groupsMenu?.Open(adm));
                    });
                }
            })
            .Button("⬅️ Ana Menüye Dön", ctx => _mainMenu?.Open(ctx.Player))
            .Build();
    }

    private IMenu BuildSettingsMenu()
    {
        return _hud.Menu("tagsystem.settings", "🔧 Genel Ayarlar")
            .Description("Değişiklikler anında sunucuya uygulanır")
            .Toggle("Scoreboard (Clan Tag)",
                _ => _config.Settings.EnableScoreboardTag,
                (ctx, v) =>
                {
                    _config.Settings.EnableScoreboardTag = v;
                    SaveAndApply();
                    if (!v) _service.RestoreScoreboardAll();
                    ctx.Refresh();
                })
            .Toggle("Chat Formatlama",
                _ => _config.Settings.EnableChatTag,
                (ctx, v) =>
                {
                    _config.Settings.EnableChatTag = v;
                    SaveAndApply();
                    ctx.Refresh();
                })
            .Toggle("Yetki Sistemi (use_permissions)",
                _ => _config.Settings.UsePermissions,
                (ctx, v) =>
                {
                    _config.Settings.UsePermissions = v;
                    SaveAndApply();
                    ctx.Refresh();
                })
            .Toggle("Ölüler Sadece Ölülere Yazsın",
                _ => _config.Chat.DeadTalkOnlyToDead,
                (ctx, v) =>
                {
                    _config.Chat.DeadTalkOnlyToDead = v;
                    SaveAndApply();
                    ctx.Refresh();
                })
            .Toggle("Botlara Etiket Uygula",
                _ => _config.Settings.ApplyToBots,
                (ctx, v) =>
                {
                    _config.Settings.ApplyToBots = v;
                    SaveAndApply();
                    ctx.Refresh();
                })
            .Toggle("✨ Canlı Renkli CenterHTML Önizlemesi",
                p => !_centerHtmlDisabled.Contains(p.Slot),
                (ctx, v) =>
                {
                    if (v)
                    {
                        _centerHtmlDisabled.Remove(ctx.Player.Slot);
                        RenderCenterHtmlPreview(ctx.Player, null);
                    }
                    else
                    {
                        _centerHtmlDisabled.Add(ctx.Player.Slot);
                        ctx.Player.SendCenterHTML(" ", 1);
                    }
                    ctx.Refresh();
                })
            .Button("⬅️ Ana Menüye Dön", ctx => _mainMenu?.Open(ctx.Player))
            .Build();
    }

    // ================================= DYNAMIC MENUS =================================

    public void OpenManagePlayerMenu(IPlayer admin, ulong targetSid, string targetName)
    {
        var steam2 = SteamIdHelper.ToSteam2(targetSid);
        var customEntry = _config.Players.FirstOrDefault(p => p.SteamId64 == targetSid);
        var targetPlayer = _core.PlayerManager.GetAllPlayers().FirstOrDefault(p => PlayerHelper.IsUsable(p) && PlayerHelper.GetSteamId64(p) == targetSid);

        _centerHtmlActive[admin.Slot] = customEntry;
        RenderCenterHtmlPreview(admin, customEntry);

        var builder = _hud.Menu($"tagsystem.player.{targetSid}", $"👤 {targetName}")
            .Description(p =>
            {
                var res = targetPlayer != null ? _manager.Resolve(targetPlayer) : (customEntry != null ? _manager.BuildResolvedTag(customEntry) : _manager.BuildResolvedTag(_config.Default));
                return $"{steam2} | Aktif: {res.Label}";
            })
            .Button("💬 Bu Oyuncu Adına Test Mesajı At", ctx =>
            {
                _service.SendTestChat(ctx.Player, customEntry, $"{targetName} adına test chat satırı!");
                _hud.Toast(ctx.Player, new ToastOptions
                {
                    Title = "Test Mesajı",
                    Text = $"{targetName} etiket önizlemesi sohbete gönderildi.",
                    Kind = ToastKind.Info,
                    Key = "tagsystem.testchat"
                });
            });

        if (customEntry != null)
        {
            builder.Button("✏️ Özel Etiketini Düzenle", ctx =>
            {
                OpenEntryEditMenu(ctx.Player, customEntry, $"Özel: {targetName}",
                    onDelete: () =>
                    {
                        _config.Players.Remove(customEntry);
                        SaveAndApply();
                    },
                    onBack: adm => OpenManagePlayerMenu(adm, targetSid, targetName));
            });

            builder.Button("🗑️ Özel Etiketini Kaldır", ctx =>
            {
                _hud.Dialog(ctx.Player, "Özel Etiket Silinsin mi?", $"{targetName} oyuncusunun kişisel özel etiketi silinecek.")
                    .Button("Evet, Sil", c =>
                    {
                        _config.Players.RemoveAll(x => x.SteamId64 == targetSid);
                        SaveAndApply();
                        _hud.Toast(c.Player, new ToastOptions { Title = "Silindi", Text = $"{targetName} özel etiketi silindi.", Kind = ToastKind.Warning });
                        c.Close();
                        OpenManagePlayerMenu(c.Player, targetSid, targetName);
                    }, ButtonStyle.Danger)
                    .Button("Vazgeç", c =>
                    {
                        c.Close();
                        OpenManagePlayerMenu(c.Player, targetSid, targetName);
                    })
                    .Open();
            });
        }
        else
        {
            builder.Button("✨ Özel Kişisel Etiket Ver", ctx =>
            {
                PromptInput(ctx.Player, $"{targetName} için yeni özel etiket metnini yazın (örn: [KRAL]):", (adm, text) =>
                {
                    var pData = new PlayerData
                    {
                        Id = $"player:{targetSid}",
                        SteamId64 = targetSid,
                        RawSteamId = steam2,
                        DisplayName = targetName,
                        ChatTag = text,
                        ScoreboardTag = TagService.StripOuterBrackets(text),
                        ChatTagColor = "gold",
                        NameColor = "gold",
                        Priority = 1000,
                        Enabled = true
                    };
                    _config.Players.Add(pData);
                    SaveAndApply();
                    _hud.Toast(adm, new ToastOptions { Title = "Başarılı", Text = $"{targetName} için [gold]{text}[/] etiketi tanımlandı!", Kind = ToastKind.Success });
                    OpenEntryEditMenu(adm, pData, $"Özel: {targetName}",
                        onDelete: () => { _config.Players.Remove(pData); SaveAndApply(); },
                        onBack: a => OpenManagePlayerMenu(a, targetSid, targetName));
                }, adm => OpenManagePlayerMenu(adm, targetSid, targetName));
            });
        }

        // Grup üyelikleri toggle listesi
        foreach (var g in _config.Groups)
        {
            var grp = g;
            var tagVis = GetColorVisuals(grp.ChatTagColor ?? "gold");
            builder.Toggle($"Grup: {tagVis.Emoji} {grp.DisplayName ?? grp.Id} ({grp.ChatTag ?? "-"})",
                _ => grp.Members.Contains(targetSid),
                (ctx, v) =>
                {
                    if (v)
                    {
                        grp.Members.Add(targetSid);
                        _hud.Toast(ctx.Player, new ToastOptions { Title = "Grup", Text = $"{targetName}, {grp.Id} grubuna eklendi.", Kind = ToastKind.Success, Key = $"tag.grp.{grp.Id}" });
                    }
                    else
                    {
                        grp.Members.Remove(targetSid);
                        _hud.Toast(ctx.Player, new ToastOptions { Title = "Grup", Text = $"{targetName}, {grp.Id} grubundan çıkarıldı.", Kind = ToastKind.Warning, Key = $"tag.grp.{grp.Id}" });
                    }
                    SaveAndApply();
                    ctx.Refresh();
                    RenderCenterHtmlPreview(ctx.Player, customEntry);
                });
        }

        builder.Button("⬅️ Oyuncu Listesine Dön", ctx => _onlinePlayersMenu?.Open(ctx.Player));

        builder.Build().Open(admin);
    }

    public void OpenEntryEditMenu(IPlayer admin, TagEntry entry, string title, Action? onDelete = null, Action<IPlayer>? onBack = null)
    {
        _centerHtmlActive[admin.Slot] = entry;
        RenderCenterHtmlPreview(admin, entry);

        var menuId = $"tagsystem.entry.{entry.Id}";
        var builder = _hud.Menu(menuId, title)
            .Description(_ =>
            {
                var cleanChat = _service.BuildPreviewChatLine(null, entry, "Merhaba Dünya!", true);
                return $"Chat: {cleanChat}";
            })
            .Button("💬 Sohbete Canlı Test Mesajı At", ctx =>
            {
                _service.SendTestChat(ctx.Player, entry, "TagSystem etiket önizleme test mesajı!");
                _hud.Toast(ctx.Player, new ToastOptions
                {
                    Title = "Test Mesajı",
                    Text = "Renkli test satırı sohbete gönderildi.",
                    Kind = ToastKind.Success,
                    Key = "tagsystem.testchat"
                });
            })
            .Toggle("Durum (Aktif/Kapalı)",
                _ => entry.Enabled,
                (ctx, v) =>
                {
                    entry.Enabled = v;
                    SaveAndApply();
                    ctx.Refresh();
                    RenderCenterHtmlPreview(ctx.Player, entry);
                })
            .Button($"💬 Chat Etiketi: {entry.ChatTag ?? "(boş)"}", ctx =>
            {
                PromptInput(ctx.Player, "Yeni Chat Etiketini sohbete yazın (örn: [VIP]):", (adm, val) =>
                {
                    entry.ChatTag = val;
                    SaveAndApply();
                    RenderCenterHtmlPreview(adm, entry);
                }, adm => OpenEntryEditMenu(adm, entry, title, onDelete, onBack));
            })
            .Button($"📋 Clan Tag: {(string.IsNullOrEmpty(entry.ScoreboardTag) ? "(boş)" : entry.ScoreboardTag)}", ctx =>
            {
                PromptInput(ctx.Player, "Yeni Scoreboard / Clan Tag'ini yazın (boş bırakmak için - yazın):", (adm, val) =>
                {
                    entry.ScoreboardTag = val == "-" ? "" : TagService.StripOuterBrackets(val);
                    SaveAndApply();
                    RenderCenterHtmlPreview(adm, entry);
                }, adm => OpenEntryEditMenu(adm, entry, title, onDelete, onBack));
            })
            .Select("👑 Çerçeve / Sembol", FrameDisplayOptions,
                _ => GetFrameIndex(entry.Frame),
                (ctx, i) =>
                {
                    entry.Frame = FrameKeys[i] == "none" ? null : FrameKeys[i];
                    SaveAndApply();
                    ctx.Refresh();
                    RenderCenterHtmlPreview(ctx.Player, entry);
                })
            // Renk Seçiciler - Canlı Emojili ve Türkçe İsimli
            .Select("🎨 Etiket Rengi", ColorDisplayOptions,
                _ => GetColorIndex(entry.ChatTagColor ?? "gold"),
                (ctx, i) =>
                {
                    entry.ChatTagColor = ColorKeys[i];
                    SaveAndApply();
                    ctx.Refresh();
                    RenderCenterHtmlPreview(ctx.Player, entry);
                })
            .Select("🎨 İsim Rengi", ColorDisplayOptions,
                _ => GetColorIndex(entry.NameColor ?? "teamcolor"),
                (ctx, i) =>
                {
                    entry.NameColor = ColorKeys[i];
                    SaveAndApply();
                    ctx.Refresh();
                    RenderCenterHtmlPreview(ctx.Player, entry);
                })
            .Select("🎨 Mesaj Rengi", ColorDisplayOptions,
                _ => GetColorIndex(entry.MessageColor ?? "default"),
                (ctx, i) =>
                {
                    entry.MessageColor = ColorKeys[i];
                    SaveAndApply();
                    ctx.Refresh();
                    RenderCenterHtmlPreview(ctx.Player, entry);
                })
            .Select("🔢 Öncelik (Priority)", PriorityLabels,
                _ => GetPriorityIndex(entry.Priority),
                (ctx, i) =>
                {
                    entry.Priority = PriorityValues[i];
                    SaveAndApply();
                    ctx.Refresh();
                })
            .Button($"🔢 Özel Öncelik Sayısı Gir ({entry.Priority})", ctx =>
            {
                PromptInput(ctx.Player, "Öncelik sayısını yazın (örn: 150):", (adm, val) =>
                {
                    if (int.TryParse(val, out var p))
                    {
                        entry.Priority = p;
                        SaveAndApply();
                    }
                }, adm => OpenEntryEditMenu(adm, entry, title, onDelete, onBack));
            });

        if (entry != _config.Default)
        {
            builder.Select("🚩 Takım Kısıtlaması", TeamPresetLabels,
                _ => GetTeamPresetIndex(entry.Teams),
                (ctx, i) =>
                {
                    entry.Teams = TeamPresets[i].ToList();
                    SaveAndApply();
                    ctx.Refresh();
                });
        }

        if (entry is GroupData g)
        {
            var perms = g.Permissions.Count > 0 ? string.Join(", ", g.Permissions) : "(yok)";
            builder.Button($"🔑 Yetkiler: {perms}", ctx =>
            {
                PromptInput(ctx.Player, "Yetki anahtarını yazın (birden fazlaysa virgülle ayırın):", (adm, val) =>
                {
                    g.Permissions = val.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
                    SaveAndApply();
                }, adm => OpenEntryEditMenu(adm, entry, title, onDelete, onBack));
            });
        }

        if (onDelete != null)
        {
            builder.Button("🗑️ Bu Kaydı Sil", ctx =>
            {
                _hud.Dialog(ctx.Player, $"'{entry.Id}' Kaydı Silinsin mi?", "Bu işlem geri alınamaz.")
                    .Button("Evet, Sil", c =>
                    {
                        onDelete();
                        _hud.Toast(c.Player, new ToastOptions { Title = "Silindi", Text = $"{entry.Id} kaydı silindi.", Kind = ToastKind.Warning });
                        c.Close();
                        if (onBack != null) onBack(c.Player);
                        else _mainMenu?.Open(c.Player);
                    }, ButtonStyle.Danger)
                    .Button("Vazgeç", c =>
                    {
                        c.Close();
                        OpenEntryEditMenu(c.Player, entry, title, onDelete, onBack);
                    })
                    .Open();
            });
        }

        builder.Button("⬅️ Geri Dön", ctx =>
        {
            if (onBack != null) onBack(ctx.Player);
            else _mainMenu?.Open(ctx.Player);
        });

        builder.Build().Open(admin);
    }

    private void StartCreateGroupFlow(IPlayer admin)
    {
        PromptInput(admin, "Yeni grubun ID'sini yazın (örn: mod, rehber, vip):", (adm, rawId) =>
        {
            var id = rawId.Trim().ToLowerInvariant().Replace(' ', '_');
            if (string.IsNullOrWhiteSpace(id) || _config.Groups.Any(g => g.Id.Equals(id, StringComparison.OrdinalIgnoreCase)))
            {
                _hud.Toast(adm, new ToastOptions
                {
                    Title = "Hata",
                    Text = $"'{id}' ID'si geçersiz veya zaten mevcut.",
                    Kind = ToastKind.Error
                });
                return;
            }

            var tagText = $"[{id.ToUpperInvariant()}]";
            var group = new GroupData
            {
                Id = id,
                DisplayName = id.ToUpperInvariant(),
                Permissions = new List<string> { id, $"tagsystem.{id}" },
                ChatTag = tagText,
                ScoreboardTag = id.ToUpperInvariant(),
                ChatTagColor = "gold",
                NameColor = "gold",
                Priority = 50,
                Enabled = true
            };
            _config.Groups.Add(group);
            SaveAndApply();

            _hud.Toast(adm, new ToastOptions
            {
                Title = "Grup Oluşturuldu",
                Text = $"'{id}' grubu oluşturuldu! Şimdi ayarlarını düzenleyebilirsiniz.",
                Kind = ToastKind.Success
            });

            OpenEntryEditMenu(adm, group, $"Grup: {group.Id}",
                onDelete: () => { _config.Groups.Remove(group); SaveAndApply(); },
                onBack: a => _groupsMenu?.Open(a));
        }, adm => _mainMenu?.Open(adm));
    }

    // ================================= PREVIEW RENDERING =================================

    public static ColorInfo GetColorVisuals(string? colorName)
    {
        if (string.IsNullOrWhiteSpace(colorName)) return ColorPalette[^1];
        var match = ColorPalette.FirstOrDefault(c => c.Key.Equals(colorName, StringComparison.OrdinalIgnoreCase));
        return match ?? ColorPalette[^1];
    }

    public void RenderCenterHtmlPreview(IPlayer admin, TagEntry? entry)
    {
        if (_centerHtmlDisabled.Contains(admin.Slot)) return;

        try
        {
            var res = entry != null ? _manager.BuildResolvedTag(entry) : _manager.Resolve(admin);
            var tagCol = GetColorVisuals(res.ChatTagColor);
            var nameCol = GetColorVisuals(res.NameColor);
            var msgCol = GetColorVisuals(res.MessageColor);

            var tagText = string.IsNullOrEmpty(res.ChatTag) ? "" : res.ChatTag;
            if (!string.IsNullOrEmpty(tagText) && !string.IsNullOrWhiteSpace(res.Frame) && PlaceholderHelper.IsFrame(res.Frame))
            {
                tagText = PlaceholderHelper.ApplyFrame(tagText, res.Frame);
            }
            var name = PlayerHelper.GetName(admin);
            var clan = string.IsNullOrEmpty(res.ScoreboardTag) ? "-" : res.ScoreboardTag;

            var sb = new StringBuilder();
            sb.Append("<font class='fontSize-xs' color='#94A3B8'>✦ <b>TagSystem Canlı Renk Önizlemesi</b> ✦</font><br>");
            sb.Append("<font class='fontSize-m'>");
            if (!string.IsNullOrEmpty(tagText))
            {
                sb.Append($"<font color='{tagCol.Hex}'><b>{WebUtility.HtmlEncode(tagText)}</b></font> ");
            }
            sb.Append($"<font color='{nameCol.Hex}'><b>{WebUtility.HtmlEncode(name)}</b></font>");
            sb.Append("<font color='#94A3B8'>: </font>");
            sb.Append($"<font color='{msgCol.Hex}'>Merhaba Dünya!</font>");
            sb.Append("</font><br>");
            sb.Append($"<font class='fontSize-xs' color='#94A3B8'>Clan Tag: </font><font class='fontSize-xs' color='#38BDF8'><b>[{WebUtility.HtmlEncode(clan)}]</b></font>");

            admin.SendCenterHTML(sb.ToString(), 2000);
        }
        catch (Exception ex)
        {
            _core.Logger.LogWarning(ex, "[TagSystem] CenterHTML önizleme basılamadı.");
        }
    }

    // ================================= TEXT INPUT VIA CHAT =================================

    public void PromptInput(IPlayer admin, string prompt, Action<IPlayer, string> callback, Action<IPlayer>? reopen)
    {
        _pendingInput[admin.Slot] = (prompt, callback, reopen);
        _api.CloseModal(admin);
        _centerHtmlActive.Remove(admin.Slot);
        admin.SendCenterHTML(" ", 1);

        _hud.Banner(admin, new BannerOptions
        {
            Title = "Metin Girişi Bekleniyor",
            Subtitle = $"{prompt} (İptal etmek için 'iptal' yazın)",
            Kind = BannerKind.Info,
            Priority = HudPriority.High,
            Duration = TimeSpan.FromSeconds(25)
        });

        _hud.Toast(admin, new ToastOptions
        {
            Title = "Sohbete Yazın",
            Text = prompt,
            Kind = ToastKind.Info,
            Key = "tagsystem.input"
        });

        _service.Reply(admin, $"[gold]{prompt}[default] (İptal için sohbete [red]iptal[default] yazın)");
    }

    public bool TryConsumeChatInput(int playerId, string text)
    {
        if (!_pendingInput.TryGetValue(playerId, out var pending)) return false;

        var admin = _core.PlayerManager.GetPlayer(playerId);
        _pendingInput.Remove(playerId);

        if (!PlayerHelper.IsUsable(admin)) return true;

        var trimmed = (text ?? string.Empty).Trim();
        if (trimmed.Length >= 2 && trimmed[0] == '"' && trimmed[^1] == '"')
            trimmed = trimmed[1..^1].Trim();

        if (trimmed.Equals("iptal", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Equals("!iptal", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Equals("cancel", StringComparison.OrdinalIgnoreCase))
        {
            _hud.Toast(admin!, new ToastOptions
            {
                Title = "İptal Edildi",
                Text = "Giriş işlemi iptal edildi.",
                Kind = ToastKind.Warning,
                Key = "tagsystem.input"
            });
            _service.Reply(admin, "[red]Giriş iptal edildi.[default]");
            pending.Reopen?.Invoke(admin!);
            return true;
        }

        try
        {
            pending.Callback(admin!, trimmed);
            _hud.Toast(admin!, new ToastOptions
            {
                Title = "Başarılı",
                Text = "Değer güncellendi ve kaydedildi!",
                Kind = ToastKind.Success,
                Key = "tagsystem.input"
            });
        }
        catch (Exception ex)
        {
            _core.Logger.LogError(ex, "[TagSystem] Input callback hatası.");
            _hud.Toast(admin!, new ToastOptions
            {
                Title = "Hata",
                Text = "Değer kaydedilirken bir hata oluştu.",
                Kind = ToastKind.Error,
                Key = "tagsystem.input"
            });
        }

        pending.Reopen?.Invoke(admin!);
        return true;
    }

    // ================================= HELPERS =================================

    private static int GetColorIndex(string color)
    {
        int idx = Array.FindIndex(ColorKeys, c => c.Equals(color, StringComparison.OrdinalIgnoreCase));
        return idx < 0 ? 0 : idx;
    }

    private static int GetFrameIndex(string? frame)
    {
        if (string.IsNullOrWhiteSpace(frame)) return 0;
        int idx = Array.FindIndex(FrameKeys, k => k.Equals(frame, StringComparison.OrdinalIgnoreCase));
        return idx < 0 ? 0 : idx;
    }

    private static int GetPriorityIndex(int priority)
    {
        int idx = Array.IndexOf(PriorityValues, priority);
        if (idx >= 0) return idx;
        int bestIdx = 0;
        int minDiff = int.MaxValue;
        for (int i = 0; i < PriorityValues.Length; i++)
        {
            int diff = Math.Abs(PriorityValues[i] - priority);
            if (diff < minDiff)
            {
                minDiff = diff;
                bestIdx = i;
            }
        }
        return bestIdx;
    }

    private static int GetTeamPresetIndex(List<string> teams)
    {
        if (teams.Count == 1)
        {
            if (teams[0].Equals("T", StringComparison.OrdinalIgnoreCase)) return 1;
            if (teams[0].Equals("CT", StringComparison.OrdinalIgnoreCase)) return 2;
            if (teams[0].Equals("SPEC", StringComparison.OrdinalIgnoreCase)) return 3;
        }
        return 0;
    }
}

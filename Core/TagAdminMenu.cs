using System.Globalization;
using Microsoft.Extensions.Logging;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Menu;
using SwiftlyS2.Shared.Menu.Components;
using SwiftlyS2.Shared.Players;
using TagSystem.Config;
using TagSystem.Models;
using TagSystem.Utils;

namespace TagSystem.Core;

/// <summary>
/// Adminler icin SwiftlyS2 Core.Menu tabanli etiket ve grup yonetim menusu (!tagmenu).
/// CustomHUD, CenterHTML ve Chat rendererlariyla tam uyumludur.
/// </summary>
public sealed class TagAdminMenu
{
    private const string MenuTag = "tagsystem.menu";

    public sealed record ColorOption(string Key, string TurkishName, string SwatchToken);
    private sealed record FrameOption(string Key, string Name);

    public static readonly ColorOption[] ColorPalette =
    [
        new("gold", "Altın (Gold)", "yellow"),
        new("lightred", "Açık Kırmızı", "red"),
        new("red", "Kırmızı", "red"),
        new("darkred", "Koyu Kırmızı", "red"),
        new("lightblue", "Açık Mavi", "cyan"),
        new("blue", "Mavi", "blue"),
        new("darkblue", "Koyu Mavi", "blue"),
        new("lime", "Açık Yeşil (Lime)", "green"),
        new("green", "Yeşil", "green"),
        new("olive", "Zeytin Yeşili", "green"),
        new("yellow", "Sarı", "yellow"),
        new("lightyellow", "Açık Sarı", "yellow"),
        new("orange", "Turuncu", "orange"),
        new("purple", "Mor", "purple"),
        new("lightpurple", "Açık Mor", "purple"),
        new("magenta", "Magenta", "pink"),
        new("grey", "Gri", "white"),
        new("silver", "Gümüş", "white"),
        new("white", "Beyaz", "white"),
        new("bluegrey", "Mavi Gri", "cyan"),
        new("teamcolor", "Takım Rengi", "team"),
        // Özel Çok Renkli ve Efekt Renkleri
        new("rainbow", "Gökkuşağı (Rainbow)", "random"),
        new("fire", "Ateş (Fire)", "orange"),
        new("ice", "Buz (Ice)", "cyan"),
        new("neon", "Neon", "green"),
        new("sunset", "Günbatımı (Sunset)", "orange"),
        new("toxic", "Toksik (Toxic)", "green"),
        new("police", "Polis (Police)", "blue"),
        new("blood", "Kan (Blood)", "red"),
        new("royal", "Kraliyet (Royal)", "yellow"),
        new("matrix", "Matrix", "green"),
        new("pastel", "Pastel", "pink"),
        // Yeni Tematik Gradyanlar
        new("galaxy", "Galaksi (Galaxy)", "purple"),
        new("cyberpunk", "Siberpunk", "random"),
        new("lava", "Lav (Lava)", "orange"),
        new("aurora", "Kuzey Işıkları", "random"),
        new("ghost", "Hayalet (Ghost)", "white"),
        new("nature", "Doğa (Nature)", "green"),
        new("candy", "Pamuk Şeker", "pink"),
        new("goldrush", "Altın Avcısı", "yellow"),
        // Çift Renkli (Dama) Desenler
        new("redblue", "Dama: Kırmızı-Mavi", "red"),
        new("pinkblue", "Dama: Pembe-Mavi", "pink"),
        new("goldblack", "Dama: Altın-Gri", "yellow"),
        new("greenyellow", "Dama: Yeşil-Sarı", "green"),
        new("blackwhite", "Dama: Siyah-Beyaz", "black"),
        new("redwhite", "Dama: Kırmızı-Beyaz", "red"),
        new("default", "Varsayılan", "")
    ];

    private static readonly FrameOption[] FrameOptions = PlaceholderHelper.AvailableFrames
        .Select(f => new FrameOption(f.Key, f.Name))
        .ToArray();

    private sealed class SwatchColorSelector : SelectorComponent<ColorOption>
    {
        public SwatchColorSelector(
            string text,
            IReadOnlyList<ColorOption> choices,
            int defaultIndex,
            Action<MenuValueChangedContext<ColorOption>> onItemChanged,
            string? id = null)
            : base(text, choices, defaultIndex, onItemChanged, id)
        {
            Formatter = item => item.TurkishName;
        }

        public override MenuNode Render(IMenuComponentRenderContext context)
        {
            var node = base.Render(context);
            if (Choices.Count == 0)
                return node;

            int idx = Math.Clamp(GetValue(context.Session), 0, Choices.Count - 1);
            string token = Choices[idx].SwatchToken;
            if (string.IsNullOrEmpty(token))
                return node;

            var raw = new MenuRawNode("customhud.panorama", $"glyph=colors;swatch={token}");
            return node is MenuLineNode line
                ? line with { Children = [.. line.Children, raw] }
                : MenuLineNode.Of(node, raw);
        }
    }

    private sealed class DangerSubmenuComponent : SubmenuComponent
    {
        public DangerSubmenuComponent(string text, Func<IMenuSession, IMenu?> factory, string? id = null)
            : base(text, factory, id)
        {
        }

        public override MenuNode Render(IMenuComponentRenderContext context)
        {
            var node = base.Render(context);
            var raw = new MenuRawNode("customhud.panorama", "glyph=cancel;danger;verb=open");
            return node is MenuLineNode line
                ? line with { Children = [.. line.Children, raw] }
                : MenuLineNode.Of(node, raw);
        }
    }

    private sealed class PageState
    {
        public int? Focus { get; set; }
        public object? FocusKey { get; set; }
        public int RowsAt { get; set; }
        public List<IMenuComponent> Rows { get; } = [];
    }

    private readonly ISwiftlyCore _core;
    private readonly ConfigLoader _loader;
    private readonly TagManager _manager;
    private readonly TagService _service;
    private PluginConfig _config;

    private readonly Dictionary<int, List<IMenu>> _ownedMenus = new();
    private readonly Dictionary<int, long> _lastToggleTick = new();

    public TagAdminMenu(ISwiftlyCore core, ConfigLoader loader, PluginConfig config, TagManager manager, TagService service)
    {
        _core = core;
        _loader = loader;
        _config = config;
        _manager = manager;
        _service = service;
    }

    public void UpdateConfig(PluginConfig config) => _config = config;

    public bool IsMenuOpen(int slot)
    {
        var player = _core.PlayerManager.GetPlayer(slot);
        if (!PlayerHelper.IsUsable(player)) return false;

        return _core.Menu.GetSession(player!) is { IsOpen: true, Menu.Tag: string tag } && tag == MenuTag;
    }

    public void Toggle(IPlayer admin)
    {
        if (!PlayerHelper.IsUsable(admin))
            return;

        long now = Environment.TickCount64;
        if (_lastToggleTick.TryGetValue(admin.Slot, out long prev) && now - prev < 150)
            return;
        _lastToggleTick[admin.Slot] = now;

        if (_core.Menu.GetSession(admin) is { IsOpen: true, Menu.Tag: string tag } && tag == MenuTag)
        {
            Close(admin);
            return;
        }

        Open(admin);
    }

    public void Open(IPlayer admin)
    {
        if (!PlayerHelper.IsUsable(admin))
            return;

        Close(admin);
        var menu = BuildMainMenu(admin.Slot);
        menu.Open(admin);
    }

    public void Close(IPlayer admin)
    {
        if (!PlayerHelper.IsUsable(admin))
            return;

        if (_core.Menu.GetSession(admin) is { IsOpen: true, Menu.Tag: string tag } && tag == MenuTag)
        {
            _core.Menu.CloseFor(admin);
        }

        DisposeSlotMenus(admin.Slot);
    }

    public void OnPlayerDisconnected(int slot)
    {
        _lastToggleTick.Remove(slot);
        DisposeSlotMenus(slot);
    }

    public void CloseAll()
    {
        try { _core.Menu.CloseAll(); } catch { }

        foreach (int slot in _ownedMenus.Keys.ToList())
        {
            DisposeSlotMenus(slot);
        }
        _ownedMenus.Clear();
    }

    private IMenu Track(int slot, IMenu menu)
    {
        menu.Tag = MenuTag;
        if (!_ownedMenus.TryGetValue(slot, out var list))
        {
            list = new List<IMenu>();
            _ownedMenus[slot] = list;
        }
        list.Add(menu);
        return menu;
    }

    private void DisposeSlotMenus(int slot)
    {
        if (!_ownedMenus.TryGetValue(slot, out var list))
            return;

        foreach (var menu in list)
        {
            try { menu.Dispose(); } catch { }
        }
        list.Clear();
        _ownedMenus.Remove(slot);
    }

    // ============================== 1. ANA MENÜ ==============================

    private IMenu BuildMainMenu(int ownerSlot)
    {
        IMenu? mainMenu = null;
        var playersMenu = BuildOnlinePlayersMenu(ownerSlot);
        var customPlayersMenu = BuildCustomPlayersMenu(ownerSlot);
        var groupsMenu = BuildGroupsListMenu(ownerSlot);
        var settingsMenu = BuildSettingsMenu(ownerSlot);

        var createGroupInput = CreateNewGroupInput(ownerSlot, () =>
        {
            groupsMenu.Parent ??= mainMenu;
            return groupsMenu;
        });

        mainMenu = _core.Menu.CreateMenu("tagsystem.main")
            .WithTitle("TAGSYSTEM YÖNETİM")
            .AddHeader(new HudTextComponent
            {
                TextProvider = _ => $"{_config.Groups.Count}\tGrup",
                HudDirective = "chip=gold;glyph=shield"
            })
            .AddHeader(new HudTextComponent
            {
                TextProvider = _ => $"{_config.Players.Count}\tÖzel Oyuncu",
                HudDirective = "chip=info;glyph=crown"
            })
            .AddHeader(new HudTextComponent
            {
                TextProvider = session =>
                {
                    var resolved = _manager.Resolve(session.Player);
                    return $"{SafeMenuText(resolved.Label, 16, "-")}\tAktif Etiketin";
                },
                HudDirective = "chip=good;glyph=tag"
            })
            .AddHeader(new HudTextComponent("Yönetmek istediğiniz bölümü seçin — değişiklikler anında uygulanır")
            {
                HudDirective = "hint"
            })
            .Add(new HudSubmenuComponent("Oyuncuya Etiket / Grup Ver", playersMenu, "players")
            {
                TextProvider = _ =>
                {
                    int onlineCount = _core.PlayerManager.GetAllPlayers()
                        .Count(p => PlayerHelper.IsUsable(p) && (!p!.IsFakeClient || _config.Settings.ApplyToBots));
                    return $"Oyuncuya Etiket / Grup Ver\t{onlineCount} aktif";
                },
                HudDirective = "glyph=teamplayer;sub=Sunucudaki oyunculara özel etiket veya grup atayın;verb=open"
            })
            .Add(new HudSubmenuComponent("Özel Oyuncu Etiketleri", customPlayersMenu, "custom_players")
            {
                TextProvider = _ => $"Özel Oyuncu Etiketleri\t{_config.Players.Count}",
                HudDirective = "glyph=crown;sub=SteamID bazlı kayıtlı kişisel etiketleri düzenleyin;verb=open"
            })
            .Add(new HudSubmenuComponent("Yetki Gruplarını Yönet", groupsMenu, "groups")
            {
                TextProvider = _ => $"Yetki Gruplarını Yönet\t{_config.Groups.Count}",
                HudDirective = "glyph=shield;sub=VIP, Admin ve özel yetki gruplarını düzenleyin;verb=open"
            })
            .Add(createGroupInput)
            .Add(new HudSubmenuComponent("Varsayılan [default] Etiket",
                _ => BuildEntryEditMenu(ownerSlot, _config.Default, "Varsayılan [default]", null, mainMenu), "default_tag")
            {
                TextProvider = _ => $"Varsayılan [default] Etiket\t{SafeMenuText(_config.Default.ChatTag, 16, "-")}",
                HudDirective = "glyph=tag;sub=Grubu olmayan tüm oyunculara uygulanan temel etiket;verb=edit"
            })
            .Add(new HudSubmenuComponent("Genel Ayarlar", settingsMenu, "settings")
            {
                HudDirective = "glyph=legend;sub=Scoreboard, chat, yetki ve bot ayarlarını yönetin;verb=open"
            })
            .Add(new HudButtonComponent("Sohbete Canlı Test Mesajı At", ctx =>
            {
                _service.SendTestChat(ctx.Player, null, "TagSystem canlı renkli chat önizlemesi!");
                _service.Reply(ctx.Player, "Renkli test sohbet satırı chat kutunuza gönderildi.");
                return ValueTask.CompletedTask;
            })
            {
                HudDirective = "glyph=chat;primary;sub=Kendi aktif etiketinizle sohbete test satırı gönderir;verb=select"
            })
            .Add(new HudButtonComponent("Skorboard Taglarını Yenile", ctx =>
            {
                _service.EnsureClanTagsEnabled();
                _service.ApplyScoreboardAll(force: true);
                _service.Reply(ctx.Player, "[lime]Tüm oyuncuların skorboard (clan) tagları yeniden senkronize edildi.[default]");
                return ValueTask.CompletedTask;
            })
            {
                HudDirective = "glyph=refresh;sub=Tüm aktif oyuncuların TAB clan taglarını zorla günceller;verb=apply"
            })
            .Build();

        return Track(ownerSlot, mainMenu);
    }

    // ============================== 2. AKTİF OYUNCULAR ==============================

    private IMenu BuildOnlinePlayersMenu(int ownerSlot)
    {
        var page = new PageState { RowsAt = 0 };
        IMenu? playersMenu = null;

        var builder = _core.Menu.CreateMenu("tagsystem.players")
            .WithTitle("OYUNCU SEÇİN")
            .AddHeader(new HudTextComponent("Etiket veya grup atamak için bir oyuncuya tıklayın")
            {
                HudDirective = "hint"
            });

        playersMenu = Track(ownerSlot, builder.Build());

        void Populate()
        {
            var players = _core.PlayerManager.GetAllPlayers()
                .Where(p => PlayerHelper.IsUsable(p) && (!p!.IsFakeClient || _config.Settings.ApplyToBots))
                .ToList();

            var rows = new List<IMenuComponent>();
            foreach (var target in players)
            {
                var sid = PlayerHelper.GetSteamId64(target);
                var name = PlayerHelper.GetName(target);
                var safeName = SafeMenuText(name, 22, "Oyuncu");
                var team = PlayerHelper.GetTeam(target);
                var teamTone = team switch
                {
                    "CT" => "ct",
                    "T" => "t",
                    _ => "faint"
                };
                var resolved = _manager.Resolve(target);
                var tagLabel = HudMenuNodeHelper.CleanValue(SafeMenuText(resolved.Label, 14, "-"), 18);
                string badge = $"{SafeMenuText(resolved.Label, 14, "-")} ({SafeMenuText(resolved.SourceId, 12)})";

                if (sid == 0)
                {
                    rows.Add(new HudButtonComponent($"{safeName}\tSteamID Yok", ctx =>
                    {
                        _service.Reply(ctx.Player, "[red]Bu oyuncunun SteamID doğrulaması henüz tamamlanmamış.[default]");
                        return ValueTask.CompletedTask;
                    })
                    {
                        HudDirective = $"avatar;sub=Steam doğrulaması bekleniyor;badge={team};badgetone={teamTone};note=Yetkisiz;notetone=bad"
                    });
                }
                else
                {
                    var steam2 = SteamIdHelper.ToSteam2(sid);
                    var tagTone = resolved.SourceId == "default" ? "faint" : "gold";
                    rows.Add(new HudSubmenuComponent($"{safeName}\t{badge}",
                        _ => BuildManagePlayerMenu(ownerSlot, sid, name))
                    {
                        Tag = sid,
                        HudDirective = $"avatar;sub={steam2};badge={team};badgetone={teamTone};tag={tagLabel};tagtone={tagTone};verb=open"
                    });
                }
            }

            if (rows.Count == 0)
            {
                rows.Add(new HudTextComponent("Aktif oyuncu yok")
                {
                    HudDirective = "glyph=player;tone=faint"
                });
            }

            ReplaceRows(playersMenu, page, rows);
        }

        playersMenu.Opened += session =>
        {
            Populate();
            RestoreFocus(session, page);
        };

        playersMenu.FocusChanged += session =>
        {
            page.Focus = session.FocusedIndex;
            page.FocusKey = (session.FocusedComponent as MenuComponentBase)?.Tag;
        };

        return playersMenu;
    }

    // ============================== 3. TEK OYUNCU YÖNETİMİ ==============================

    private IMenu BuildManagePlayerMenu(int ownerSlot, ulong steamId, string displayName)
    {
        var steam2 = SteamIdHelper.ToSteam2(steamId);
        var safeName = SafeMenuText(displayName, 24, steam2);
        var page = new PageState { RowsAt = 1 };
        IMenu? playerMenu = null;

        var testBtn = new HudButtonComponent("Bu Oyuncu Adına Test Mesajı At", ctx =>
        {
            var custom = _config.Players.FirstOrDefault(p => p.SteamId64 == steamId);
            _service.SendTestChat(ctx.Player, custom, $"{displayName} adına test chat satırı!");
            _service.Reply(ctx.Player, $"{safeName} için test chat satırı sohbete gönderildi.");
            return ValueTask.CompletedTask;
        })
        {
            HudDirective = "glyph=chat;primary;sub=Oyuncunun aktif etiketiyle sohbete önizleme gönderir;verb=select"
        };

        var builder = _core.Menu.CreateMenu("tagsystem.player")
            .WithTitle($"OYUNCU: {safeName}")
            .AddHeader(new HudTextComponent
            {
                TextProvider = _ =>
                {
                    var targetPlayer = _core.PlayerManager.GetAllPlayers()
                        .FirstOrDefault(p => PlayerHelper.IsUsable(p) && PlayerHelper.GetSteamId64(p) == steamId);
                    var custom = _config.Players.FirstOrDefault(p => p.SteamId64 == steamId);
                    var res = targetPlayer != null
                        ? _manager.Resolve(targetPlayer)
                        : (custom != null ? _manager.BuildResolvedTag(custom) : _manager.BuildResolvedTag(_config.Default));
                    return $"{SafeMenuText(res.Label, 16, "-")}\tAktif Etiket";
                },
                HudDirective = "chip=gold;glyph=tag"
            })
            .AddHeader(new HudTextComponent($"{steam2}\tSteamID")
            {
                HudDirective = "chip=info;glyph=player"
            })
            .AddHeader(new HudTextComponent("Özel etiket verin veya grup üyeliklerini açıp kapatın")
            {
                HudDirective = "hint"
            })
            .Add(testBtn);

        playerMenu = Track(ownerSlot, builder.Build());

        void Populate()
        {
            var rows = new List<IMenuComponent>();
            var customEntry = _config.Players.FirstOrDefault(p => p.SteamId64 == steamId);

            if (customEntry != null)
            {
                rows.Add(new SubmenuComponent($"Özel Etiketini Düzenle\t{SafeMenuText(customEntry.ChatTag, 16, "-")}",
                    _ => BuildEntryEditMenu(ownerSlot, customEntry, $"Özel: {safeName}", () =>
                    {
                        _config.Players.Remove(customEntry);
                        SaveAndApply();
                    }, playerMenu))
                {
                    Tag = "edit_custom"
                });

                rows.Add(new DangerSubmenuComponent("Özel Etiketini Kaldır", _ =>
                    BuildConfirmDeleteMenu(ownerSlot, safeName, "Kişisel özel etiketi kalıcı olarak kaldırılacak.", () =>
                    {
                        _config.Players.RemoveAll(x => x.SteamId64 == steamId);
                        SaveAndApply();
                        _service.Reply(_core.PlayerManager.GetPlayer(ownerSlot), $"[lime]{safeName}[default] oyuncusunun özel etiketi kaldırıldı.");
                    }, playerMenu), "remove_custom")
                {
                    Tag = "remove_custom"
                });
            }
            else
            {
                bool creating = false;
                InputComponent? createInput = null;
                createInput = new InputComponent(
                    "Özel Kişisel Etiket Ver",
                    defaultValue: "",
                    onChanged: change =>
                    {
                        if (creating) return;
                        creating = true;
                        var text = change.NewValue.Trim();
                        createInput?.SetValue(change.Session, "");
                        creating = false;

                        var pData = new PlayerData
                        {
                            Id = $"player:{steamId}",
                            SteamId64 = steamId,
                            RawSteamId = steam2,
                            DisplayName = displayName,
                            ChatTag = text,
                            ScoreboardTag = TagService.StripOuterBrackets(text),
                            ChatTagColor = "gold",
                            NameColor = "gold",
                            Priority = 1000,
                            Enabled = true
                        };
                        _config.Players.Add(pData);
                        SaveAndApply();
                        _service.Reply(change.Player, $"[lime]{safeName}[default] oyuncusuna [gold]{text}[default] özel etiketi verildi!");

                        var admin = change.Player;
                        _core.Scheduler.NextWorldUpdate(() =>
                        {
                            if (!PlayerHelper.IsUsable(admin)) return;
                            var edit = BuildEntryEditMenu(ownerSlot, pData, $"Özel: {safeName}", () =>
                            {
                                _config.Players.Remove(pData);
                                SaveAndApply();
                            }, playerMenu);
                            edit.Parent = playerMenu;
                            edit.Open(admin);
                        });
                    },
                    id: "give_custom")
                {
                    MaxLength = 64,
                    EmptyText = "Sohbete Yaz",
                    HintMessage = $"[gold]{safeName} için yeni özel etiket yazın[default] (örn: [lime][KRAL][default])",
                    Tag = "give_custom"
                };

                rows.Add(createInput);
            }

            foreach (var g in _config.Groups)
            {
                var group = g;
                bool isMember = group.Members.Contains(steamId);
                var grpTag = HudMenuNodeHelper.CleanValue(SafeMenuText(group.ChatTag, 14, "-"), 16);
                rows.Add(new HudToggleComponent(
                    $"Grup: {SafeMenuText(group.Label, 16)} ({SafeMenuText(group.Id, 12)})",
                    isMember,
                    change =>
                    {
                        if (change.NewValue)
                        {
                            group.Members.Add(steamId);
                            _service.Reply(change.Player, $"[lime]{safeName}[default], [gold]{group.Id}[default] grubuna eklendi.");
                        }
                        else
                        {
                            group.Members.Remove(steamId);
                            _service.Reply(change.Player, $"[lime]{safeName}[default], [gold]{group.Id}[default] grubundan çıkarıldı.");
                        }
                        SaveAndApply();
                        change.Session.Invalidate();
                    })
                {
                    DefaultValueProvider = _ => group.Members.Contains(steamId),
                    Tag = $"grp:{group.Id}",
                    HudDirective = $"glyph=shield;sub=Etiket: {grpTag} • Öncelik: {group.Priority};verb=switch"
                });
            }

            ReplaceRows(playerMenu, page, rows);
        }

        playerMenu.Opened += session =>
        {
            Populate();
            RestoreFocus(session, page);
        };

        playerMenu.FocusChanged += session =>
        {
            page.Focus = session.FocusedIndex;
            page.FocusKey = (session.FocusedComponent as MenuComponentBase)?.Tag;
        };

        return playerMenu;
    }

    // ============================== 4. ÖZEL OYUNCU ETİKETLERİ ==============================

    private IMenu BuildCustomPlayersMenu(int ownerSlot)
    {
        var page = new PageState { RowsAt = 0 };
        IMenu? listMenu = null;

        var builder = _core.Menu.CreateMenu("tagsystem.custom_players")
            .WithTitle("ÖZEL OYUNCU ETİKETLERİ")
            .AddHeader(new HudTextComponent
            {
                TextProvider = _ => $"{_config.Players.Count}\tKayıtlı Oyuncu",
                HudDirective = "chip=info;glyph=crown"
            })
            .AddHeader(new HudTextComponent("Düzenlemek veya silmek istediğiniz özel oyuncu kaydını seçin")
            {
                HudDirective = "hint"
            });

        listMenu = Track(ownerSlot, builder.Build());

        void Populate()
        {
            var rows = new List<IMenuComponent>();
            foreach (var p in _config.Players.ToList())
            {
                var entry = p;
                var steam2 = SteamIdHelper.ToSteam2(entry.SteamId64);
                var rawLabel = !string.IsNullOrWhiteSpace(entry.DisplayName) ? $"{entry.DisplayName} ({steam2})" : steam2;
                var safeLabel = SafeMenuText(rawLabel, 24, steam2);
                var chatTagClean = HudMenuNodeHelper.CleanValue(SafeMenuText(entry.ChatTag, 16, "-"), 18);

                rows.Add(new HudSubmenuComponent(
                    $"{safeLabel}\t{SafeMenuText(entry.ChatTag, 16, "-")}",
                    _ => BuildEntryEditMenu(ownerSlot, entry, safeLabel, () =>
                    {
                        _config.Players.Remove(entry);
                        SaveAndApply();
                    }, listMenu))
                {
                    Tag = entry.Id,
                    HudDirective = $"avatar;sub={steam2};tag={chatTagClean};tagtone=gold;note=P:{entry.Priority};notetone=faint;verb=edit"
                });
            }

            if (rows.Count == 0)
            {
                rows.Add(new HudTextComponent("Kayıtlı özel oyuncu yok")
                {
                    HudDirective = "glyph=crown;tone=faint"
                });
            }

            ReplaceRows(listMenu, page, rows);
        }

        listMenu.Opened += session =>
        {
            Populate();
            RestoreFocus(session, page);
        };

        listMenu.FocusChanged += session =>
        {
            page.Focus = session.FocusedIndex;
            page.FocusKey = (session.FocusedComponent as MenuComponentBase)?.Tag;
        };

        return listMenu;
    }

    // ============================== 5. YETKİ GRUPLARI ==============================

    private IMenu BuildGroupsListMenu(int ownerSlot)
    {
        var page = new PageState { RowsAt = 1 };
        IMenu? groupsMenu = null;

        var createInput = CreateNewGroupInput(ownerSlot, () => groupsMenu);

        var builder = _core.Menu.CreateMenu("tagsystem.groups")
            .WithTitle("YETKİ GRUPLARI")
            .AddHeader(new HudTextComponent
            {
                TextProvider = _ => $"{_config.Groups.Count}\tGrup",
                HudDirective = "chip=gold;glyph=shield"
            })
            .AddHeader(new HudTextComponent("Öncelik sırasına göre listelenir — düzenlemek için tıklayın")
            {
                HudDirective = "hint"
            })
            .Add(createInput);

        groupsMenu = Track(ownerSlot, builder.Build());

        void Populate()
        {
            var rows = new List<IMenuComponent>();
            foreach (var g in _config.Groups.OrderByDescending(x => x.Priority).ToList())
            {
                var group = g;
                string status = group.Enabled ? "P" : "KAPALI • P";
                var permsSummary = group.Permissions.Count > 0
                    ? HudMenuNodeHelper.CleanValue(string.Join(", ", group.Permissions), 28)
                    : "Yetki yok";
                var badgeText = group.Enabled ? "Aktif" : "Kapalı";
                var badgeTone = group.Enabled ? "good" : "bad";
                var tagClean = HudMenuNodeHelper.CleanValue(SafeMenuText(group.ChatTag, 14, "-"), 16);

                rows.Add(new HudSubmenuComponent(
                    $"{SafeMenuText(group.Id, 14)} ({SafeMenuText(group.ChatTag, 14, "-")})\t{status}:{group.Priority} | {group.Members.Count}",
                    _ => BuildEntryEditMenu(ownerSlot, group, $"Grup: {group.Id}", () =>
                    {
                        _config.Groups.Remove(group);
                        SaveAndApply();
                    }, groupsMenu))
                {
                    Tag = group.Id,
                    HudDirective = $"glyph=shield;sub=Yetkiler: {permsSummary};badge={badgeText};badgetone={badgeTone};tag={tagClean};tagtone=gold;note={group.Members.Count} üye;verb=edit"
                });
            }

            if (rows.Count == 0)
            {
                rows.Add(new HudTextComponent("Kayıtlı grup bulunmuyor")
                {
                    HudDirective = "glyph=shield;tone=faint"
                });
            }

            ReplaceRows(groupsMenu, page, rows);
        }

        groupsMenu.Opened += session =>
        {
            Populate();
            RestoreFocus(session, page);
        };

        groupsMenu.FocusChanged += session =>
        {
            page.Focus = session.FocusedIndex;
            page.FocusKey = (session.FocusedComponent as MenuComponentBase)?.Tag;
        };

        return groupsMenu;
    }

    private HudInputComponent CreateNewGroupInput(int ownerSlot, Func<IMenu?> groupsMenuProvider)
    {
        bool creating = false;
        HudInputComponent? input = null;

        input = new HudInputComponent(
            "Yeni Grup Oluştur",
            defaultValue: "",
            validator: raw =>
            {
                var id = NormalizeGroupId(raw);
                return !string.IsNullOrWhiteSpace(id) && !_config.Groups.Any(g => g.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
            },
            onChanged: change =>
            {
                if (creating) return;
                var id = NormalizeGroupId(change.NewValue);
                if (string.IsNullOrWhiteSpace(id) || _config.Groups.Any(g => g.Id.Equals(id, StringComparison.OrdinalIgnoreCase)))
                    return;

                creating = true;
                input?.SetValue(change.Session, "");
                creating = false;

                var tagText = $"[{id.ToUpperInvariant()}]";
                var group = new GroupData
                {
                    Id = id,
                    DisplayName = id.ToUpperInvariant(),
                    Permissions = [id, $"tagsystem.{id}"],
                    ChatTag = tagText,
                    ScoreboardTag = id.ToUpperInvariant(),
                    ChatTagColor = "gold",
                    NameColor = "gold",
                    Priority = 50,
                    Enabled = true
                };
                _config.Groups.Add(group);
                SaveAndApply();
                _service.Reply(change.Player, $"[lime]'{id}'[default] grubu oluşturuldu! Şimdi detaylarını düzenleyebilirsiniz.");

                var admin = change.Player;
                _core.Scheduler.NextWorldUpdate(() =>
                {
                    if (!PlayerHelper.IsUsable(admin)) return;
                    var groupsMenu = groupsMenuProvider();
                    var editMenu = BuildEntryEditMenu(ownerSlot, group, $"Grup: {group.Id}", () =>
                    {
                        _config.Groups.Remove(group);
                        SaveAndApply();
                    }, groupsMenu);
                    editMenu.Parent = groupsMenu ?? change.Session.Menu;
                    editMenu.Open(admin);
                });
            },
            id: "create_group")
        {
            MaxLength = 48,
            EmptyText = "Sohbete Yaz",
            HintMessage = "[gold]Yeni grubun ID'sini sohbete yazın[default] (örn: [lime]mod, rehber, supervip[default])",
            InvalidText = "Geçersiz veya mevcut ID",
            AcceptedText = "Grup oluşturuldu",
            HudDirective = "glyph=add;sub=Sohbete yeni grup ID'si yazarak anında oluşturun;verb=edit"
        };

        return input;
    }

    // ============================== 6. ETİKET DÜZENLEME ==============================

    private IMenu BuildEntryEditMenu(int ownerSlot, TagEntry entry, string title, Action? onDelete, IMenu? parentForDelete)
    {
        bool syncingChatTag = false;
        bool syncingClanTag = false;
        bool syncingPriority = false;
        bool syncingPerms = false;

        InputComponent? chatTagInput = null;
        InputComponent? clanTagInput = null;
        SliderComponent? prioritySlider = null;
        InputComponent? priorityInput = null;

        chatTagInput = new InputComponent("Chat Etiketi", defaultValue: SafeMenuText(entry.ChatTag, 32), onChanged: change =>
        {
            if (syncingChatTag) return;
            syncingChatTag = true;
            var val = change.NewValue.Trim();
            entry.ChatTag = val == "-" ? "" : val;
            chatTagInput?.SetValue(change.Session, SafeMenuText(entry.ChatTag, 32));
            syncingChatTag = false;
            SaveAndApply();
            change.Session.Invalidate();
            _service.Reply(change.Player, $"Chat etiketi [lime]{entry.ChatTag}[default] olarak güncellendi.");
        })
        {
            MaxLength = 64,
            EmptyText = "(boş)",
            DefaultValueProvider = _ => SafeMenuText(entry.ChatTag, 32),
            HintMessage = "[gold]Yeni Chat Etiketini yazın[default] (örn: [lime][VIP][default] veya temizlemek için [lightred]-[default])"
        };

        clanTagInput = new InputComponent("Scoreboard / Clan Tag", defaultValue: SafeMenuText(entry.ScoreboardTag, 32), onChanged: change =>
        {
            if (syncingClanTag) return;
            syncingClanTag = true;
            var val = change.NewValue.Trim();
            entry.ScoreboardTag = val == "-" ? "" : TagService.StripOuterBrackets(val);
            clanTagInput?.SetValue(change.Session, SafeMenuText(entry.ScoreboardTag, 32));
            syncingClanTag = false;
            SaveAndApply();
            change.Session.Invalidate();
            _service.Reply(change.Player, $"Scoreboard etiketi [lime]{entry.ScoreboardTag}[default] olarak güncellendi.");
        })
        {
            MaxLength = 48,
            EmptyText = "(boş)",
            DefaultValueProvider = _ => SafeMenuText(entry.ScoreboardTag, 32),
            HintMessage = "[gold]Yeni Scoreboard Etiketini yazın[default] (boş bırakmak için [lightred]-[default] yazın)"
        };

        prioritySlider = new SliderComponent("Öncelik (Slider)", -1000f, 1000f, Math.Clamp(entry.Priority, -1000, 1000), 10f, change =>
        {
            if (syncingPriority) return;
            syncingPriority = true;
            entry.Priority = (int)MathF.Round(change.NewValue);
            priorityInput?.SetValue(change.Session, entry.Priority.ToString(CultureInfo.InvariantCulture));
            syncingPriority = false;
            SaveAndApply();
        })
        {
            TotalBars = 10,
            ValueFormat = "0",
            DefaultValueProvider = _ => Math.Clamp(entry.Priority, -1000, 1000)
        };

        priorityInput = new InputComponent("Özel Öncelik Sayısı", defaultValue: entry.Priority.ToString(CultureInfo.InvariantCulture),
            validator: val => int.TryParse(val.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out _),
            onChanged: change =>
            {
                if (syncingPriority) return;
                if (!int.TryParse(change.NewValue.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int p))
                    return;

                syncingPriority = true;
                entry.Priority = p;
                prioritySlider?.SetValue(change.Session, Math.Clamp(p, -1000, 1000));
                syncingPriority = false;
                SaveAndApply();
                _service.Reply(change.Player, $"Öncelik [lime]{p}[default] olarak güncellendi.");
            })
        {
            MaxLength = 16,
            DefaultValueProvider = _ => entry.Priority.ToString(CultureInfo.InvariantCulture),
            HintMessage = "[gold]Öncelik sayısını yazın[default] (örn: [lime]100, 500, -100[default])",
            InvalidText = "Sayı giriniz"
        };

        var builder = _core.Menu.CreateMenu("tagsystem.entry")
            .WithTitle(SafeMenuText(title, 28, "ETİKET DÜZENLE"))
            .AddHeader(new HudTextComponent
            {
                TextProvider = session =>
                {
                    var preview = _service.BuildPreviewChatLine(session.Player, entry, "Merhaba!", stripped: true);
                    return $"{SafeMenuText(preview, 28, "-")}\tÖnizleme";
                },
                HudDirective = "chip=gold;glyph=chat"
            })
            .AddHeader(new HudTextComponent("Renkleri A/D veya tıklayarak değiştirin — tüm değişiklikler anında kaydedilir")
            {
                HudDirective = "hint"
            })
            .Add(new HudButtonComponent("Sohbete Canlı Test Mesajı At", ctx =>
            {
                _service.SendTestChat(ctx.Player, entry, "TagSystem etiket önizleme test mesajı!");
                return ValueTask.CompletedTask;
            })
            {
                HudDirective = "glyph=chat;primary;sub=Bu etiketin gerçek renkli görünümünü sohbete yazar;verb=select"
            })
            .Add(new HudButtonComponent("Ekrana Önizleme Göster (SendCenter)", ctx =>
            {
                var res = _manager.BuildResolvedTag(entry);
                var cleanTag = string.IsNullOrEmpty(res.ChatTag) ? "" : $"{res.ChatTag} ";
                var clan = string.IsNullOrEmpty(res.ScoreboardTag) ? "-" : res.ScoreboardTag;
                ctx.Player.SendCenter($"[{clan}] {cleanTag}{PlayerHelper.GetName(ctx.Player)}: Merhaba Dünya!");
                return ValueTask.CompletedTask;
            })
            {
                HudDirective = "glyph=star;sub=Skorboard ve chat etiketini ekranın ortasında önizler;verb=select"
            })
            .Add(new HudToggleComponent("Durum (Aktif)", entry.Enabled, change =>
            {
                entry.Enabled = change.NewValue;
                SaveAndApply();
                change.Session.Invalidate();
            })
            {
                DefaultValueProvider = _ => entry.Enabled,
                HudDirective = "glyph=ready;sub=Kapalıysa bu etiket tanımı oyunculara uygulanmaz;verb=switch"
            })
            .Add(chatTagInput)
            .Add(clanTagInput)
            .Add(new HudSelectorComponent<FrameOption>("Çerçeve / Sembol", FrameOptions, GetFrameIndex(entry.Frame), change =>
            {
                entry.Frame = change.NewValue.Key == "none" ? null : change.NewValue.Key;
                SaveAndApply();
                change.Session.Invalidate();
            })
            {
                DefaultValueProvider = _ => GetFrameIndex(entry.Frame),
                Formatter = f => f.Name,
                HudDirective = "glyph=crown;verb=change"
            })
            .Add(new SwatchColorSelector("Etiket Rengi", ColorPalette, GetColorIndex(entry.ChatTagColor ?? "gold"), change =>
            {
                entry.ChatTagColor = change.NewValue.Key;
                SaveAndApply();
                change.Session.Invalidate();
            })
            {
                DefaultValueProvider = _ => GetColorIndex(entry.ChatTagColor ?? "gold")
            })
            .Add(new SwatchColorSelector("İsim Rengi", ColorPalette, GetColorIndex(entry.NameColor ?? "teamcolor"), change =>
            {
                entry.NameColor = change.NewValue.Key;
                SaveAndApply();
                change.Session.Invalidate();
            })
            {
                DefaultValueProvider = _ => GetColorIndex(entry.NameColor ?? "teamcolor")
            })
            .Add(new SwatchColorSelector("Mesaj Rengi", ColorPalette, GetColorIndex(entry.MessageColor ?? "default"), change =>
            {
                entry.MessageColor = change.NewValue.Key;
                SaveAndApply();
                change.Session.Invalidate();
            })
            {
                DefaultValueProvider = _ => GetColorIndex(entry.MessageColor ?? "default")
            })
            .Add(prioritySlider)
            .Add(priorityInput);

        if (entry is GroupData group)
        {
            InputComponent? permsInput = null;
            permsInput = new InputComponent(
                "Yetkiler",
                defaultValue: string.Join(", ", group.Permissions),
                onChanged: change =>
                {
                    if (syncingPerms) return;
                    syncingPerms = true;
                    var raw = change.NewValue.Trim();
                    group.Permissions = raw == "-"
                        ? []
                        : raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
                    permsInput?.SetValue(change.Session, SafeMenuText(string.Join(", ", group.Permissions), 48));
                    syncingPerms = false;
                    SaveAndApply();
                    _service.Reply(change.Player, $"Grup yetkileri güncellendi: [lime]{string.Join(", ", group.Permissions)}[default]");
                })
            {
                MaxLength = 256,
                EmptyText = "(yok)",
                DefaultValueProvider = _ => SafeMenuText(string.Join(", ", group.Permissions), 48),
                HintMessage = "[gold]Yetki anahtarlarını virgülle yazın[default] (temizlemek için [lightred]-[default])"
            };
            builder.Add(permsInput);
        }

        if (entry != _config.Default)
        {
            builder.Add(new SelectorComponent<int>("Takım Kısıtlaması", [0, 1, 2, 3], GetTeamPresetIndex(entry.Teams), change =>
            {
                entry.Teams = change.NewValue switch
                {
                    1 => ["T"],
                    2 => ["CT"],
                    3 => ["SPEC"],
                    _ => []
                };
                SaveAndApply();
            })
            {
                DefaultValueProvider = _ => GetTeamPresetIndex(entry.Teams),
                Formatter = idx => idx switch
                {
                    1 => "Sadece T",
                    2 => "Sadece CT",
                    3 => "Sadece İzleyici",
                    _ => "Herkes"
                }
            });
        }

        if (onDelete != null)
        {
            builder.Add(new DangerSubmenuComponent("Bu Kaydı Sil", _ =>
                BuildConfirmDeleteMenu(ownerSlot, SafeMenuText(entry.Id, 24), "Bu etiket tanımı kalıcı olarak silinecek.", () =>
                {
                    onDelete();
                    _service.Reply(_core.PlayerManager.GetPlayer(ownerSlot), "[red]Kayıt silindi.[default]");
                }, parentForDelete), "delete"));
        }

        return Track(ownerSlot, builder.Build());
    }

    private IMenu BuildConfirmDeleteMenu(int ownerSlot, string subjectTitle, string description, Action onConfirm, IMenu? parentToJumpBack)
    {
        IMenu? confirmMenu = null;

        confirmMenu = _core.Menu.CreateMenu("tagsystem.confirm_delete")
            .WithTitle($"SİL: {SafeMenuText(subjectTitle, 24)}")
            .AddHeader(new TextComponent(description))
            .Add(new ButtonComponent("Evet, Sil", ctx =>
            {
                onConfirm();
                if (confirmMenu != null && parentToJumpBack != null)
                {
                    confirmMenu.Parent = parentToJumpBack;
                }
                ctx.Session.Close();
                return ValueTask.CompletedTask;
            })
            {
                FocusedColor = "#F87171"
            })
            .Add(new ButtonComponent("Vazgeç", ctx =>
            {
                ctx.Session.Close();
                return ValueTask.CompletedTask;
            }))
            .Build();

        return Track(ownerSlot, confirmMenu);
    }

    // ============================== 7. GENEL AYARLAR ==============================

    private IMenu BuildSettingsMenu(int ownerSlot)
    {
        var s = _config.Settings;
        var c = _config.Chat;

        var menu = _core.Menu.CreateMenu("tagsystem.settings")
            .WithTitle("GENEL AYARLAR")
            .AddHeader(new HudTextComponent("Değişiklikler anında kaydedilir")
            {
                HudDirective = "hint"
            })
            .Add(new HudToggleComponent("Scoreboard (Clan Tag)", s.EnableScoreboardTag, change =>
            {
                s.EnableScoreboardTag = change.NewValue;
                SaveAndApply();
                if (s.EnableScoreboardTag)
                {
                    _service.EnsureClanTagsEnabled();
                    _service.ApplyScoreboardAll(force: true);
                }
                else
                {
                    _service.RestoreScoreboardAll();
                }
            })
            {
                DefaultValueProvider = _ => s.EnableScoreboardTag,
                HudDirective = "glyph=trophy;sub=TAB skor tablosunda clan tag gösterimini açar/kapatır;verb=switch"
            })
            .Add(new HudToggleComponent("Chat Formatlama", s.EnableChatTag, change =>
            {
                s.EnableChatTag = change.NewValue;
                SaveAndApply();
            })
            {
                DefaultValueProvider = _ => s.EnableChatTag,
                HudDirective = "glyph=chat;sub=Sohbet mesajlarına renkli etiket ve format uygular;verb=switch"
            })
            .Add(new HudToggleComponent("Yetki Sistemi (use_permissions)", s.UsePermissions, change =>
            {
                s.UsePermissions = change.NewValue;
                SaveAndApply();
            })
            {
                DefaultValueProvider = _ => s.UsePermissions,
                HudDirective = "glyph=key;sub=Grupları admin/VIP yetkileriyle otomatik eşleştirir;verb=switch"
            })
            .Add(new HudToggleComponent("Ölüler Sadece Ölülere Yazsın", c.DeadTalkOnlyToDead, change =>
            {
                c.DeadTalkOnlyToDead = change.NewValue;
                SaveAndApply();
            })
            {
                DefaultValueProvider = _ => c.DeadTalkOnlyToDead,
                HudDirective = "glyph=mute;sub=Ölü oyuncuların sohbet mesajlarını canlılardan gizler;verb=switch"
            })
            .Add(new HudToggleComponent("Botlara Etiket Uygula", s.ApplyToBots, change =>
            {
                s.ApplyToBots = change.NewValue;
                SaveAndApply();
            })
            {
                DefaultValueProvider = _ => s.ApplyToBots,
                HudDirective = "glyph=bot;sub=Sunucudaki botlara da varsayılan etiketi uygular;verb=switch"
            })
            .Add(new HudButtonComponent("Skorboard Taglarını Şimdi Yenile", ctx =>
            {
                _service.EnsureClanTagsEnabled();
                _service.ApplyScoreboardAll(force: true);
                _service.Reply(ctx.Player, "[lime]Tüm oyuncuların skorboard tagları yeniden senkronize edildi.[default]");
                return ValueTask.CompletedTask;
            })
            {
                HudDirective = "glyph=refresh;primary;sub=sv_clantags_enabled kontrolü yapıp tüm TAB taglarını yeniler;verb=apply"
            })
            .Build();

        return Track(ownerSlot, menu);
    }

    // ============================== YARDIMCILAR ==============================

    private void SaveAndApply()
    {
        try { _loader.Save(_config); }
        catch (Exception ex) { _core.Logger.LogError(ex, "[TagSystem] Config kaydedilemedi."); }

        _manager.UpdateConfig(_config);
        _service.UpdateConfig(_config);
        _service.ApplyScoreboardAll(force: true);
    }

    private static string NormalizeGroupId(string? raw) =>
        (raw ?? string.Empty).Trim().ToLowerInvariant().Replace(' ', '_');

    private static int GetColorIndex(string color)
    {
        int idx = Array.FindIndex(ColorPalette, c => c.Key.Equals(color, StringComparison.OrdinalIgnoreCase));
        return idx < 0 ? 0 : idx;
    }

    private static int GetFrameIndex(string? frame)
    {
        if (string.IsNullOrWhiteSpace(frame)) return 0;
        int idx = Array.FindIndex(FrameOptions, f => f.Key.Equals(frame, StringComparison.OrdinalIgnoreCase));
        return idx < 0 ? 0 : idx;
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

    private static string SafeMenuText(string? text, int maxLen = 28, string fallback = "")
    {
        if (string.IsNullOrWhiteSpace(text)) return fallback;
        var clean = ColorHelper.StripAll(text)
            .Replace("<", "")
            .Replace(">", "")
            .Replace("\r", " ")
            .Replace("\n", " ")
            .Trim();
        if (clean.Length == 0) return fallback;
        return clean.Length > maxLen ? clean[..maxLen] + "..." : clean;
    }

    private static void ReplaceRows(IMenu menu, PageState page, IReadOnlyList<IMenuComponent> rows)
    {
        foreach (var row in page.Rows)
            menu.Remove(row);
        page.Rows.Clear();

        for (int i = 0; i < rows.Count; i++)
        {
            menu.Insert(MenuRegion.Body, page.RowsAt + i, rows[i]);
            page.Rows.Add(rows[i]);
        }
    }

    private static void RestoreFocus(IMenuSession session, PageState page)
    {
        if (page.FocusKey is { } key && FocusWhere(session, c => c is MenuComponentBase { Tag: { } tag } && tag.Equals(key)))
            return;

        if (page.Focus is { } focus)
        {
            for (int i = focus; i > 0; i--)
            {
                if (session.FocusedIndex == i || session.SetFocus(i))
                    return;
            }
        }
    }

    private static bool FocusWhere(IMenuSession session, Func<IMenuComponent, bool> match)
    {
        int index = 0;
        foreach (var component in session.Menu.GetComponents(MenuRegion.Body))
        {
            if (!component.IsFocusable || !component.IsVisible(session))
                continue;

            if (match(component))
            {
                if (session.FocusedIndex != index)
                    session.SetFocus(index);
                return true;
            }

            index++;
        }

        return false;
    }
}

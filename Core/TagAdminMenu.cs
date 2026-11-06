using System.Net;
using System.Text;
using Microsoft.Extensions.Logging;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Events;
using SwiftlyS2.Shared.Natives;
using SwiftlyS2.Shared.Players;
using SwiftlyS2.Shared.SchemaDefinitions;
using TagSystem.Config;
using TagSystem.Models;
using TagSystem.Utils;

namespace TagSystem.Core;

/// <summary>
/// Adminler icin oyun ici gorsel (CenterHTML) etiket ve grup yonetim menusu (!tagmenu).
/// W/S: Yukari/Asagi | A/D: Deger degistir | E: Sec | R: Geri/Kapat
/// Metin girisi gerektiginde (etiket metni, yetki vb.) bir sonraki chat mesajini yakalar.
/// </summary>
public sealed class TagAdminMenu
{
    private const int RowsPerPage = 6;
    private const int HtmlDurationMs = 2000;
    private const float ResendSeconds = 0.5f;

    private static readonly string[] ColorCycle =
    {
        "gold", "lightred", "red", "darkred", "lightblue", "blue", "darkblue",
        "lime", "green", "olive", "yellow", "lightyellow", "orange",
        "purple", "lightpurple", "magenta", "grey", "silver", "white", "bluegrey",
        "teamcolor", "rainbow", "fire", "ice", "neon", "sunset", "toxic",
        "police", "blood", "royal", "matrix", "pastel",
        "galaxy", "cyberpunk", "lava", "aurora", "ghost", "nature", "candy", "goldrush",
        "redblue", "pinkblue", "goldblack", "greenyellow", "blackwhite", "redwhite",
        "default"
    };

    private static readonly string[] FrameCycle = PlaceholderHelper.AvailableFrames.Select(f => f.Key).ToArray();

    private static readonly string[][] TeamPresets =
    {
        Array.Empty<string>(),
        new[] { "T" },
        new[] { "CT" },
        new[] { "SPEC" }
    };

    private static readonly string[] TeamPresetLabels = { "Herkes", "Sadece T", "Sadece CT", "Sadece İzleyici" };

    private readonly ISwiftlyCore _core;
    private readonly ConfigLoader _loader;
    private readonly TagManager _manager;
    private readonly TagService _service;
    private PluginConfig _config;

    private readonly Dictionary<int, Session> _sessions = new();
    private readonly Dictionary<int, (string Prompt, Action<IPlayer, string> Callback)> _pendingInput = new();
    private readonly Dictionary<int, (string Html, float Time)> _lastHtml = new();

    private enum RowStyle { Normal, Accent, On, Off, Danger, Muted }

    private sealed record Row(
        string Label,
        string? Value = null,
        bool Arrows = false,
        RowStyle Style = RowStyle.Normal,
        Action<IPlayer>? OnSelect = null,
        Action<IPlayer, int>? OnAdjust = null);

    private sealed class Page
    {
        public string Title { get; init; } = "";
        public string Subtitle { get; init; } = "";
        public List<Row> Rows { get; init; } = new();
    }

    private delegate Page PageBuilder(IPlayer admin);

    private sealed class Session(PageBuilder root)
    {
        private readonly Stack<(PageBuilder Builder, int Cursor)> _history = new();
        public PageBuilder Current { get; private set; } = root;
        public int Cursor { get; set; }
        public GameButtonFlags LastButtons { get; set; }
        public float LastKeyTime { get; set; }

        public void Push(PageBuilder next)
        {
            _history.Push((Current, Cursor));
            Current = next;
            Cursor = 0;
        }

        public bool Back()
        {
            if (_history.Count == 0) return false;
            var (b, c) = _history.Pop();
            Current = b;
            Cursor = c;
            return true;
        }
    }

    public TagAdminMenu(ISwiftlyCore core, ConfigLoader loader, PluginConfig config, TagManager manager, TagService service)
    {
        _core = core;
        _loader = loader;
        _config = config;
        _manager = manager;
        _service = service;
    }

    public void UpdateConfig(PluginConfig config) => _config = config;

    public bool IsMenuOpen(int slot) => _sessions.ContainsKey(slot);
    public bool IsWaitingChatInput(int slot) => _pendingInput.ContainsKey(slot);

    public void Toggle(IPlayer admin)
    {
        if (IsMenuOpen(admin.Slot)) Close(admin);
        else Open(admin);
    }

    public void Open(IPlayer admin)
    {
        _sessions[admin.Slot] = new Session(BuildMainPage)
        {
            LastButtons = admin.PressedButtons,
            LastKeyTime = _core.Engine.GlobalVars.CurrentTime
        };
        SetFrozen(admin, true);
        _service.Reply(admin, "Yönetim menüsü açıldı! [lime]W/S[default]: Gezin | [lime]A/D[default]: Değiştir | [lime]E[default]: Seç | [lime]R[default]: Geri/Kapat");
    }

    public void Close(IPlayer admin)
    {
        _pendingInput.Remove(admin.Slot);
        if (_sessions.Remove(admin.Slot))
        {
            SetFrozen(admin, false);
            if (_lastHtml.Remove(admin.Slot))
                admin.SendCenterHTML(" ", 1);
        }
    }

    public void OnPlayerDisconnected(int slot)
    {
        _sessions.Remove(slot);
        _pendingInput.Remove(slot);
        _lastHtml.Remove(slot);
    }

    public void CloseAll()
    {
        foreach (var p in _core.PlayerManager.GetAllPlayers().Where(PlayerHelper.IsUsable))
        {
            if (_sessions.ContainsKey(p!.Slot)) Close(p);
        }
        _sessions.Clear();
        _pendingInput.Clear();
        _lastHtml.Clear();
    }

    /// <summary>Chat hook icinden cagrilir; admin metin girisi bekliyorsa mesaji yakalar.</summary>
    public bool TryConsumeChatInput(int playerId, string text)
    {
        if (!_pendingInput.TryGetValue(playerId, out var pending)) return false;

        var admin = _core.PlayerManager.GetPlayer(playerId);
        if (!PlayerHelper.IsUsable(admin))
        {
            _pendingInput.Remove(playerId);
            return false;
        }

        var trimmed = (text ?? string.Empty).Trim();
        if (trimmed.Length >= 2 && trimmed[0] == '"' && trimmed[^1] == '"')
            trimmed = trimmed[1..^1].Trim();

        _pendingInput.Remove(playerId);

        if (trimmed.Equals("iptal", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Equals("!iptal", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Equals("cancel", StringComparison.OrdinalIgnoreCase))
        {
            _service.Reply(admin, "[red]Giriş iptal edildi.[default]");
            return true;
        }

        pending.Callback(admin!, trimmed);
        return true;
    }

    private void PromptInput(IPlayer admin, string prompt, Action<IPlayer, string> callback)
    {
        _pendingInput[admin.Slot] = (prompt, callback);
        _service.Reply(admin, $"[gold]{prompt}[default] (İptal için sohbete [red]iptal[default] yazın)");
    }

    // ============================== TICK & INPUT ==============================

    public void OnTick()
    {
        if (_sessions.Count == 0) return;

        foreach (var kv in _sessions.ToList())
        {
            var admin = _core.PlayerManager.GetPlayer(kv.Key);
            if (!PlayerHelper.IsUsable(admin))
            {
                OnPlayerDisconnected(kv.Key);
                continue;
            }

            PollButtons(admin!, kv.Value);
            Render(admin!, kv.Value);
        }
    }

    private void PollButtons(IPlayer admin, Session session)
    {
        if (_config.Settings.FreezeWhileMenuOpen)
            SetFrozen(admin, true);

        var buttons = admin.PressedButtons;
        var rising = buttons & ~session.LastButtons;
        session.LastButtons = buttons;
        if (rising == 0) return;

        if ((rising & GameButtonFlags.W) != 0) OnKeyPressed(admin, KeyKind.W);
        else if ((rising & GameButtonFlags.S) != 0) OnKeyPressed(admin, KeyKind.S);
        else if ((rising & GameButtonFlags.A) != 0) OnKeyPressed(admin, KeyKind.A);
        else if ((rising & GameButtonFlags.D) != 0) OnKeyPressed(admin, KeyKind.D);
        else if ((rising & GameButtonFlags.E) != 0) OnKeyPressed(admin, KeyKind.E);
        else if ((rising & GameButtonFlags.R) != 0) OnKeyPressed(admin, KeyKind.R);
    }

    public void OnKeyPressed(IPlayer admin, KeyKind key)
    {
        if (!_sessions.TryGetValue(admin.Slot, out var session)) return;

        var now = _core.Engine.GlobalVars.CurrentTime;
        if (now - session.LastKeyTime < 0.12f) return;
        session.LastKeyTime = now;

        var page = session.Current(admin);
        var count = page.Rows.Count;
        if (count == 0) return;
        session.Cursor = Math.Clamp(session.Cursor, 0, count - 1);

        switch (key)
        {
            case KeyKind.W:
                session.Cursor = (session.Cursor - 1 + count) % count;
                break;
            case KeyKind.S:
                session.Cursor = (session.Cursor + 1) % count;
                break;
            case KeyKind.A:
                page.Rows[session.Cursor].OnAdjust?.Invoke(admin, -1);
                break;
            case KeyKind.D:
                page.Rows[session.Cursor].OnAdjust?.Invoke(admin, +1);
                break;
            case KeyKind.E:
                var r = page.Rows[session.Cursor];
                if (r.OnSelect != null) r.OnSelect(admin);
                else r.OnAdjust?.Invoke(admin, +1);
                break;
            case KeyKind.R:
            case KeyKind.Esc:
                _pendingInput.Remove(admin.Slot);
                if (!session.Back()) Close(admin);
                break;
        }
    }

    private void Render(IPlayer admin, Session session)
    {
        var page = session.Current(admin);
        if (page.Rows.Count > 0)
            session.Cursor = Math.Clamp(session.Cursor, 0, page.Rows.Count - 1);

        int pageIdx = session.Cursor / RowsPerPage;
        int pageCount = Math.Max(1, (page.Rows.Count + RowsPerPage - 1) / RowsPerPage);
        var slice = page.Rows.Skip(pageIdx * RowsPerPage).Take(RowsPerPage).ToList();
        int selectedInSlice = session.Cursor - pageIdx * RowsPerPage;

        var sb = new StringBuilder();
        sb.Append($"<font class='fontSize-m' color='#38BDF8'><b>✦ {E(page.Title)} ✦</b></font>");
        if (pageCount > 1)
            sb.Append($" <font class='fontSize-s' color='#94A3B8'>[{pageIdx + 1}/{pageCount}]</font>");
        sb.Append("<br>");

        if (!string.IsNullOrEmpty(page.Subtitle))
            sb.Append($"<font class='fontSize-xs' color='#CBD5E1'>{E(page.Subtitle)}</font><br>");

        for (int i = 0; i < slice.Count; i++)
        {
            var row = slice[i];
            bool sel = i == selectedInSlice;
            var labelColor = sel ? "#FACC15" : row.Style switch
            {
                RowStyle.Danger => "#F87171",
                RowStyle.Accent => "#38BDF8",
                RowStyle.Muted => "#64748B",
                _ => "#F8FAFC"
            };

            sb.Append("<font class='fontSize-sm'>");
            sb.Append(sel ? "<font color='#FACC15'><b>➤ </b></font>" : "<font color='#475569'>• </font>");
            sb.Append(sel
                ? $"<font color='{labelColor}'><b>{E(row.Label)}</b></font>"
                : $"<font color='{labelColor}'>{E(row.Label)}</font>");

            if (!string.IsNullOrEmpty(row.Value))
            {
                var valColor = row.Style switch
                {
                    RowStyle.On => "#4ADE80",
                    RowStyle.Off => "#F87171",
                    _ => "#E2E8F0"
                };
                var v = row.Arrows ? $"◀ {E(row.Value)} ▶" : $"[{E(row.Value)}]";
                sb.Append($" <font color='{valColor}'><b>{v}</b></font>");
            }
            sb.Append("</font><br>");
        }

        if (_pendingInput.TryGetValue(admin.Slot, out var pending))
            sb.Append($"<font class='fontSize-s' color='#FBBF24'><b>💬 Sohbete yazın: {E(pending.Prompt)}</b></font><br>");

        sb.Append("<font class='fontSize-xs' color='#94A3B8'>[W/S] Gezin • [A/D] Değiştir • [E] Seç • [R] Geri/Kapat</font>");

        var html = sb.ToString();
        var now = _core.Engine.GlobalVars.CurrentTime;
        if (_lastHtml.TryGetValue(admin.Slot, out var last) && last.Html == html && now - last.Time < ResendSeconds)
            return;

        _lastHtml[admin.Slot] = (html, now);
        admin.SendCenterHTML(html, HtmlDurationMs);
    }

    private static string E(string s) => WebUtility.HtmlEncode(s);

    private void SetFrozen(IPlayer player, bool freeze)
    {
        if (!_config.Settings.FreezeWhileMenuOpen && freeze) return;
        try
        {
            var pawn = player.PlayerPawn;
            if (pawn is not { IsValid: true }) return;
            var target = freeze ? MoveType_t.MOVETYPE_NONE : MoveType_t.MOVETYPE_WALK;
            if (pawn.MoveType != target)
            {
                pawn.MoveType = target;
                pawn.ActualMoveType = target;
                pawn.MoveTypeUpdated();
            }
            if (freeze)
            {
                pawn.AbsVelocity.X = 0f;
                pawn.AbsVelocity.Y = 0f;
                pawn.AbsVelocity.Z = 0f;
            }
        }
        catch { /* yoksay */ }
    }

    private void SaveAndApply()
    {
        try { _loader.Save(_config); }
        catch (Exception ex) { _core.Logger.LogError(ex, "[TagSystem] Config kaydedilemedi."); }

        _manager.UpdateConfig(_config);
        _service.UpdateConfig(_config);
        _service.ApplyScoreboardAll();
    }

    // ============================== MENU PAGES ==============================

    private Page BuildMainPage(IPlayer admin)
    {
        var onlineCount = _core.PlayerManager.GetAllPlayers().Count(p => PlayerHelper.IsUsable(p) && !p!.IsFakeClient);
        return new Page
        {
            Title = "TagSystem Yönetim",
            Subtitle = $"{_config.Groups.Count} grup • {_config.Players.Count} özel oyuncu",
            Rows =
            {
                new Row("👤 Oyuncuya Etiket / Grup Ver", $"{onlineCount} aktif", Style: RowStyle.Accent,
                    OnSelect: p => _sessions[p.Slot].Push(BuildOnlinePlayersPage)),
                new Row("⭐ Özel Oyuncu Etiketleri", _config.Players.Count.ToString(),
                    OnSelect: p => _sessions[p.Slot].Push(BuildCustomPlayersPage)),
                new Row("🛡️ Yetki Gruplarını Yönet", _config.Groups.Count.ToString(),
                    OnSelect: p => _sessions[p.Slot].Push(BuildGroupsListPage)),
                new Row("➕ Yeni Yetki Grubu Oluştur", Style: RowStyle.Accent,
                    OnSelect: StartCreateGroupFlow),
                new Row("⚙️ Varsayılan [default] Etiket", _config.Default.ChatTag ?? "-",
                    OnSelect: p => _sessions[p.Slot].Push(_ => BuildEntryEditPage(_config.Default, "Varsayılan [default]"))),
                new Row("🔧 Genel Ayarlar",
                    OnSelect: p => _sessions[p.Slot].Push(BuildSettingsPage)),
                new Row("❌ Menüyü Kapat", Style: RowStyle.Danger, OnSelect: Close)
            }
        };
    }

    private Page BuildOnlinePlayersPage(IPlayer admin)
    {
        var players = _core.PlayerManager.GetAllPlayers()
            .Where(p => PlayerHelper.IsUsable(p) && (!p!.IsFakeClient || _config.Settings.ApplyToBots))
            .ToList();

        var rows = new List<Row>();
        foreach (var target in players)
        {
            var sid = PlayerHelper.GetSteamId64(target);
            var name = PlayerHelper.GetName(target);
            var resolved = _manager.Resolve(target);
            rows.Add(new Row(
                Label: name,
                Value: $"{resolved.Label} ({resolved.SourceId})",
                Style: sid == 0 ? RowStyle.Muted : RowStyle.Normal,
                OnSelect: a =>
                {
                    if (sid == 0)
                    {
                        _service.Reply(a, "[red]Bu oyuncunun SteamID doğrulaması henüz tamamlanmamış.[default]");
                        return;
                    }
                    _sessions[a.Slot].Push(_ => BuildManagePlayerPage(sid, name));
                }));
        }

        if (rows.Count == 0)
            rows.Add(new Row("Aktif oyuncu yok", Style: RowStyle.Muted));

        return new Page
        {
            Title = "Oyuncu Seçin",
            Subtitle = "Etiket veya grup atamak istediğiniz oyuncuyu seçin",
            Rows = rows
        };
    }

    private Page BuildManagePlayerPage(ulong steamId, string displayName)
    {
        var steam2 = SteamIdHelper.ToSteam2(steamId);
        var customEntry = _config.Players.FirstOrDefault(p => p.SteamId64 == steamId);

        var rows = new List<Row>();

        if (customEntry != null)
        {
            rows.Add(new Row("✏️ Özel Etiketini Düzenle", customEntry.ChatTag ?? "-", Style: RowStyle.Accent,
                OnSelect: a => _sessions[a.Slot].Push(_ => BuildEntryEditPage(customEntry, $"Özel: {displayName}"))));
            rows.Add(new Row("🗑️ Özel Etiketini Kaldır", Style: RowStyle.Danger,
                OnSelect: a =>
                {
                    _config.Players.RemoveAll(x => x.SteamId64 == steamId);
                    SaveAndApply();
                    _service.Reply(a, $"[lime]{displayName}[default] oyuncusunun özel etiketi kaldırıldı.");
                }));
        }
        else
        {
            rows.Add(new Row("✨ Özel Kişisel Etiket Ver (Sohbetten Yaz)", Style: RowStyle.Accent,
                OnSelect: a => PromptInput(a, $"{displayName} için yeni etiket yazın (örn: [KRAL]):", (adm, text) =>
                {
                    var pData = new PlayerData
                    {
                        Id = $"player:{steamId}",
                        SteamId64 = steamId,
                        RawSteamId = steam2,
                        DisplayName = text,
                        ChatTag = text,
                        ScoreboardTag = TagService.StripOuterBrackets(text),
                        ChatTagColor = "gold",
                        NameColor = "gold",
                        Priority = 1000,
                        Enabled = true
                    };
                    _config.Players.Add(pData);
                    SaveAndApply();
                    _service.Reply(adm, $"[lime]{displayName}[default] oyuncusuna [gold]{text}[default] özel etiketi verildi!");
                    if (_sessions.TryGetValue(adm.Slot, out var s))
                        s.Push(_ => BuildEntryEditPage(pData, $"Özel: {displayName}"));
                })));
        }

        // Her grup icin uyelik toggle'i
        foreach (var g in _config.Groups)
        {
            var group = g;
            bool isMember = group.Members.Contains(steamId);
            rows.Add(new Row(
                Label: $"Grup Üyeliği: {group.Label} ({group.Id})",
                Value: isMember ? "ÜYE" : "DEĞİL",
                Arrows: true,
                Style: isMember ? RowStyle.On : RowStyle.Off,
                OnAdjust: (a, _) =>
                {
                    if (!group.Members.Add(steamId))
                    {
                        group.Members.Remove(steamId);
                        _service.Reply(a, $"[lime]{displayName}[default], [gold]{group.Id}[default] grubundan çıkarıldı.");
                    }
                    else
                    {
                        _service.Reply(a, $"[lime]{displayName}[default], [gold]{group.Id}[default] grubuna eklendi.");
                    }
                    SaveAndApply();
                }));
        }

        return new Page
        {
            Title = displayName,
            Subtitle = steam2,
            Rows = rows
        };
    }

    private Page BuildCustomPlayersPage(IPlayer admin)
    {
        var rows = new List<Row>();
        foreach (var p in _config.Players.ToList())
        {
            var entry = p;
            var label = !string.IsNullOrWhiteSpace(entry.DisplayName) ? $"{entry.DisplayName} ({SteamIdHelper.ToSteam2(entry.SteamId64)})" : SteamIdHelper.ToSteam2(entry.SteamId64);
            rows.Add(new Row(
                Label: label,
                Value: entry.ChatTag ?? "-",
                OnSelect: a => _sessions[a.Slot].Push(_ => BuildEntryEditPage(entry, label, onDelete: () =>
                {
                    _config.Players.Remove(entry);
                    SaveAndApply();
                }))));
        }

        if (rows.Count == 0)
            rows.Add(new Row("Kayıtlı özel oyuncu yok", Style: RowStyle.Muted));

        return new Page
        {
            Title = "Özel Oyuncu Etiketleri",
            Subtitle = "Düzenlemek için seçin",
            Rows = rows
        };
    }

    private Page BuildGroupsListPage(IPlayer admin)
    {
        var rows = new List<Row>
        {
            new("➕ Yeni Grup Oluştur", Style: RowStyle.Accent, OnSelect: StartCreateGroupFlow)
        };

        foreach (var g in _config.Groups.OrderByDescending(x => x.Priority).ToList())
        {
            var group = g;
            rows.Add(new Row(
                Label: $"{group.Id} ({group.ChatTag ?? "-"})",
                Value: $"Öncelik: {group.Priority} | Üye: {group.Members.Count}",
                Style: group.Enabled ? RowStyle.Normal : RowStyle.Off,
                OnSelect: a => _sessions[a.Slot].Push(_ => BuildEntryEditPage(group, $"Grup: {group.Id}", onDelete: () =>
                {
                    _config.Groups.Remove(group);
                    SaveAndApply();
                }))));
        }

        return new Page
        {
            Title = "Yetki Grupları",
            Subtitle = "Öncelik sırasına göre listelenir",
            Rows = rows
        };
    }

    private void StartCreateGroupFlow(IPlayer admin)
    {
        PromptInput(admin, "Yeni grubun ID'sini yazın (örn: mod, rehber, supervip):", (adm, rawId) =>
        {
            var id = rawId.Trim().ToLowerInvariant().Replace(' ', '_');
            if (string.IsNullOrWhiteSpace(id) || _config.Groups.Any(g => g.Id.Equals(id, StringComparison.OrdinalIgnoreCase)))
            {
                _service.Reply(adm, $"[red]'{id}' geçersiz veya zaten mevcut.[default]");
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
            _service.Reply(adm, $"[lime]'{id}'[default] grubu oluşturuldu! Şimdi detaylarını düzenleyebilirsiniz.");
            if (_sessions.TryGetValue(adm.Slot, out var s))
                s.Push(_ => BuildEntryEditPage(group, $"Grup: {group.Id}", onDelete: () =>
                {
                    _config.Groups.Remove(group);
                    SaveAndApply();
                }));
        });
    }

    private Page BuildEntryEditPage(TagEntry entry, string title, Action? onDelete = null)
    {
        var rows = new List<Row>
        {
            new("Durum", entry.Enabled ? "AKTİF" : "KAPALI", Arrows: true,
                Style: entry.Enabled ? RowStyle.On : RowStyle.Off,
                OnAdjust: (_, _) => { entry.Enabled = !entry.Enabled; SaveAndApply(); }),

            new("💬 Chat Etiketi (Yazmak için E)", entry.ChatTag ?? "(boş)", Style: RowStyle.Accent,
                OnSelect: a => PromptInput(a, "Yeni Chat Etiketini yazın (örn: [VIP]):", (adm, val) =>
                {
                    entry.ChatTag = val;
                    SaveAndApply();
                    _service.Reply(adm, $"Chat etiketi [lime]{val}[default] olarak güncellendi.");
                })),

            new("📋 Scoreboard / Clan Tag (Yazmak için E)", string.IsNullOrEmpty(entry.ScoreboardTag) ? "(boş)" : entry.ScoreboardTag, Style: RowStyle.Accent,
                OnSelect: a => PromptInput(a, "Yeni Scoreboard Etiketini yazın (boş bırakmak için - yazın):", (adm, val) =>
                {
                    entry.ScoreboardTag = val == "-" ? "" : TagService.StripOuterBrackets(val);
                    SaveAndApply();
                    _service.Reply(adm, $"Scoreboard etiketi [lime]{entry.ScoreboardTag}[default] olarak güncellendi.");
                })),

            BuildFrameRow("👑 Çerçeve / Sembol", () => entry.Frame, v => { entry.Frame = v; SaveAndApply(); }),

            BuildColorRow("🎨 Etiket Rengi", () => entry.ChatTagColor ?? "gold", v => { entry.ChatTagColor = v; SaveAndApply(); }),
            BuildColorRow("🎨 İsim Rengi", () => entry.NameColor ?? "teamcolor", v => { entry.NameColor = v; SaveAndApply(); }),
            BuildColorRow("🎨 Mesaj Rengi", () => entry.MessageColor ?? "default", v => { entry.MessageColor = v; SaveAndApply(); }),

            new("🔢 Öncelik (Priority)", entry.Priority.ToString(), Arrows: true, Style: RowStyle.Accent,
                OnAdjust: (_, dir) => { entry.Priority += dir * 10; SaveAndApply(); },
                OnSelect: a => PromptInput(a, "Öncelik sayısını yazın (örn: 100):", (adm, val) =>
                {
                    if (int.TryParse(val, out var p)) { entry.Priority = p; SaveAndApply(); }
                }))
        };

        if (entry is GroupData g)
        {
            var perms = g.Permissions.Count > 0 ? string.Join(", ", g.Permissions) : "(yok)";
            rows.Add(new Row("🔑 Yetkiler (Yazmak için E)", perms, Style: RowStyle.Accent,
                OnSelect: a => PromptInput(a, "Yetki anahtarını yazın (birden fazlaysa virgülle ayırın):", (adm, val) =>
                {
                    g.Permissions = val.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
                    SaveAndApply();
                    _service.Reply(adm, $"Grup yetkileri güncellendi: [lime]{string.Join(", ", g.Permissions)}[default]");
                })));
        }

        if (entry != _config.Default)
        {
            int curTeamIdx = GetTeamPresetIndex(entry.Teams);
            rows.Add(new Row("🚩 Takım Kısıtlaması", TeamPresetLabels[curTeamIdx], Arrows: true,
                OnAdjust: (_, dir) =>
                {
                    int next = ((curTeamIdx + dir) % TeamPresets.Length + TeamPresets.Length) % TeamPresets.Length;
                    entry.Teams = TeamPresets[next].ToList();
                    SaveAndApply();
                }));
        }

        if (onDelete != null)
        {
            rows.Add(new Row("🗑️ Bu Kaydı Sil", Style: RowStyle.Danger,
                OnSelect: a =>
                {
                    onDelete();
                    _service.Reply(a, "[red]Kayıt silindi.[default]");
                    if (_sessions.TryGetValue(a.Slot, out var s)) s.Back();
                }));
        }

        return new Page
        {
            Title = title,
            Subtitle = $"Chat: {entry.ChatTag ?? "-"} | Clan: {entry.ScoreboardTag ?? "-"}",
            Rows = rows
        };
    }

    private Page BuildSettingsPage(IPlayer admin)
    {
        var s = _config.Settings;
        var c = _config.Chat;
        return new Page
        {
            Title = "Genel Ayarlar",
            Subtitle = "Değişiklikler anında kaydedilir",
            Rows =
            {
                new Row("Scoreboard (Clan Tag)", s.EnableScoreboardTag ? "AÇIK" : "KAPALI", Arrows: true,
                    Style: s.EnableScoreboardTag ? RowStyle.On : RowStyle.Off,
                    OnAdjust: (_, _) =>
                    {
                        s.EnableScoreboardTag = !s.EnableScoreboardTag;
                        SaveAndApply();
                        if (!s.EnableScoreboardTag) _service.RestoreScoreboardAll();
                    }),
                new Row("Chat Formatlama", s.EnableChatTag ? "AÇIK" : "KAPALI", Arrows: true,
                    Style: s.EnableChatTag ? RowStyle.On : RowStyle.Off,
                    OnAdjust: (_, _) => { s.EnableChatTag = !s.EnableChatTag; SaveAndApply(); }),
                new Row("Yetki Sistemi (use_permissions)", s.UsePermissions ? "AÇIK" : "KAPALI", Arrows: true,
                    Style: s.UsePermissions ? RowStyle.On : RowStyle.Off,
                    OnAdjust: (_, _) => { s.UsePermissions = !s.UsePermissions; SaveAndApply(); }),
                new Row("Ölüler Sadece Ölülere Yazsın", c.DeadTalkOnlyToDead ? "AÇIK" : "KAPALI", Arrows: true,
                    Style: c.DeadTalkOnlyToDead ? RowStyle.On : RowStyle.Off,
                    OnAdjust: (_, _) => { c.DeadTalkOnlyToDead = !c.DeadTalkOnlyToDead; SaveAndApply(); }),
                new Row("Botlara Etiket Uygula", s.ApplyToBots ? "AÇIK" : "KAPALI", Arrows: true,
                    Style: s.ApplyToBots ? RowStyle.On : RowStyle.Off,
                    OnAdjust: (_, _) => { s.ApplyToBots = !s.ApplyToBots; SaveAndApply(); })
            }
        };
    }

    private static Row BuildColorRow(string label, Func<string> get, Action<string> set)
    {
        var cur = get();
        int idx = Array.FindIndex(ColorCycle, c => c.Equals(cur, StringComparison.OrdinalIgnoreCase));
        if (idx < 0) idx = 0;

        return new Row(label, ColorCycle[idx], Arrows: true, Style: RowStyle.Accent,
            OnAdjust: (_, dir) =>
            {
                int next = ((idx + dir) % ColorCycle.Length + ColorCycle.Length) % ColorCycle.Length;
                set(ColorCycle[next]);
            });
    }

    private static Row BuildFrameRow(string label, Func<string?> get, Action<string?> set)
    {
        var cur = get() ?? "none";
        int idx = Array.FindIndex(FrameCycle, f => f.Equals(cur, StringComparison.OrdinalIgnoreCase));
        if (idx < 0) idx = 0;

        var display = PlaceholderHelper.AvailableFrames[idx].Name;
        return new Row(label, display, Arrows: true, Style: RowStyle.Accent,
            OnAdjust: (_, dir) =>
            {
                int next = ((idx + dir) % FrameCycle.Length + FrameCycle.Length) % FrameCycle.Length;
                set(FrameCycle[next] == "none" ? null : FrameCycle[next]);
            });
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

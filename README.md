# 🏷️ TagSystem (SwiftlyS2 CS2 Scoreboard & Chat Etiket Sistemi)

Counter-Strike 2 (CS2) ve **SwiftlyS2 (.NET 10 / v1.5.0+)** altyapısı için geliştirilmiş, yüksek performanslı, TOML tabanlı, öncelik hiyerarşili, otomatik Scoreboard (Clan Tag) ve zengin Chat etiket/formatlama eklentisidir.

---

## ✨ Özellikler

- **🎯 Tam Otomatik Etiket Atama:**
  - Oyuncuların komutla seçim yapmasına gerek kalmaz.
  - SteamID, yetki grubu (`permissions`) veya varsayılan (`[default]`) tanımlara göre en yüksek önceliğe (`priority`) sahip etiket otomatik belirlenir ve anında uygulanır.
  - Takım bazlı filtreleme (`teams = ["CT"]`, `teams = ["T"]`) desteği.

- **🏆 Scoreboard (Clan Tag) & Canlı Senkronizasyon:**
  - `CCSPlayerController.Clan` ve `ClanUpdated()` üzerinden CS2 TAB skor tablosunda anında clan tag güncellemesi.
  - Oyuncunun orijinal Steam grup clan tag'i hafızada saklanır ve eklenti kapandığında (`Unload`) otomatik geri yüklenir.
  - UTF-8 31-byte sınırı motor seviyesinde korunur; Türkçe karakterler (`Ö`, `Ü`, `Ş`, `İ`, `Ğ`, `Ç`) güvenle kesilir ve buffer taşması önlenir.

- **💬 Gelişmiş Chat Formatlama:**
  - Takım etiketleri (`(T)`, `(CT)`, `(İzleyici)`), ölü durumu (`*ÖLÜ*`), etiket rengi, isim rengi ve mesaj rengi birbirinden bağımsız özelleştirilebilir.
  - Ölülerin yalnızca ölülere konuşması seçeneği (`dead_talk_only_to_dead`).
  - Sessiz komut filtreleme (`/tagmenu`, `/ws`) ve açık komut desteği.

- **🎨 Zengin Renk Paleti, Gradyanlar ve Hex Dönüşümü:**
  - **20 Standart CS2 Rengi:** `gold`, `red`, `lightred`, `darkred`, `blue`, `lightblue`, `darkblue`, `lime`, `green`, `olive`, `yellow`, `lightyellow`, `orange`, `purple`, `lightpurple`, `magenta`, `silver`, `grey`, `white`, `bluegrey`.
  - **19 Zengin Çok Renkli Gradyan:** `rainbow`, `fire`, `ice`, `neon`, `sunset`, `toxic`, `police`, `blood`, `royal`, `matrix`, `pastel`, `galaxy`, `cyberpunk`, `lava`, `aurora`, `ghost`, `nature`, `candy`, `goldrush`.
  - **6 İki Renkli Dama/Zebra Deseni:** `redblue`, `pinkblue`, `goldblack`, `greenyellow`, `blackwhite`, `redwhite`.
  - **Hex Desteği:** `#RRGGBB` hex kodları en yakın CS2 sohbet rengine ağırlıklı Öklid (redmean) formülüyle otomatik eşleştirilir.
  - Türkçe renk takma adları (`kirmizi`, `acikkirmizi`, `altin`, `gumus`, `yesil`, `mavi` vb.).

- **✨ Estetik Çerçeveler ve Semboller:**
  - Etiketlerin etrafına otomatik çift taraflı sembol ekleme: Taç (`crown` / `tac`), Alev (`flame` / `alev`), Kanat (`wings` / `kanat`), Yıldız (`star` / `yildiz`), Elmas (`diamond` / `elmas`), Şimşek (`lightning` / `simsek`), Işıltı (`sparkle` / `parilti`), Kılıç (`sword` / `kilic`), Kalkan (`shield` / `kalkan`), Kalp (`heart` / `kalp`), Haç (`cross` / `hac`), Kuru Kafa (`skull` / `kurukafa`).

- **🎮 Çift Arayüz Mimarisi (Panorama HUD & CenterHTML):**
  - **Panorama HUD Desteği:** Sunucuda `PanoramaHud` eklentisi mevcutsa modern CS2 Panorama UI kartları, canlı renk paleti, çerçeve seçici, oyuncu atama sihirbazı, canlı sohbet önizleme penceresi ve onay diyalogları.
  - **Klasik CenterHTML Fallback:** Panorama HUD yüklü olmadığında devreye giren ekran ortası `W/S/A/D/E/R` tuş navigasyonlu görsel admin menüsü.

- **🛡️ Güvenlik ve Performans Optimizasyonları:**
  - **Color Injection Engeli:** Oyuncuların sohbet mesajlarına veya isimlerine renk kodu ekleyerek suistimal etmesi engellenir.
  - **Reliable Buffer Overflow Koruması:** `ClanUpdated()` çağrıları slot bazında dedüplike edilir; oyuncu zaten aynı clan tag'e sahipse ağ paketi gönderilmez. CS2 istemcisi ile ping-pong döngüsü ve sunucudan atılma sorunları tamamen çözülmüştür.
  - **Sıfır Sunucu Lagı:** Sunucudaki tüm oyuncuların tuş hareketlerini dinleyen hook'lar kaldırılmış, yalnızca menüsü açık olan adminler için güvenli slot sorgusu yapılmıştır.
  - **Slot Bazlı TTL Önbellekleme:** Oyuncu yetki ve etiket çözümlemeleri 10 saniye süreyle önbelleğe alınır.

---

## 🕹️ Komutlar

| Komut | Alternatifler | Varsayılan Yetki | Açıklama |
| :--- | :--- | :--- | :--- |
| `!tagmenu` | `!tagsmenu`, `!etiketmenu` | `tagsystem.admin` | Görsel admin yönetim menüsünü açar (Panorama HUD veya CenterHTML). |
| `!tagver <hedef> <grup_id>` | - | `tagsystem.admin` | Oyuncuyu yetki grubuna ekler veya gruptan çıkarır. |
| `!ozeltag <hedef> <etiket> [renk] [isim_rengi] [öncelik]` | - | `tagsystem.admin` | Oyuncuya SteamID bazlı kalıcı özel etiket tanımlar. |
| `!tagsil <hedef> [grup_id]` | - | `tagsystem.admin` | Oyuncunun özel etiketini veya grup üyeliğini siler. |
| `!grupekle <id> <yetki> <etiket> [renk] [öncelik]` | - | `tagsystem.admin` | Yeni yetki grubu ekler ve config'e kaydeder. |
| `!tagcheck [hedef]` | - | `tagsystem.admin` | Bir oyuncunun aktif etiketini, grubunu, renklerini ve önceliğini inceler. |
| `!tagreload` | - | `tagsystem.admin` | `tagconfig.toml` dosyasını sunucuyu yeniden başlatmadan yükler. |

> [!NOTE]
> `<hedef>` parametresi oyuncu ismi, `#slot` numarası veya herhangi bir SteamID formatı (`STEAM_0:1:...`, `[U:1:...]`, `7656119...`) olabilir. Çift tırnak içinde çoklu kelimeler desteklenir (Örnek: `!ozeltag "Devrim" "[VIP OYUNCU]" gold`).

---

## 🧩 Değişkenler (Placeholders)

Sohbet formatında (`chat_format`) veya etiketlerde aşağıdaki değişkenleri kullanabilirsiniz:

| Değişken | Açıklama | Örnek Çıktı |
| :--- | :--- | :--- |
| `{NAME}` | Oyuncunun görünen adı (renk injection korumalı) | `Devrim` |
| `{CHAT_TAG}` | Çözümlenmiş renkli sohbet etiketi | `[ADMIN]` |
| `{SCOREBOARD_TAG}` | Çözümlenmiş clan tag metni | `ADMIN` |
| `{NAME_COLOR}` | Oyuncu isminin rengi | `[gold]` |
| `{MESSAGE_COLOR}` | Sohbet mesajı rengi | `[default]` |
| `{DEAD}` | Oyuncu ölü ise `dead_prefix` metni | `*ÖLÜ* ` |
| `{TEAMCHAT}` | Takım içi konuşma ise takım ön eki | `(CT) ` |
| `{STEAMID64}` | 64-bit Steam ID | `76561198000000000` |
| `{STEAMID2}` | Klasik Steam ID formatı | `STEAM_0:0:12345678` |
| `{STEAMID3}` | Steam3 formatı | `[U:1:24691356]` |
| `{TEAM}` | Oyuncunun takımı | `CT`, `T`, `SPEC` |
| `{PING}` | Oyuncunun anlık gecikme süresi | `18` |
| `{SCORE}` | Oyuncunun skoru | `45` |
| `{KILLS}` | Öldürme sayısı | `14` |
| `{DEATHS}` | Ölme sayısı | `3` |
| `{MVPS}` | MVP yıldız sayısı | `2` |
| `{TAG_ID}` | Uygulanan etiketin grup veya oyuncu ID'si | `admin` |

---

## ⚙️ Yapılandırma (`tagconfig.toml`)

Dosya konumu: `addons/swiftlys2/configs/plugins/TagSystem/tagconfig.toml`

```toml
[settings]
enable_scoreboard_tag = true
enable_chat_tag = true
use_permissions = true
cache_on_connect = true
apply_to_bots = false
scoreboard_refresh_seconds = 0.0      # 0.0 = Sadece spawn/takım değişiminde uygula (önerilen)
scoreboard_max_length = 24
freeze_while_menu_open = true
chat_format = "{DEAD}{TEAMCHAT}{CHAT_TAG} {NAME_COLOR}{NAME}[default]: {MESSAGE_COLOR}{MESSAGE}"
scoreboard_format = "{SCOREBOARD_TAG}"
prefix = "[gold][TagSystem][default]"
debug = false

[chat]
dead_prefix = "[grey]*ÖLÜ* [default]"
team_prefix_t = "[gold](T) [default]"
team_prefix_ct = "[lightblue](CT) [default]"
team_prefix_spec = "[grey](İzleyici) [default]"
ignore_prefixes = []
log_to_console = false
dead_talk_only_to_dead = false
block_empty_messages = true

[commands]
menu_command = "tagmenu"
give_group_command = "tagver"
set_custom_tag_command = "ozeltag"
remove_tag_command = "tagsil"
add_group_command = "grupekle"
reload_command = "tagreload"
check_command = "tagcheck"
admin_permission = "tagsystem.admin"

[default]
display_name = "Varsayılan"
chat_tag = "[OYUNCU]"
scoreboard_tag = ""
chat_tag_color = "grey"
name_color = "teamcolor"
message_color = "default"
use_tag_color = true
priority = -1000
enabled = true

# ----------------------------- YETKİ GRUPLARI ---------------------------------

[[groups]]
id = "vip"
display_name = "VIP"
permissions = ["vip", "tagsystem.vip"]
chat_tag = "[VIP]"
scoreboard_tag = "VIP"
chat_tag_color = "gold"
name_color = "gold"
priority = 10

[[groups]]
id = "admin"
display_name = "Admin"
permissions = ["admin", "tagsystem.admin"]
chat_tag = "[ADMIN]"
scoreboard_tag = "ADMIN"
chat_tag_color = "lightred"
name_color = "red"
message_color = "lightyellow"
priority = 100

# ----------------------------- ÖZEL OYUNCULAR ---------------------------------

[[players]]
steamid = "STEAM_0:1:12345678"
display_name = "Özel"
chat_tag = "[ÖZEL]"
scoreboard_tag = "ÖZEL"
chat_tag_color = "lightblue"
name_color = "lightblue"
priority = 1000
```

---

## 📥 Kurulum

1. `TagSystem.dll` ve bağımlılıklarını derleyin veya yayınlanan sürümü indirin.
2. CS2 sunucunuzda aşağıdaki klasörü oluşturun:
   ```text
   addons/swiftlys2/plugins/TagSystem/
   ```
3. Aşağıdaki dosyaları bu klasöre yükleyin:
   - `TagSystem.dll`
   - `TagSystem.deps.json`
   - `Tomlyn.dll`
   - `tagconfig.example.toml` dosyasını `tagconfig.toml` olarak adlandırıp yapılandırma klasörünüze koyun.
   - *(İsteğe Bağlı)* Panorama arayüzü için `PanoramaHud` eklentisini sunucunuza kurun.
4. Sunucu konsolundan eklentiyi yükleyin:
   ```text
   sw_plugins reload TagSystem
   ```

---

## 👥 Geliştiriciler & Lisans

- **Geliştiriciler:** CanDaysa, Devrim & SoulSnatcher
- **Organizasyon:** [Mazics2](https://github.com/Mazics2)
- **Framework:** [SwiftlyS2](https://github.com/swiftly-solution/swiftlys2)

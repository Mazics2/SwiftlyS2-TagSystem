# 🏷️ TagSystem (SwiftlyS2 CS2 Tag & Chat Biçimlendirme Sistemi)

Counter-Strike 2 (CS2) ve **SwiftlyS2 (.NET 10 / v1.5.0+)** altyapısı için geliştirilmiş, yüksek performanslı, TOML tabanlı, Scoreboard (Clan Tag) ve Chat Etiket/Formatlama eklentisidir.

---

## ✨ Özellikler

- **🎯 Otomatik Tag Çözümleme:**
  - Oyuncular manuel etiket seçimiyle uğraşmaz; `tagconfig.toml` içinde ayarlanan `priority` (öncelik), yetki (`permission`) ve SteamID64 tanımlarına göre etiketler otomatik atanır.
- **📊 Canlı Scoreboard (Clan Tag) Desteği:**
  - `CCSPlayerController.Clan` ve `controller.ClanUpdated()` motor entegrasyonuyla TAB skor tablosunda anında görünen klan etiketleri.
  - UTF-8 31-byte sınır koruması ve Türkçe karakter desteği.
- **💬 Gelişmiş Sohbet Formatlama & Renklendirme:**
  - `[red]`, `[gold]`, `[darkred]`, `[#RRGGBB]` renk etiketleri ve CS2 renk motoru normalizasyonu.
  - Renk enjeksiyonu koruması (oyuncuların kendi yazdığı mesajlardaki renk hilelerini engeller).
  - Takım sohbeti, ölü/canlı ayrımı ve gizli komut koruması.
- **🎛️ Oyun İçi Görsel Admin Menüsü (`!tagmenu`):**
  - Panorama CenterHTML ve `W / S / A / D / E / R` tuş navigasyonuyla çalışan görsel yönetim menüsü.
  - Oyunculara anlık etiket verme, grupları canlı düzenleme ve dosyaya atomik kaydetme.
- **🎨 Zengin Değişken Desteği:**
  - `{NAME}`, `{STEAMID2}`, `{STEAMID64}`, `{TEAM}`, `{PING}`, `{SCORE}`, `{KILLS}`, `{DEATHS}`, `{MVPS}`, `{CHAT_TAG}`, `{SCOREBOARD_TAG}` vb.

---

## 🕹️ Komutlar

| Komut | Kısayollar | Yetki | Açıklama |
| :--- | :--- | :--- | :--- |
| `!tagmenu` | `!tags` | `tagsystem.admin` | Görsel `W/S/A/D/E/R` admin etiket yönetim menüsünü açar. |
| `!tagver <hedef> <grup_id>` | `sw_tagver` | `tagsystem.admin` | Oyuncuyu belirtilen etiket grubuna atar. |
| `!ozeltag <hedef> "<sohbet>" "<tab>"` | `sw_ozeltag` | `tagsystem.admin` | Oyuncuya özel sohbet ve scoreboard etiketi tanımlar. |
| `!tagsil <hedef>` | `sw_tagsil` | `tagsystem.admin` | Oyuncunun özel etiket veya grup atamasını kaldırır. |
| `!grupekle <id> "<ad>" "<tag>"` | `sw_grupekle` | `tagsystem.admin` | Hızlıca yeni bir etiket grubu oluşturur. |
| `!tagreload` | `sw_tagreload` | `tagsystem.admin` | `tagconfig.toml` dosyasını yeniden yükler. |
| `!tagcheck [hedef]` | `sw_tagcheck` | `tagsystem.admin` | Hedef oyuncunun aktif etiket ve öncelik detaylarını gösterir. |

---

## 📥 Kurulum

1. `TagSystem.dll` dosyasını derleyin veya hazır paketi indirin.
2. CS2 sunucunuzda eklenti klasörünü oluşturun:
   ```text
   addons/swiftlys2/plugins/TagSystem/
   ```
3. Aşağıdaki dosyaları bu klasöre kopyalayın:
   - `TagSystem.dll`
   - `TagSystem.deps.json`
   - `tagconfig.example.toml` -> `tagconfig.toml` olarak adlandırın.
4. Sunucu konsolundan `sw_plugins reload TagSystem` yazın.

---

## 📄 Lisans

Bu proje **Mazics2** bünyesinde geliştirilmiştir.

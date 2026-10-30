# TagSystem - Geliştirme ve Mimari Notları (Gemini Argon / Devam Notu)

Bu belge, `TagSystem` eklentisinin mevcut mimarisini, tamamlanan özellikleri, çözülen SwiftlyS2/CS2 kısıtlamalarını ve ileride eklenebilecek geliştirmeleri özetler.

---

## 1. Proje Özeti ve Mimari

`TagSystem`, **SwiftlyS2 v1.5.0 (.NET 10.0)** üzerinde çalışan, TOML tabanlı (`Tomlyn 0.17.0`) bir **Scoreboard (Clan Tag)** ve **Chat Etiket/Formatlama** eklentisidir. Oyuncular etiket seçmez; `tagconfig.toml` içinde ayarlanan tanımlara (`[[players]]`, `[[groups]]`, `[default]`) ve `priority` (öncelik) değerine göre etiketler **tamamen otomatik** olarak uygulanır. Adminler etiketleri ister dosyadan, ister oyun içi hızlı komutlarla (`!tagver`, `!ozeltag`, `!tagsil`, `!grupekle`), ister görsel menüden (`!tagmenu`) yönetebilir.

### Klasör ve Dosya Yapısı
* **[`TagSystem.csproj`](file:///c:/Users/Devrim/Documents/Varsayılan%20Proje/TagSystem/TagSystem.csproj)**: `net10.0`, `SwiftlyS2.CS2` (1.5.0) ve `Tomlyn` (0.17.0) referansları. Build sonrası yerel test sunucusu varsa (`LocalServerPluginDir`) otomatik kopyalama hedefi içerir.
* **[`Plugin.cs`](file:///c:/Users/Devrim/Documents/Varsayılan%20Proje/TagSystem/Plugin.cs)**: Ana eklenti giriş noktası (`BasePlugin`). Yaşam döngüsü, event/hook abonelikleri, periyodik clan tag yenileme zamanlayıcısı ve admin komutları (`!tagmenu`, `!tagver`, `!ozeltag`, `!tagsil`, `!grupekle`, `!tagreload`, `!tagcheck`).
* **Config/**
  * **[`PluginConfig.cs`](file:///c:/Users/Devrim/Documents/Varsayılan%20Proje/TagSystem/Config/PluginConfig.cs)**: `Settings`, `Chat`, `Commands`, `Default`, `Groups` ve `Players` bölümlerini barındıran veri modeli.
  * **[`ConfigLoader.cs`](file:///c:/Users/Devrim/Documents/Varsayılan%20Proje/TagSystem/Config/ConfigLoader.cs)**: `tagconfig.toml` okuma (`Load`) ve atomik kaydetme (`Save`) sınıfı. Sözdizimi hatalarında eski config'in korunmasını sağlar; geçersiz SteamID veya tanınmayan renklerde `Warnings` listesi üretir.
  * **[`DefaultConfigTemplate.cs`](file:///c:/Users/Devrim/Documents/Varsayılan%20Proje/TagSystem/Config/DefaultConfigTemplate.cs)**: İlk çalıştırmada oluşturulan kapsamlı Türkçe açıklamalı varsayılan `tagconfig.toml` şablonu.
* **Models/**
  * **[`TagEntry.cs`](file:///c:/Users/Devrim/Documents/Varsayılan%20Proje/TagSystem/Models/TagEntry.cs)**: `Default`, `GroupData` ve `PlayerData` için ortak temel sınıf (`Id`, `DisplayName`, `ChatTag`, `ScoreboardTag`, `ChatTagColor`, `NameColor`, `MessageColor`, `Priority`, `Teams` vb.).
  * **[`GroupData.cs`](file:///c:/Users/Devrim/Documents/Varsayılan%20Proje/TagSystem/Models/GroupData.cs)**: Çoklu yetki (`Permissions`) ve doğrudan SteamID64 listesi (`Members`) destekleyen grup modeli.
  * **[`PlayerData.cs`](file:///c:/Users/Devrim/Documents/Varsayılan%20Proje/TagSystem/Models/PlayerData.cs)**: Herhangi bir SteamID formatından (`STEAM_0:1:...`, `[U:1:...]`, `7656119...`) normalize edilmiş `SteamId64` tutan özel oyuncu modeli.
  * **[`ResolvedTag.cs`](file:///c:/Users/Devrim/Documents/Varsayılan%20Proje/TagSystem/Models/ResolvedTag.cs)**: Bir oyuncu için `[default]` ile birleştirilmiş (merge edilmiş), kullanıma hazır nihai etiket kaydı.
* **Core/**
  * **[`TagManager.cs`](file:///c:/Users/Devrim/Documents/Varsayılan%20Proje/TagSystem/Core/TagManager.cs)**: Oyuncuya uyan tüm `[[players]]` ve `[[groups]]` kayıtlarını toplar, takım filtresi (`teams`) ve yetki (`Core.Permission.PlayerHasPermission`) kontrollerini yapar, en yüksek `priority` değerine sahip olanı seçer ve 10 saniyelik TTL ile slot bazında önbellekler.
  * **[`TagService.cs`](file:///c:/Users/Devrim/Documents/Varsayılan%20Proje/TagSystem/Core/TagService.cs)**:
    * **Scoreboard**: `CCSPlayerController.Clan` değerini günceller ve `controller.ClanUpdated()` çağırarak ağ üzerinden tüm istemcilere anında senkronize eder. Oyuncunun orijinal Steam grup clan tag'ini saklar ve eklenti kapatıldığında (`Unload`) geri yükler.
    * **Chat**: `HookClientChat` üzerinden mesajları yakalar, komut öneklerini (`!`, `/`, `.`) pas geçer, tek geçişli placeholder ve renk dönüşümü uygular, takım/ölü görünürlüğüne göre dağıtır ve `HookResult.CancelOriginal` döndürür.
  * **[`TagAdminMenu.cs`](file:///c:/Users/Devrim/Documents/Varsayılan%20Proje/TagSystem/Core/TagAdminMenu.cs)**: `Bolgeler` eklentisindeki `CenterHTML` ve `W/S/A/D/E/R` tuş navigasyonu mimarisiyle çalışan oyun içi görsel admin menüsü (`!tagmenu`). Aktif oyuncuya grup/özel etiket verme, grupları ve renkleri canlı düzenleme, sohbetten metin girişi (`TryConsumeChatInput`) ve anında `tagconfig.toml`'a kaydetme özelliklerini içerir.
* **Utils/**
  * **[`ColorHelper.cs`](file:///c:/Users/Devrim/Documents/Varsayılan%20Proje/TagSystem/Utils/ColorHelper.cs)**: SwiftlyS2 `Helper.Colored()` yalnızca küçük harfli `[red]`, `[gold]` vb. etiketleri tanıdığı için tüm renk adlarını, `[Etiket]` yazımlarını, `teamcolor` değerini ve `#RRGGBB` hex kodlarını (en yakın CS2 chat rengine ağırlıklı Öklid / redmean mesafesi ile eşleştirerek) normalize eder. Ayrıca oyuncu ismi ve mesajındaki renk etiketlerini temizleyerek **renk enjeksiyonunu (color injection)** engeller.
  * **[`PlaceholderHelper.cs`](file:///c:/Users/Devrim/Documents/Varsayılan%20Proje/TagSystem/Utils/PlaceholderHelper.cs)**: Tek geçişli (`Regex.Replace`) `{PLACEHOLDER}` değiştirici. Desteklenenler: `{NAME}`, `{STEAMID2}`, `{STEAMID3}`, `{STEAMID64}`, `{SLOT}`, `{PLAYERID}`, `{USERID}`, `{TEAM}`, `{PING}`, `{SCORE}`, `{KILLS}`, `{DEATHS}`, `{MVPS}`, `{TAG_ID}`, `{CHAT_TAG}`, `{SCOREBOARD_TAG}`, `{NAME_COLOR}`, `{MESSAGE_COLOR}`, `{MESSAGE}`, `{DEAD}`, `{TEAMCHAT}`.
  * **[`PlayerHelper.cs`](file:///c:/Users/Devrim/Documents/Varsayılan%20Proje/TagSystem/Utils/PlayerHelper.cs)**: `IPlayer` ve `CCSPlayerController` üzerinden istisna fırlatmayan güvenli veri okuyucular.
  * **[`SteamIdHelper.cs`](file:///c:/Users/Devrim/Documents/Varsayılan%20Proje/TagSystem/Utils/SteamIdHelper.cs)**: `SteamID64` <-> `SteamID2` (`STEAM_0:Y:Z`) <-> `SteamID3` (`[U:1:N]`) çift yönlü dönüştürücü.

---

## 2. Kritik Teknik Detaylar (Dokunulmaması Gereken Noktalar)

1. **`controller.ClanUpdated()`**: CS2'de yalnızca `controller.Clan = ...` ataması yapmak tabloya (TAB) yansımaz; mutlaka ardından `controller.ClanUpdated()` çağrılmalıdır.
2. **UTF-8 31 Byte Sınırı**: `m_szClan` alanı motor tarafında 32 byte'tır. Türkçe karakterler (`Ö`, `Ü`, `Ş`, `İ`, `Ğ`, `Ç`) UTF-8'de 2 byte yer kapladığı için `TagService.TruncateUtf8` hem karakter sayısını hem de UTF-8 byte uzunluğunu (max 31 byte) kontrol ederek keser.
3. **`Helper.Colored` Büyük/Küçük Harf Duyarlılığı**: SwiftlyS2'nin `Helper.Colored(string)` metodu `[Red]` veya `[GOLD]` gibi büyük harfli etiketleri çevirmez; yalnızca küçük harfli (`[red]`, `[gold]`, `[default]`) etiketleri `\x07` vb. CS2 kontrol byte'larına dönüştürür. `ColorHelper.NormalizeTags` bu yüzden her zaman `Helper.Colored` öncesinde çalışmalıdır.
4. **Thread Safety**: `OnClientPutInServer`, `OnClientSteamAuthorize`, `EventPlayerSpawn` ve `EventPlayerTeam` içinde entity/schema (`Clan`) erişimi `Core.Scheduler.NextTick(...)` içine alınmıştır.

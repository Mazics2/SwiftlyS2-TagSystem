namespace TagSystem.Models;

/// <summary>
/// Bir etiket tanimi. Default, grup ve oyuncu tanimlarinin ortak alanlari.
/// null olan alanlar cozumleme sirasinda [default] tanimindan doldurulur.
/// </summary>
public class TagEntry
{
    /// <summary>Benzersiz kimlik (grup id'si, oyuncu icin "player:STEAMID64", default icin "default").</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Bilgi/kontrol komutlarinda gosterilecek okunur isim. Bos ise chat_tag kullanilir.</summary>
    public string? DisplayName { get; set; }

    public string? ChatTag { get; set; }
    public string? ScoreboardTag { get; set; }

    /// <summary>Opsiyonel estetik cerceve/sembol (crown, flame, wings, star, diamond vb.).</summary>
    public string? Frame { get; set; }

    // Renklendirme (isim: "gold", etiket: "[gold]", hex: "#FFD700" -> en yakin chat rengi, "teamcolor")
    public string? ChatTagColor { get; set; }
    public string? NameColor { get; set; }
    public string? MessageColor { get; set; }

    /// <summary>Eski config uyumlulugu icin okunur; clan tag renk desteklemedigi icin kullanilmaz.</summary>
    public string? ScoreboardTagColor { get; set; }

    public bool? UseTagColor { get; set; }

    public string? ChatFormat { get; set; }
    public string? ScoreboardFormat { get; set; }

    public int Priority { get; set; }
    public bool Enabled { get; set; } = true;

    /// <summary>Opsiyonel takim filtresi: bos ise her takimda gecerli. Ornek: ["CT"], ["T","CT"].</summary>
    public List<string> Teams { get; set; } = new();

    public string Label => !string.IsNullOrWhiteSpace(DisplayName) ? DisplayName! : (ChatTag ?? ScoreboardTag ?? Id);

    public bool MatchesTeam(string team) =>
        Teams.Count == 0 || Teams.Any(t => t.Equals(team, StringComparison.OrdinalIgnoreCase));
}

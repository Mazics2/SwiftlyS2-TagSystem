using System.Globalization;
using System.Text.RegularExpressions;

namespace TagSystem.Utils;

/// <summary>
/// CS2 chat yalnizca sabit bir renk paletini destekler (hex desteklemez).
/// SwiftlyS2'nin Helper.Colored() fonksiyonu sadece KUCUK HARFLI [red], [gold] ... etiketlerini cevirir.
/// Bu sinif:
///  - Config'teki renk degerlerini (isim / hex / [etiket]) gecerli bir SwiftlyS2 renk etiketine cevirir,
///  - Hex renkleri en yakin chat rengine eslestirir,
///  - Metin icindeki [Red], [GOLD] gibi etiketleri kucuk harfe normalize eder,
///  - Oyuncu girdisindeki renk etiketlerini ve kontrol karakterlerini temizler (renk enjeksiyonu engeli).
/// </summary>
public static class ColorHelper
{
    /// <summary>SwiftlyS2 Helper.ChatColors ile birebir ayni isimler (kucuk harf) + yaklasik RGB degerleri.</summary>
    private static readonly (string Name, int R, int G, int B)[] Palette =
    {
        ("white",       255, 255, 255),
        ("darkred",     255,   0,   0),
        ("green",        64, 255,  64),
        ("lightyellow", 255, 255, 153),
        ("lightblue",   153, 204, 255),
        ("olive",       190, 255, 144),
        ("lime",        162, 255,  71),
        ("red",         255,  64,  64),
        ("lightpurple", 186, 129, 240),
        ("purple",      136,  71, 255),
        ("grey",        197, 202, 208),
        ("yellow",      237, 228, 122),
        ("gold",        228, 174,  57),
        ("silver",      176, 195, 217),
        ("blue",         94, 152, 217),
        ("darkblue",     75, 105, 255),
        ("bluegrey",    100, 120, 140),
        ("magenta",     211,  44, 230),
        ("lightred",    235,  75,  75),
        ("orange",      255, 156,   0),
    };

    /// <summary>Kullanicilarin yazabilecegi ek takma adlar (Turkce ve alternatif isimler).</summary>
    private static readonly Dictionary<string, string> Aliases = new(StringComparer.OrdinalIgnoreCase)
    {
        // Default / Sifirlama
        { "default", "default" },
        { "/", "default" },
        { "reset", "default" },
        { "varsayilan", "default" },
        { "normal", "default" },

        // Gri tonlari
        { "gray", "grey" },
        { "gri", "grey" },
        { "silver", "silver" },
        { "gumus", "silver" },
        { "platin", "silver" },
        { "platinum", "silver" },
        { "bluegrey", "bluegrey" },
        { "mavigri", "bluegrey" },
        { "fume", "bluegrey" },
        { "antrasit", "bluegrey" },
        { "charcoal", "bluegrey" },
        { "kursuni", "bluegrey" },
        { "siyah", "grey" },
        { "black", "grey" },

        // Beyaz
        { "white", "white" },
        { "beyaz", "white" },
        { "kar", "white" },
        { "buzbeyazi", "white" },

        // Kirmizi tonlari
        { "red", "red" },
        { "kirmizi", "red" },
        { "yakut", "red" },
        { "ruby", "red" },
        { "lightred", "lightred" },
        { "acikkirmizi", "lightred" },
        { "coral", "lightred" },
        { "mercan", "lightred" },
        { "darkred", "darkred" },
        { "koyukirmizi", "darkred" },
        { "bordo", "darkred" },
        { "maroon", "darkred" },
        { "burgundy", "darkred" },
        { "visne", "darkred" },
        { "visnecurugu", "darkred" },
        { "cherry", "darkred" },
        { "kor", "darkred" },

        // Mavi tonlari
        { "blue", "blue" },
        { "mavi", "blue" },
        { "safir", "blue" },
        { "sapphire", "blue" },
        { "lightblue", "lightblue" },
        { "acikmavi", "lightblue" },
        { "turkuaz", "lightblue" },
        { "turquoise", "lightblue" },
        { "cyan", "lightblue" },
        { "aqua", "lightblue" },
        { "camgobegi", "lightblue" },
        { "gokyuzu", "lightblue" },
        { "sky", "lightblue" },
        { "teal", "lightblue" },
        { "darkblue", "darkblue" },
        { "koyumavi", "darkblue" },
        { "lacivert", "darkblue" },
        { "navy", "darkblue" },
        { "gecemavisi", "darkblue" },

        // Yesil tonlari
        { "green", "green" },
        { "yesil", "green" },
        { "koyuyesil", "green" },
        { "zumrut", "green" },
        { "emerald", "green" },
        { "lime", "lime" },
        { "acikyesil", "lime" },
        { "fistik", "lime" },
        { "fistikyesili", "lime" },
        { "limon", "lime" },
        { "lemon", "lime" },
        { "olive", "olive" },
        { "zeytin", "olive" },
        { "zeytinyesili", "olive" },

        // Sari & Altin tonlari
        { "yellow", "yellow" },
        { "sari", "yellow" },
        { "hardal", "yellow" },
        { "mustard", "yellow" },
        { "lightyellow", "lightyellow" },
        { "aciksari", "lightyellow" },
        { "bej", "lightyellow" },
        { "krem", "lightyellow" },
        { "cream", "lightyellow" },
        { "gold", "gold" },
        { "altin", "gold" },
        { "altinsarisi", "gold" },
        { "bronz", "gold" },
        { "bronze", "gold" },

        // Turuncu tonlari
        { "orange", "orange" },
        { "turuncu", "orange" },
        { "kahverengi", "orange" },
        { "brown", "orange" },
        { "tarcin", "orange" },
        { "kehribar", "orange" },
        { "amber", "orange" },
        { "kavunici", "orange" },
        { "peach", "orange" },

        // Mor & Pembe tonlari
        { "purple", "purple" },
        { "mor", "purple" },
        { "koyumor", "purple" },
        { "violet", "purple" },
        { "lightpurple", "lightpurple" },
        { "acikmor", "lightpurple" },
        { "lila", "lightpurple" },
        { "lavanta", "lightpurple" },
        { "lavender", "lightpurple" },
        { "eflatun", "lightpurple" },
        { "magenta", "magenta" },
        { "pembe", "magenta" },
        { "fusya", "magenta" },
        { "pink", "magenta" },

        // Takim bazli dinamik renkler
        { "teamcolor", "teamcolor" },
        { "team", "teamcolor" },
        { "takim", "teamcolor" },
        { "takimrengi", "teamcolor" },
        { "tcolor", "gold" },
        { "trengi", "gold" },
        { "ctcolor", "lightblue" },
        { "ctrengi", "lightblue" },
        { "speccolor", "grey" },
    };

    /// <summary>Cok renkli gecis / gradyan efektleri sozlugu (CS2 renk kodlari dizisi).</summary>
    public static readonly Dictionary<string, string[]> GradientPalettes = new(StringComparer.OrdinalIgnoreCase)
    {
        { "rainbow",    new[] { "red", "orange", "yellow", "lime", "lightblue", "blue", "purple", "magenta" } },
        { "gokkusagi",  new[] { "red", "orange", "yellow", "lime", "lightblue", "blue", "purple", "magenta" } },
        { "fire",       new[] { "darkred", "red", "lightred", "orange", "yellow", "lightyellow" } },
        { "ates",       new[] { "darkred", "red", "lightred", "orange", "yellow", "lightyellow" } },
        { "ice",        new[] { "darkblue", "blue", "lightblue", "silver", "white" } },
        { "buz",        new[] { "darkblue", "blue", "lightblue", "silver", "white" } },
        { "neon",       new[] { "lime", "lightblue", "magenta", "lightpurple" } },
        { "sunset",     new[] { "purple", "lightpurple", "magenta", "lightred", "orange", "gold" } },
        { "gunbatimi",  new[] { "purple", "lightpurple", "magenta", "lightred", "orange", "gold" } },
        { "toxic",      new[] { "green", "olive", "lime", "lightyellow" } },
        { "zehir",      new[] { "green", "olive", "lime", "lightyellow" } },
        { "police",     new[] { "red", "lightred", "white", "blue", "lightblue" } },
        { "polis",      new[] { "red", "lightred", "white", "blue", "lightblue" } },
        { "blood",      new[] { "darkred", "red", "lightred" } },
        { "kan",        new[] { "darkred", "red", "lightred" } },
        { "royal",      new[] { "gold", "yellow", "purple", "lightpurple" } },
        { "kraliyet",   new[] { "gold", "yellow", "purple", "lightpurple" } },
        { "matrix",     new[] { "green", "lime", "olive" } },
        { "pastel",     new[] { "lightyellow", "lightblue", "lightpurple", "lightred" } },
        // Yeni Tematik Gradyanlar
        { "galaxy",     new[] { "darkblue", "purple", "lightpurple", "silver", "white" } },
        { "galaksi",    new[] { "darkblue", "purple", "lightpurple", "silver", "white" } },
        { "cyberpunk",  new[] { "yellow", "lightred", "magenta", "lightblue", "lime" } },
        { "siberpunk",  new[] { "yellow", "lightred", "magenta", "lightblue", "lime" } },
        { "lava",       new[] { "darkred", "red", "orange", "gold" } },
        { "lav",        new[] { "darkred", "red", "orange", "gold" } },
        { "aurora",     new[] { "green", "lime", "lightblue", "purple" } },
        { "kuzeyisiklari", new[] { "green", "lime", "lightblue", "purple" } },
        { "ghost",      new[] { "grey", "bluegrey", "silver", "white" } },
        { "hayalet",    new[] { "grey", "bluegrey", "silver", "white" } },
        { "nature",     new[] { "green", "olive", "lime", "lightyellow" } },
        { "doga",       new[] { "green", "olive", "lime", "lightyellow" } },
        { "candy",      new[] { "magenta", "lightpurple", "lightblue", "white" } },
        { "seker",      new[] { "magenta", "lightpurple", "lightblue", "white" } },
        { "goldrush",   new[] { "olive", "gold", "yellow", "lightyellow", "white" } },
        { "altinhirsizi", new[] { "olive", "gold", "yellow", "lightyellow", "white" } },
        // Cift Renkli (Dama / Two-Tone) Desenler
        { "redblue",    new[] { "red", "blue" } },
        { "kirmizimavi", new[] { "red", "blue" } },
        { "pinkblue",   new[] { "magenta", "lightblue" } },
        { "pembemavi",  new[] { "magenta", "lightblue" } },
        { "goldblack",  new[] { "gold", "grey" } },
        { "altingri",   new[] { "gold", "grey" } },
        { "greenyellow", new[] { "lime", "yellow" } },
        { "yesilsari",  new[] { "lime", "yellow" } },
        { "blackwhite", new[] { "grey", "white" } },
        { "siyahbeyaz", new[] { "grey", "white" } },
        { "redwhite",   new[] { "red", "white" } },
        { "kirmizibeyaz", new[] { "red", "white" } },
    };

    /// <summary>UI ve menu listelerinde gosterilecek ana efekt adlari.</summary>
    public static readonly string[] PrimaryGradients =
    {
        "rainbow", "fire", "ice", "neon", "sunset", "toxic", "police", "blood", "royal", "matrix", "pastel",
        "galaxy", "cyberpunk", "lava", "aurora", "ghost", "nature", "candy", "goldrush",
        "redblue", "pinkblue", "goldblack", "greenyellow", "blackwhite", "redwhite"
    };

    /// <summary>Gokkusagi (Rainbow) siralamasindaki renkler (geriye donuk uyumluluk).</summary>
    public static readonly string[] RainbowColors = GradientPalettes["rainbow"];

    private static readonly HashSet<string> KnownTags = new(
        Palette.Select(p => p.Name).Concat(new[] { "default", "/" }), StringComparer.OrdinalIgnoreCase);

    private static readonly Regex TagRegex = new(@"\[(?<n>[a-zA-Z/]+)\]", RegexOptions.Compiled);
    private static readonly Regex HexRegex = new(@"^#?(?<h>[0-9A-Fa-f]{6}|[0-9A-Fa-f]{3})$", RegexOptions.Compiled);

    public static IReadOnlyList<string> AvailableColors =>
        Palette.Select(p => p.Name)
            .Prepend("default")
            .Append("teamcolor")
            .Concat(PrimaryGradients)
            .ToList();

    /// <summary>Verilen ismin cok renkli bir gradyan efekti olup olmadigini kontrol eder.</summary>
    public static bool IsGradient(string? name) =>
        !string.IsNullOrWhiteSpace(name) && GradientPalettes.ContainsKey(name);

    /// <summary>Verilen metnin her harfini sirayla secilen gradyan efektinin renklerine donusturur.</summary>
    public static string ApplyGradient(string text, string gradientName)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;
        if (!GradientPalettes.TryGetValue(gradientName, out var colors) || colors.Length == 0)
            return text;

        var clean = StripControlChars(text);
        var sb = new System.Text.StringBuilder(clean.Length * 14);
        int colorIdx = 0;
        foreach (char ch in clean)
        {
            if (char.IsWhiteSpace(ch))
            {
                sb.Append(ch);
                continue;
            }
            sb.Append($"[{colors[colorIdx % colors.Length]}]{ch}");
            colorIdx++;
        }
        sb.Append("[default]");
        return sb.ToString();
    }

    /// <summary>Verilen metnin her harfini sirayla gokkusagi renklerine donusturur.</summary>
    public static string ApplyRainbow(string text) => ApplyGradient(text, "rainbow");

    /// <summary>Verilen renk adinin, hex kodunun veya efektin gecerli olup olmadigini dogrular.</summary>
    public static bool IsValidColor(string? color)
    {
        if (string.IsNullOrWhiteSpace(color)) return false;
        var c = color.Trim();
        if (c.StartsWith('[') && c.EndsWith(']') && c.Length > 2) c = c[1..^1];
        if (Aliases.ContainsKey(c) || KnownTags.Contains(c) || IsGradient(c)) return true;
        return HexRegex.IsMatch(c);
    }

    /// <summary>
    /// "gold", "[gold]", "#FFD700", "FFD700", "teamcolor" gibi bir degeri "[gold]" seklinde bir chat etiketine cevirir.
    /// Gecersiz / bos deger icin bos string doner (renk uygulanmaz).
    /// </summary>
    public static string ToChatTag(string? color, string? team = null)
    {
        if (string.IsNullOrWhiteSpace(color)) return string.Empty;
        var c = color.Trim();

        if (c.StartsWith('[') && c.EndsWith(']') && c.Length > 2) c = c[1..^1];

        if (Aliases.TryGetValue(c, out var alias))
        {
            if (alias == "teamcolor") return TeamColorTag(team);
            return $"[{alias}]";
        }

        if (KnownTags.Contains(c)) return $"[{c.ToLowerInvariant()}]";

        var hex = HexRegex.Match(c);
        if (hex.Success) return $"[{NearestColor(hex.Groups["h"].Value)}]";

        return string.Empty;
    }

    /// <summary>Takima gore varsayilan isim rengi (CS2'nin kendi chat renklerine yakin).</summary>
    public static string TeamColorTag(string? team) => team switch
    {
        "T" => "[gold]",
        "CT" => "[lightblue]",
        "SPEC" => "[grey]",
        _ => "[default]"
    };

    /// <summary>Metindeki [Red], [GOLD] vb. bilinen etiketleri SwiftlyS2'nin tanidigi kucuk harfe cevirir.
    /// [VIP], [ADMIN] gibi renk olmayan koseli parantezli metinlere dokunmaz.</summary>
    public static string NormalizeTags(string? text, string? team = null)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;
        return TagRegex.Replace(text, m =>
        {
            var n = m.Groups["n"].Value;
            if (Aliases.TryGetValue(n, out var alias))
                return alias == "teamcolor" ? TeamColorTag(team) : $"[{alias}]";
            return KnownTags.Contains(n) ? $"[{n.ToLowerInvariant()}]" : m.Value;
        });
    }

    /// <summary>Verilen metnin tek basina "[GOLD]", "[SILVER]" gibi gorunur bir etiket olup olmadigini kontrol eder.</summary>
    public static bool IsSingleBracketTag(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        var trimmed = text.Trim();
        return TagRegex.Match(trimmed) is { Success: true } m && m.Value.Length == trimmed.Length;
    }

    /// <summary>Oyuncu kaynakli metinden yalnizca kontrol karakterlerini (&lt; 0x20, CS2 renk byte'lari vb.) temizler.
    /// Ozel karakterlere (!, /, ., [...], vb.) dokunmaz.</summary>
    public static string StripControlChars(string? text)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;
        return new string(text.Where(ch => ch >= 0x20 || ch == '\t').ToArray());
    }

    /// <summary>Oyuncu kaynakli metinden (isim / mesaj) renk etiketlerini ve kontrol karakterlerini temizler.</summary>
    public static string Sanitize(string? text)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;
        var noCtrl = StripControlChars(text);
        // Ic ice yazilarak atlatilmasin diye ("[re[red]d]") degisiklik kalmayana kadar tekrarla.
        string prev;
        do
        {
            prev = noCtrl;
            noCtrl = TagRegex.Replace(noCtrl, m =>
                KnownTags.Contains(m.Groups["n"].Value) || Aliases.ContainsKey(m.Groups["n"].Value) ? string.Empty : m.Value);
        } while (prev != noCtrl);
        return noCtrl;
    }

    /// <summary>Scoreboard (clan tag) renk desteklemez: tum renk etiketlerini ve kontrol karakterlerini atar.</summary>
    public static string StripAll(string? text)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;
        var noCtrl = StripControlChars(text);
        if (IsSingleBracketTag(noCtrl)) return noCtrl.Trim();
        return Sanitize(noCtrl);
    }

    private static string NearestColor(string hex)
    {
        if (hex.Length == 3) hex = string.Concat(hex.Select(ch => $"{ch}{ch}"));
        int r = int.Parse(hex[..2], NumberStyles.HexNumber);
        int g = int.Parse(hex[2..4], NumberStyles.HexNumber);
        int b = int.Parse(hex[4..6], NumberStyles.HexNumber);

        string best = "white";
        double bestDist = double.MaxValue;
        foreach (var p in Palette)
        {
            // Insan gozune daha yakin agirlikli oklid mesafesi ("redmean")
            double rm = (r + p.R) / 2.0;
            double dr = r - p.R, dg = g - p.G, db = b - p.B;
            double dist = (2 + rm / 256) * dr * dr + 4 * dg * dg + (2 + (255 - rm) / 256) * db * db;
            if (dist < bestDist) { bestDist = dist; best = p.Name; }
        }
        return best;
    }
}

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

    /// <summary>Kullanicilarin yazabilecegi ek takma adlar.</summary>
    private static readonly Dictionary<string, string> Aliases = new(StringComparer.OrdinalIgnoreCase)
    {
        { "default", "default" },
        { "/", "default" },
        { "reset", "default" },
        { "gray", "grey" },
        { "cyan", "lightblue" },
        { "pink", "magenta" },
        { "teamcolor", "teamcolor" }, // ozel: oyuncunun takimina gore cozulur
        { "team", "teamcolor" },
    };

    private static readonly HashSet<string> KnownTags = new(
        Palette.Select(p => p.Name).Concat(new[] { "default", "/" }), StringComparer.OrdinalIgnoreCase);

    private static readonly Regex TagRegex = new(@"\[(?<n>[a-zA-Z/]+)\]", RegexOptions.Compiled);
    private static readonly Regex HexRegex = new(@"^#?(?<h>[0-9A-Fa-f]{6}|[0-9A-Fa-f]{3})$", RegexOptions.Compiled);

    public static IReadOnlyList<string> AvailableColors => Palette.Select(p => p.Name).Prepend("default").ToList();

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

    /// <summary>Oyuncu kaynakli metinden (isim / mesaj) renk etiketlerini ve kontrol karakterlerini temizler.</summary>
    public static string Sanitize(string? text)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;
        var noCtrl = new string(text.Where(ch => ch >= 0x20 || ch == '\t').ToArray());
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
    public static string StripAll(string? text) => Sanitize(text);

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

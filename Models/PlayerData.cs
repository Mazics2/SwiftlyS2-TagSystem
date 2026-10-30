namespace TagSystem.Models;

/// <summary>Belirli bir oyuncuya ozel etiket. SteamID; STEAM_0:1:123, [U:1:246] veya 7656... olabilir.</summary>
public class PlayerData : TagEntry
{
    /// <summary>Normalize edilmis SteamID64. Config'te steamid / steamid2 / steamid3 / steamid64 anahtarlarindan biriyle yazilir.</summary>
    public ulong SteamId64 { get; set; }

    /// <summary>Config'te yazildigi haliyle (log / hata mesajlari icin).</summary>
    public string RawSteamId { get; set; } = string.Empty;
}

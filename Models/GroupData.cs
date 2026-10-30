namespace TagSystem.Models;

/// <summary>Yetki (permission) tabanli etiket grubu.</summary>
public class GroupData : TagEntry
{
    /// <summary>Oyuncunun bu gruba dahil olmasi icin sahip olmasi gereken yetkilerden HERHANGI BIRI.
    /// Config'te "permission_key" (tek) veya "permissions" (liste) olarak yazilabilir.</summary>
    public List<string> Permissions { get; set; } = new();

    /// <summary>Opsiyonel: bu gruba dogrudan dahil edilecek SteamID'ler (her formatta, SteamID64'e cevrilmis).</summary>
    public HashSet<ulong> Members { get; set; } = new();
}

namespace TagSystem.Models;

/// <summary>Bir oyuncu icin cozulmus, default ile birlestirilmis, kullanima hazir etiket.</summary>
public sealed class ResolvedTag
{
    public required string SourceId { get; init; }
    public required string Label { get; init; }
    public string ChatTag { get; init; } = string.Empty;
    public string ScoreboardTag { get; init; } = string.Empty;
    public string? Frame { get; init; }
    public string ChatTagColor { get; init; } = string.Empty;
    public string NameColor { get; init; } = string.Empty;
    public string MessageColor { get; init; } = string.Empty;
    public bool UseTagColor { get; init; } = true;
    public string ChatFormat { get; init; } = string.Empty;
    public string ScoreboardFormat { get; init; } = string.Empty;
    public int Priority { get; init; }
    public bool Enabled { get; init; } = true;
}

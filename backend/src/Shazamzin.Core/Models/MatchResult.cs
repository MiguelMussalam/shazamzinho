namespace Shazamzin.Core.Models;

/// <summary>
/// Resultado do matching. Serializado em snake_case (ver docs/architecture.md §5.2):
/// no match inclui hashes_received/hashes_hit; match inclui os metadados da música.
/// Campos nulos são omitidos do JSON.
/// </summary>
public sealed record MatchResult
{
    public required bool Match { get; init; }
    public required long ProcessingTimeMs { get; init; }

    // Preenchidos quando Match = true
    public int? SongId { get; init; }
    public string? Title { get; init; }
    public string? Artist { get; init; }
    public string? Album { get; init; }
    public double? Confidence { get; init; }
    public double? MatchedOffsetSec { get; init; }

    // Preenchidos quando Match = false
    public int? HashesReceived { get; init; }
    public int? HashesHit { get; init; }
}

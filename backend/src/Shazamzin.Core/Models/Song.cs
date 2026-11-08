namespace Shazamzin.Core.Models;

/// <summary>Metadados de uma música do catálogo (espelha a tabela songs do SQLite).</summary>
public sealed record Song(
    int Id,
    string Title,
    string Artist,
    string? Album = null,
    int? DurationSec = null);

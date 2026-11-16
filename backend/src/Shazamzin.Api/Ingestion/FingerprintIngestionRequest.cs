namespace Shazamzin.Api.Ingestion;

internal sealed record FingerprintIngestionRequest(
    string? FileSha256,
    string? Profile,
    string? Title,
    string? Artist,
    string? Album,
    int? DurationSec,
    FingerprintEntryRequest?[]? Fingerprints);

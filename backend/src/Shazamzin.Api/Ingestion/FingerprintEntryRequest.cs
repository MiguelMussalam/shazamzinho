namespace Shazamzin.Api.Ingestion;

internal sealed record FingerprintEntryRequest(uint? Hash, uint? TimestampMs);

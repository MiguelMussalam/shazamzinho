namespace Shazamzin.AudioCatalog;

public interface ITrackRepository
{
    Task<CatalogIndexState> GetIndexStateAsync(CancellationToken cancellationToken);

    // All persisted time offsets are measured in milliseconds.
    Task<int> AddTrackAsync(string title, string? artist, int durationMs,
        IEnumerable<(uint Hash, int Time)> hashes, CancellationToken cancellationToken);

    Task<(string Title, string? Artist)?> GetTrackAsync(int id, CancellationToken cancellationToken);

    IAsyncEnumerable<(uint Hash, int TrackId, int Time)> StreamAllFingerprintsAsync(
        CancellationToken cancellationToken);

    Task<IReadOnlyList<(int TrackId, int Time)>> FindByHashAsync(uint hash, CancellationToken cancellationToken);

    Task<IReadOnlyDictionary<uint, (int TrackId, int Time)[]>> FindByHashesAsync(
        IReadOnlyList<uint> hashes, CancellationToken cancellationToken);
}

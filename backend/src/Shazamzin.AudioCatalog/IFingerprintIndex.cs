namespace Shazamzin.AudioCatalog;

public interface IFingerprintIndex
{
    Task IndexAsync(int trackId, IEnumerable<(uint Hash, int Time)> hashes, CancellationToken cancellationToken);

    Task<IReadOnlyDictionary<uint, (int TrackId, int Time)[]>> LookupAsync(
        IReadOnlyList<uint> hashes, CancellationToken cancellationToken);
}

internal sealed class SqliteFingerprintIndex(ITrackRepository tracks) : IFingerprintIndex
{
    public Task IndexAsync(int trackId, IEnumerable<(uint Hash, int Time)> hashes, CancellationToken cancellationToken) =>
        Task.CompletedTask;

    public Task<IReadOnlyDictionary<uint, (int TrackId, int Time)[]>> LookupAsync(
        IReadOnlyList<uint> hashes, CancellationToken cancellationToken) =>
        tracks.FindByHashesAsync(hashes, cancellationToken);
}

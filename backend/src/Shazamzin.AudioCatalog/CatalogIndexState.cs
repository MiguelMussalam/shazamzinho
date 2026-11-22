using System.Globalization;

namespace Shazamzin.AudioCatalog;

public sealed record CatalogIndexState(string CatalogId, int LatestTrackId)
{
    private string Prefix => $"shazamzinho:audio:v1:{{{CatalogId}}}";
    internal string ReadyKey => $"{Prefix}:ready";
    internal string LockKey => $"{Prefix}:write-lock";
    internal string HashKey(uint hash) => string.Create(CultureInfo.InvariantCulture, $"{Prefix}:fp:{hash:x8}");
}

using Shazamzin.Core.Models;

namespace Shazamzin.Core.Interfaces;

/// <summary>Casa os fingerprints recebidos do ESP contra o catálogo e devolve a música identificada (ou no-match).</summary>
public interface IMatchingService
{
    Task<MatchResult> MatchAsync(FingerprintPayload payload, CancellationToken cancellationToken = default);
}

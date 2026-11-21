using Shazamzin.Core.Models;

namespace Shazamzin.Core.Interfaces;

/// <summary>Histórico de consultas ao /match (tabela match_log, docs/architecture.md §5.4).</summary>
public interface IMatchLog
{
    Task RecordAsync(string deviceId, int hashesReceived, MatchResult result, CancellationToken cancellationToken = default);
}

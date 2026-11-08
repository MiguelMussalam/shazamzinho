using Shazamzin.Core.Models;

namespace Shazamzin.Core.Interfaces;

/// <summary>Acesso aos metadados das músicas (SQLite).</summary>
public interface ISongRepository
{
    Task<Song?> GetByIdAsync(int id, CancellationToken cancellationToken = default);
    Task<int> CountAsync(CancellationToken cancellationToken = default);
}

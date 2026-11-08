namespace Shazamzin.Core.Models;

/// <summary>Um hash combinatorial do fingerprint e o instante (ms desde o início da captura) em que ocorreu.</summary>
public readonly record struct Fingerprint(uint Hash, uint TimestampMs);

/// <summary>Conteúdo decodificado de um POST /match: o identificador da sessão e os fingerprints enviados pelo ESP.</summary>
public sealed record FingerprintPayload(uint SessionId, IReadOnlyList<Fingerprint> Fingerprints);

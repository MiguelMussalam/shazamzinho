namespace Shazamzin.Core.Matching;

/// <summary>Uma ocorrência de um hash no catálogo: em qual música e em que instante (ms) dela.</summary>
public readonly record struct Posting(int SongId, int OffsetMs);

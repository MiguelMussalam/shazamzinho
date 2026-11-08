namespace Shazamzin.Core.Matching;

/// <summary>Seção "Matching" do appsettings.json.</summary>
public sealed class MatchingOptions
{
    public const string SectionName = "Matching";

    /// <summary>Largura do bucket do histograma de deltas, em ms.</summary>
    public int DeltaBucketMs { get; set; } = 50;

    /// <summary>Mínimo de hashes alinhados no mesmo bucket para declarar match.</summary>
    public int MinScoreThreshold { get; set; } = 8;
}

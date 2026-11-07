using FluentAssertions;
using Xunit;

namespace Shazamzin.Tests;

// Fase 0: sanity check de que o projeto de testes compila e roda.
// Testes reais (FingerprintCodec round-trip, scoring, etc.) entram nas Fases 1-2.
public class SanityTests
{
    [Fact]
    public void TestInfrastructure_Works()
    {
        var sum = 2 + 2;
        sum.Should().Be(4);
    }
}

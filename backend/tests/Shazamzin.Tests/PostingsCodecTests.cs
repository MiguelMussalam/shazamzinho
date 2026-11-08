using FluentAssertions;
using Shazamzin.Core.Matching;
using Shazamzin.Infrastructure.Valkey;
using Xunit;

namespace Shazamzin.Tests;

public class PostingsCodecTests
{
    [Fact]
    public void RoundTrip_PreservesPostings()
    {
        var postings = new[] { new Posting(42, 12_345), new Posting(7, 0), new Posting(int.MaxValue, int.MaxValue) };

        PostingsCodec.Decode(PostingsCodec.Encode(postings)).Should().Equal(postings);
    }

    [Fact]
    public void EncodeOne_WritesLittleEndianBytes()
    {
        PostingsCodec.EncodeOne(new Posting(0x04030201, 0x08070605))
            .Should().Equal(0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08);
    }

    [Fact]
    public void AppendedBlobs_DecodeAsOneList()
    {
        // é exatamente o que o APPEND do Valkey faz na ingestão
        var blob = PostingsCodec.EncodeOne(new Posting(1, 100))
            .Concat(PostingsCodec.EncodeOne(new Posting(2, 200)))
            .ToArray();

        PostingsCodec.Decode(blob).Should().Equal(new Posting(1, 100), new Posting(2, 200));
    }

    [Fact]
    public void EmptyBlob_DecodesToEmptyList() =>
        PostingsCodec.Decode(ReadOnlySpan<byte>.Empty).Should().BeEmpty();

    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(9)]
    public void RejectsBlobWhoseLengthIsNotAMultipleOfEight(int length)
    {
        var act = () => PostingsCodec.Decode(new byte[length]);

        act.Should().Throw<FormatException>();
    }

    [Theory]
    [InlineData(0xA3F29B01u, "fp:a3f29b01")]
    [InlineData(0xABCDu, "fp:0000abcd")]
    [InlineData(0u, "fp:00000000")]
    public void KeyFor_UsesEightLowercaseHexDigits(uint hash, string expected) =>
        PostingsCodec.KeyFor(hash).Should().Be(expected);
}

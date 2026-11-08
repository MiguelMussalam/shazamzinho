using FluentAssertions;
using Shazamzin.Core.Codec;
using Shazamzin.Core.Models;
using Xunit;

namespace Shazamzin.Tests;

public class FingerprintCodecTests
{
    private static FingerprintPayload Sample(int count, uint session = 0xAABBCCDD) =>
        new(session, Enumerable.Range(0, count)
            .Select(i => new Fingerprint((uint)(0xA3F29B01 + i), (uint)(i * 23)))
            .ToArray());

    [Fact]
    public void RoundTrip_PreservesSessionAndEntries()
    {
        var original = Sample(180);

        var decoded = FingerprintCodec.Decode(FingerprintCodec.Encode(original));

        decoded.SessionId.Should().Be(original.SessionId);
        decoded.Fingerprints.Should().Equal(original.Fingerprints);
    }

    [Fact]
    public void Encode_WritesLittleEndianBytes()
    {
        var payload = new FingerprintPayload(0x04030201, new[] { new Fingerprint(0x0A0B0C0D, 0x11223344) });

        var bytes = FingerprintCodec.Encode(payload);

        bytes.Should().Equal(
            1, 0,                   // version, reserved
            1, 0,                   // count = 1 (u16 LE)
            0x01, 0x02, 0x03, 0x04, // session_id (u32 LE)
            0x0D, 0x0C, 0x0B, 0x0A, // hash (u32 LE)
            0x44, 0x33, 0x22, 0x11  // timestamp_ms (u32 LE)
        );
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(65535)]
    public void RoundTrip_AtCountLimits(int count)
    {
        var bytes = FingerprintCodec.Encode(Sample(count));

        bytes.Length.Should().Be(FingerprintCodec.HeaderSize + count * FingerprintCodec.EntrySize);
        FingerprintCodec.Decode(bytes).Fingerprints.Should().HaveCount(count);
    }

    [Fact]
    public void Encode_RejectsMoreThanMaxCount()
    {
        var act = () => FingerprintCodec.Encode(Sample(FingerprintCodec.MaxCount + 1));

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Decode_RejectsShortHeader()
    {
        FingerprintCodec.TryDecode(new byte[7], out var payload, out var error).Should().BeFalse();

        payload.Should().BeNull();
        error.Should().Contain("Header incompleto");
    }

    [Fact]
    public void Decode_RejectsEmptyBody()
    {
        FingerprintCodec.TryDecode(ReadOnlySpan<byte>.Empty, out _, out var error).Should().BeFalse();

        error.Should().Contain("Header incompleto");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    [InlineData(255)]
    public void Decode_RejectsUnsupportedVersion(byte version)
    {
        var bytes = FingerprintCodec.Encode(Sample(1));
        bytes[0] = version;

        FingerprintCodec.TryDecode(bytes, out _, out var error).Should().BeFalse();

        error.Should().Contain("protocol_version");
    }

    [Fact]
    public void Decode_RejectsCountThatDoesNotMatchLength()
    {
        var bytes = FingerprintCodec.Encode(Sample(3));

        FingerprintCodec.TryDecode(bytes.AsSpan(0, bytes.Length - 1), out _, out var truncated).Should().BeFalse();
        truncated.Should().Contain("não bate");

        var padded = bytes.Concat(new byte[] { 0 }).ToArray();
        FingerprintCodec.TryDecode(padded, out _, out var extra).Should().BeFalse();
        extra.Should().Contain("não bate");
    }

    [Fact]
    public void Decode_ThrowsFormatException_OnInvalidInput()
    {
        var act = () => FingerprintCodec.Decode(new byte[3]);

        act.Should().Throw<FormatException>();
    }
}

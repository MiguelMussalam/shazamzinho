using System.Buffers.Binary;
using Shazamzin.Core.Models;

namespace Shazamzin.Core.Codec;

/// <summary>
/// Codec do formato binário ESP ↔ servidor (docs/protocol.md). Tudo little-endian.
/// <code>
/// header (8 bytes): u8 version=1 | u8 reserved=0 | u16 count | u32 session_id
/// entry  (8 bytes): u32 hash | u32 timestamp_ms
/// </code>
/// </summary>
public static class FingerprintCodec
{
    public const byte ProtocolVersion = 1;
    public const int HeaderSize = 8;
    public const int EntrySize = 8;
    public const int MaxCount = ushort.MaxValue;

    /// <summary>Maior payload válido (header + 65535 entries).</summary>
    public const int MaxPayloadSize = HeaderSize + MaxCount * EntrySize;

    public static byte[] Encode(FingerprintPayload payload)
    {
        ArgumentNullException.ThrowIfNull(payload);
        var entries = payload.Fingerprints;
        if (entries.Count > MaxCount)
            throw new ArgumentException($"Máximo de {MaxCount} fingerprints por payload (recebido {entries.Count}).", nameof(payload));

        var buffer = new byte[HeaderSize + entries.Count * EntrySize];
        buffer[0] = ProtocolVersion;
        buffer[1] = 0; // reserved
        BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(2), (ushort)entries.Count);
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(4), payload.SessionId);

        for (var i = 0; i < entries.Count; i++)
        {
            var entry = buffer.AsSpan(HeaderSize + i * EntrySize, EntrySize);
            BinaryPrimitives.WriteUInt32LittleEndian(entry, entries[i].Hash);
            BinaryPrimitives.WriteUInt32LittleEndian(entry[4..], entries[i].TimestampMs);
        }

        return buffer;
    }

    /// <summary>Decodifica ou lança <see cref="FormatException"/> com o motivo da rejeição.</summary>
    public static FingerprintPayload Decode(ReadOnlySpan<byte> data)
    {
        if (!TryDecode(data, out var payload, out var error))
            throw new FormatException(error);
        return payload!;
    }

    public static bool TryDecode(ReadOnlySpan<byte> data, out FingerprintPayload? payload, out string? error)
    {
        payload = null;

        if (data.Length < HeaderSize)
        {
            error = $"Header incompleto: {data.Length} bytes (mínimo {HeaderSize}).";
            return false;
        }

        if (data[0] != ProtocolVersion)
        {
            error = $"protocol_version {data[0]} não suportada (esperado {ProtocolVersion}).";
            return false;
        }

        var count = BinaryPrimitives.ReadUInt16LittleEndian(data[2..]);
        var sessionId = BinaryPrimitives.ReadUInt32LittleEndian(data[4..]);

        var expected = HeaderSize + count * EntrySize;
        if (data.Length != expected)
        {
            error = $"Tamanho {data.Length} não bate com count={count} (esperado {expected} bytes).";
            return false;
        }

        var list = new Fingerprint[count];
        for (var i = 0; i < count; i++)
        {
            var entry = data.Slice(HeaderSize + i * EntrySize, EntrySize);
            list[i] = new Fingerprint(
                BinaryPrimitives.ReadUInt32LittleEndian(entry),
                BinaryPrimitives.ReadUInt32LittleEndian(entry[4..]));
        }

        payload = new FingerprintPayload(sessionId, list);
        error = null;
        return true;
    }
}

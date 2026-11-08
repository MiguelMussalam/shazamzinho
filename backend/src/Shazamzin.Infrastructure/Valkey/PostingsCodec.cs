using System.Buffers.Binary;
using Shazamzin.Core.Matching;

namespace Shazamzin.Infrastructure.Valkey;

/// <summary>
/// Codec do valor de cada chave fp:{hash} (docs/valkey-schema.md): lista concatenada de
/// postings, 8 bytes cada, little-endian: song_id (int32) | offset_ms (int32).
/// A ingestão acrescenta com APPEND; a consulta lê o blob inteiro e decodifica.
/// </summary>
public static class PostingsCodec
{
    public const int PostingSize = 8;

    /// <summary>Chave do Valkey para um hash: fp:{8 dígitos hex minúsculos}.</summary>
    public static string KeyFor(uint hash) => $"fp:{hash:x8}";

    public static byte[] Encode(IReadOnlyList<Posting> postings)
    {
        var buffer = new byte[postings.Count * PostingSize];
        for (var i = 0; i < postings.Count; i++)
            Write(buffer.AsSpan(i * PostingSize, PostingSize), postings[i]);
        return buffer;
    }

    /// <summary>Um posting codificado: é o que a ingestão passa para APPEND.</summary>
    public static byte[] EncodeOne(Posting posting)
    {
        var buffer = new byte[PostingSize];
        Write(buffer, posting);
        return buffer;
    }

    public static Posting[] Decode(ReadOnlySpan<byte> blob)
    {
        if (blob.Length % PostingSize != 0)
            throw new FormatException($"Blob de postings com {blob.Length} bytes: não é múltiplo de {PostingSize}.");

        var postings = new Posting[blob.Length / PostingSize];
        for (var i = 0; i < postings.Length; i++)
        {
            var entry = blob.Slice(i * PostingSize, PostingSize);
            postings[i] = new Posting(
                BinaryPrimitives.ReadInt32LittleEndian(entry),
                BinaryPrimitives.ReadInt32LittleEndian(entry[4..]));
        }
        return postings;
    }

    private static void Write(Span<byte> destination, Posting posting)
    {
        BinaryPrimitives.WriteInt32LittleEndian(destination, posting.SongId);
        BinaryPrimitives.WriteInt32LittleEndian(destination[4..], posting.OffsetMs);
    }
}

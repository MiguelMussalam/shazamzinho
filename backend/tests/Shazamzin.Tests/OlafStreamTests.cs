using System.Runtime.InteropServices;
using Shazamzin.Core.Models;
using Shazamzin.Infrastructure.Native;

namespace Shazamzin.Tests;

/// <summary>
/// A extração em fluxo (shazamzin_olaf_stream_*) é o que o firmware usa para processar o
/// microfone enquanto grava. Ela precisa dar exatamente o mesmo resultado que a extração em
/// bloco usada na ingestão do catálogo, qualquer que seja o tamanho dos pedaços alimentados.
/// </summary>
public sealed class OlafStreamTests
{
    private const string Library = "libolaf";

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int Callback(uint hash, uint timestampMs, IntPtr user);

    [DllImport(Library, EntryPoint = "shazamzin_olaf_stream_new", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr StreamNew(Callback callback, IntPtr user);
    [DllImport(Library, EntryPoint = "shazamzin_olaf_stream_feed", CallingConvention = CallingConvention.Cdecl)]
    private static extern int StreamFeed(IntPtr stream, [In] float[] samples, nuint count);
    [DllImport(Library, EntryPoint = "shazamzin_olaf_stream_finish", CallingConvention = CallingConvention.Cdecl)]
    private static extern int StreamFinish(IntPtr stream);

    private static float[] Music(int seconds)
    {
        var samples = new float[8000 * seconds];
        var random = new Random(123);
        for (var i = 0; i < samples.Length; i++)
        {
            var t = i / 8000.0;
            var note = 220 + ((int)(t * 7) * 137 % 1700);
            samples[i] = (float)(0.4 * Math.Sin(2 * Math.PI * note * t) +
                0.2 * Math.Sin(2 * Math.PI * (note + 311) * t) + 0.05 * (random.NextDouble() - 0.5));
        }
        return samples;
    }

    /// <summary>Alimenta o fluxo em pedaços com os tamanhos dados (ciclando) e devolve status e fingerprints.</summary>
    private static (int FeedStatus, int FinishStatus, List<Fingerprint> Fingerprints) Stream(float[] samples, int[] chunkSizes)
    {
        var result = new List<Fingerprint>();
        Callback callback = (hash, ms, _) => { result.Add(new Fingerprint(hash, ms)); return 0; };
        var stream = StreamNew(callback, IntPtr.Zero);
        Assert.NotEqual(IntPtr.Zero, stream);
        var feed = 0;
        for (int offset = 0, k = 0; offset < samples.Length && feed == 0; k++)
        {
            var size = Math.Min(chunkSizes[k % chunkSizes.Length], samples.Length - offset);
            feed = StreamFeed(stream, samples[offset..(offset + size)], (nuint)size);
            offset += size;
        }
        var finish = StreamFinish(stream);
        GC.KeepAlive(callback);
        return (feed, finish, result);
    }

    [OlafFact]
    public void Stream_MatchesBlockExtraction_ForAnyChunkSize()
    {
        var samples = Music(6);
        var block = OlafNative.Extract(samples).ToArray();
        Assert.NotEmpty(block);

        foreach (var chunks in new[] { new[] { samples.Length }, new[] { 1 }, new[] { 128 }, new[] { 256 },
                     new[] { 1000 }, new[] { 7, 1023, 1, 129, 4096 } })
        {
            var (feed, finish, streamed) = Stream(samples, chunks);
            Assert.Equal(0, feed);
            Assert.Equal(0, finish);
            Assert.Equal(block, streamed);
        }
    }

    [OlafFact]
    public void Stream_ShortAudioAndSilence_LikeBlockExtraction()
    {
        Assert.Empty(Stream(new float[1023], [100]).Fingerprints);
        Assert.Empty(Stream(new float[8000 * 5], [256]).Fingerprints);
        var (_, finish, _) = Stream(new float[1], [1]);
        Assert.Equal(0, finish);
    }

    [OlafFact]
    public void Stream_RejectsNonFiniteSamples()
    {
        var samples = Music(2);
        samples[5000] = float.NaN;
        var (feed, finish, _) = Stream(samples, [256]);
        Assert.Equal(1, feed);
        Assert.Equal(1, finish);
    }
}

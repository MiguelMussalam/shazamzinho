using System.Runtime.InteropServices;
using Shazamzin.Core.Models;

namespace Shazamzin.Infrastructure.Native;

/// <summary>Fronteira C/.NET. Cada extração cria e destrói uma sessão nativa independente.</summary>
public static class OlafNative
{
    private const string Library = "libolaf";
    public static string Profile => Marshal.PtrToStringUTF8(ProfileNative())
        ?? throw new InvalidOperationException("Olaf retornou um perfil inválido.");
    public static int SampleRate => SampleRateNative();
    public static IReadOnlyList<Fingerprint> Extract(float[] monoSamples, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(monoSamples);
        cancellationToken.ThrowIfCancellationRequested();
        var fingerprints = new List<Fingerprint>();
        Exception? callbackError = null;
        FingerprintCallback callback = (hash, ms, _) =>
        {
            if (cancellationToken.IsCancellationRequested) return 1;
            try { fingerprints.Add(new Fingerprint(hash, ms)); return 0; }
            catch (Exception error) { callbackError = error; return 1; }
        };
        var status = ExtractNative(monoSamples, (nuint)monoSamples.Length, callback, IntPtr.Zero);
        GC.KeepAlive(callback);
        if (callbackError is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(callbackError).Throw();
        cancellationToken.ThrowIfCancellationRequested();
        return status switch
        {
            0 => fingerprints,
            1 => throw new ArgumentException("PCM inválido: use mono float32 normalizado a 8 kHz.", nameof(monoSamples)),
            2 => throw new OutOfMemoryException("Falha ao alocar a sessão Olaf."),
            _ => throw new InvalidOperationException($"Extração Olaf interrompida (código {status}).")
        };
    }
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int FingerprintCallback(uint hash, uint timestampMs, IntPtr user);
    [DllImport(Library, EntryPoint = "shazamzin_olaf_extract", CallingConvention = CallingConvention.Cdecl)]
    private static extern int ExtractNative([In] float[] samples, nuint count, FingerprintCallback callback, IntPtr user);
    [DllImport(Library, EntryPoint = "shazamzin_olaf_profile", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr ProfileNative();
    [DllImport(Library, EntryPoint = "shazamzin_olaf_sample_rate", CallingConvention = CallingConvention.Cdecl)]
    private static extern int SampleRateNative();
}

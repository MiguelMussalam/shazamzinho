using System.Diagnostics;
using Shazamzin.Infrastructure.Native;

namespace Shazamzin.Tests;

public sealed class OlafFactAttribute : FactAttribute
{
    public OlafFactAttribute()
    {
        try { _ = OlafNative.Profile; }
        catch (DllNotFoundException) { Skip = "Compile native/build.ps1 e rode dotnet build para testar a integração nativa."; }
    }
}

public sealed class OlafNativeTests
{
    [OlafFact]
    public void SilenceAndShortAudio_DoNotProduceFingerprints()
    {
        Assert.Equal(8000, OlafNative.SampleRate);
        Assert.Empty(OlafNative.Extract([]));
        Assert.Empty(OlafNative.Extract(new float[512]));
        Assert.Empty(OlafNative.Extract(new float[8000 * 5]));
    }

    [OlafFact]
    public void NonFiniteSamples_AreRejectedBeforeEnteringTheFft()
    {
        Assert.Throws<ArgumentException>(() => OlafNative.Extract([float.NaN]));
        Assert.Throws<ArgumentException>(() => OlafNative.Extract([float.PositiveInfinity]));
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        Assert.Throws<OperationCanceledException>(() => OlafNative.Extract(new float[8000], cancelled.Token));
    }

    [OlafFact]
    public async Task ManagedAndNativeCli_ProduceTheSameFingerprints_AndSessionsDoNotLeakState()
    {
        var samples = new float[8000 * 6];
        var random = new Random(123);
        for (var i = 0; i < samples.Length; i++)
        {
            var t = i / 8000.0;
            var note = 220 + ((int)(t * 7) * 137 % 1700);
            samples[i] = (float)(0.4 * Math.Sin(2 * Math.PI * note * t) +
                0.2 * Math.Sin(2 * Math.PI * (note + 311) * t) + 0.05 * (random.NextDouble() - 0.5));
        }
        var managed = OlafNative.Extract(samples).ToArray();
        Assert.NotEmpty(managed);
        Assert.Equal(managed, OlafNative.Extract(samples).ToArray());
        Assert.Empty(OlafNative.Extract(new float[samples.Length]));

        var backend = new DirectoryInfo(AppContext.BaseDirectory);
        while (backend is not null && !File.Exists(Path.Combine(backend.FullName, "Shazamzin.slnx"))) backend = backend.Parent;
        Assert.NotNull(backend);
        var cli = Path.Combine(backend.Parent!.FullName, "native", "build", OperatingSystem.IsWindows() ? "shazamzin-olaf.exe" : "shazamzin-olaf");
        Assert.True(File.Exists(cli), "Compile a CLI nativa com native/build.ps1.");
        var pcmPath = Path.Combine(Path.GetTempPath(), $"shazamzin-{Guid.NewGuid():N}.f32");
        try
        {
            var bytes = new byte[samples.Length * sizeof(float)];
            Buffer.BlockCopy(samples, 0, bytes, 0, bytes.Length);
            await File.WriteAllBytesAsync(pcmPath, bytes);
            var start = new ProcessStartInfo(cli) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
            start.ArgumentList.Add(pcmPath);
            using var process = Process.Start(start)!;
            var output = process.StandardOutput.ReadToEndAsync();
            var error = process.StandardError.ReadToEndAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            await process.WaitForExitAsync(timeout.Token);
            Assert.Equal(0, process.ExitCode);
            var actual = (await output).Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            await error;
            Assert.Equal(managed.Select(f => $"{f.Hash},{f.TimestampMs}"), actual);
        }
        finally { File.Delete(pcmPath); }
    }
}

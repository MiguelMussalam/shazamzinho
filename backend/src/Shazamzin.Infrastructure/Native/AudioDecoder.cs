using System.Diagnostics;
using System.Globalization;
using System.Text.Json;

namespace Shazamzin.Infrastructure.Native;

public sealed record AudioMetadata(string Title, string Artist, string? Album, int? DurationSec);

/// <summary>Usa as ferramentas FFmpeg/FFprobe sem shell, aceitando caminhos com espaços e Unicode.</summary>
public static class AudioDecoder
{
    private const int MaxPcmBytes = 256 * 1024 * 1024;

    public static async Task<float[]> DecodeAsync(string path, double offsetSeconds = 0, double? durationSeconds = null,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(path)) throw new FileNotFoundException("Arquivo de áudio não encontrado.", path);
        if (!double.IsFinite(offsetSeconds) || offsetSeconds < 0 ||
            durationSeconds is { } duration && (!double.IsFinite(duration) || duration <= 0))
            throw new ArgumentOutOfRangeException(nameof(offsetSeconds), "Offset deve ser >= 0 e duração deve ser > 0.");
        var args = new List<string> { "-nostdin", "-hide_banner", "-loglevel", "error", "-i", Path.GetFullPath(path) };
        // Seek depois de -i: decodifica o trecho com precisão, sem reutilizar fingerprints do catálogo.
        if (offsetSeconds > 0) args.AddRange(["-ss", offsetSeconds.ToString("R", CultureInfo.InvariantCulture)]);
        if (durationSeconds is { } seconds) args.AddRange(["-t", seconds.ToString("R", CultureInfo.InvariantCulture)]);
        args.AddRange(["-vn", "-ac", "1", "-ar", OlafNative.SampleRate.ToString(CultureInfo.InvariantCulture), "-f", "f32le", "pipe:1"]);
        using var process = Start("ffmpeg", args);
        var stderr = process.StandardError.ReadToEndAsync(cancellationToken);
        using var pcm = new MemoryStream();
        try
        {
            var buffer = new byte[64 * 1024];
            int read;
            while ((read = await process.StandardOutput.BaseStream.ReadAsync(buffer, cancellationToken)) > 0)
            {
                if (pcm.Length + read > MaxPcmBytes)
                    throw new InvalidOperationException("Áudio excede o limite de 256 MiB de PCM por extração. Divida o arquivo.");
                pcm.Write(buffer, 0, read);
            }
            await process.WaitForExitAsync(cancellationToken);
            var error = await stderr;
            if (process.ExitCode != 0) throw new InvalidOperationException($"FFmpeg falhou: {error.Trim()}");
            if (pcm.Length == 0 || pcm.Length % sizeof(float) != 0)
                throw new InvalidOperationException("FFmpeg não produziu PCM válido. Verifique o trecho solicitado.");
            var samples = new float[checked((int)pcm.Length / sizeof(float))];
            Buffer.BlockCopy(pcm.GetBuffer(), 0, samples, 0, (int)pcm.Length);
            if (!BitConverter.IsLittleEndian) throw new PlatformNotSupportedException("PCM f32le requer host little-endian.");
            return samples;
        }
        finally
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            // Drena stderr inclusive ao cancelar ou atingir o limite, sem deixar tarefa/processo órfão.
            try { await stderr; } catch (OperationCanceledException) { }
        }
    }

    public static async Task<AudioMetadata> ReadMetadataAsync(string path, CancellationToken cancellationToken = default)
    {
        using var process = Start("ffprobe", ["-v", "error", "-show_entries", "format=duration:format_tags=title,artist,album",
            "-of", "json", Path.GetFullPath(path)]);
        var stdout = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderr = process.StandardError.ReadToEndAsync(cancellationToken);
        try
        {
            await process.WaitForExitAsync(cancellationToken);
            var error = await stderr;
            if (process.ExitCode != 0) throw new InvalidOperationException($"FFprobe falhou: {error.Trim()}");
            using var json = JsonDocument.Parse(await stdout);
            var format = json.RootElement.GetProperty("format");
            string? Tag(string name)
            {
                if (!format.TryGetProperty("tags", out var tags)) return null;
                foreach (var tag in tags.EnumerateObject())
                    if (tag.Name.Equals(name, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(tag.Value.GetString()))
                        return tag.Value.GetString();
                return null;
            }
            int? duration = format.TryGetProperty("duration", out var value) &&
                double.TryParse(value.GetString(), CultureInfo.InvariantCulture, out var seconds) &&
                double.IsFinite(seconds) && seconds >= 0 && seconds < int.MaxValue ? (int)Math.Round(seconds) : null;
            return new AudioMetadata(Tag("title") ?? Path.GetFileNameWithoutExtension(path),
                Tag("artist") ?? "Artista desconhecido", Tag("album"), duration);
        }
        finally
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            try { await Task.WhenAll(stdout, stderr); } catch (OperationCanceledException) { }
        }
    }

    private static Process Start(string tool, IEnumerable<string> arguments)
    {
        var start = new ProcessStartInfo(tool)
        {
            UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true
        };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        try { return Process.Start(start) ?? throw new InvalidOperationException($"Não foi possível iniciar {tool}."); }
        catch (System.ComponentModel.Win32Exception e)
        { throw new InvalidOperationException($"{tool} não encontrado. Instale FFmpeg e confira o PATH.", e); }
    }
}

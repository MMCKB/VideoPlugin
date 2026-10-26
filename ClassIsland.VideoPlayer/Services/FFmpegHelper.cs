using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;

namespace ClassIsland.VideoPlayer.Services;

/// <summary>
/// 视频基本信息。
/// </summary>
public sealed class VideoInfo
{
    public int Width { get; init; }
    public int Height { get; init; }
    public double Fps { get; init; } = 25;
    public TimeSpan Duration { get; init; }

    /// <summary>
    /// 按最大宽度等比缩放，返回偶数尺寸（保证 rawvideo 帧对齐）。
    /// </summary>
    public (int Width, int Height) FitWidth(int maxWidth)
    {
        if (Width <= 0 || Height <= 0)
        {
            return (Math.Max(2, maxWidth), Math.Max(2, maxWidth * 9 / 16));
        }

        var w = Width;
        var h = Height;
        if (maxWidth > 0 && w > maxWidth)
        {
            h = (int)Math.Round(h * (double)maxWidth / w);
            w = maxWidth;
        }

        w = Math.Max(2, w & ~1);
        h = Math.Max(2, h & ~1);
        return (w, h);
    }
}

/// <summary>
/// 负责定位 ffmpeg、探测视频信息以及下载 ffmpeg。
/// </summary>
public static class FFmpegHelper
{
    public const string DownloadUrl = "https://www.gyan.dev/ffmpeg/builds/ffmpeg-release-essentials.zip";

    private static readonly Regex DurationRegex = new(@"Duration:\s*(\d+):(\d+):(\d+(?:\.\d+)?)", RegexOptions.Compiled);
    private static readonly Regex SizeRegex = new(@"(\d{2,5})x(\d{2,5})", RegexOptions.Compiled);
    private static readonly Regex FpsRegex = new(@"([\d.]+)\s*fps", RegexOptions.Compiled);

    /// <summary>
    /// 插件程序集所在目录。
    /// </summary>
    public static string PluginDirectory =>
        Path.GetDirectoryName(typeof(FFmpegHelper).Assembly.Location) ?? AppContext.BaseDirectory;

    /// <summary>
    /// 查找 ffmpeg.exe。
    /// </summary>
    /// <param name="configuredPath">用户在设置里指定的路径，可以为空。</param>
    public static string? Locate(string? configuredPath)
    {
        // 1. 用户指定
        var byUser = TryAsExecutable(configuredPath);
        if (byUser != null)
        {
            return byUser;
        }

        // 2. 插件目录及其子目录
        var baseDir = PluginDirectory;
        foreach (var candidate in new[]
                 {
                     Path.Combine(baseDir, "ffmpeg.exe"),
                     Path.Combine(baseDir, "ffmpeg", "ffmpeg.exe"),
                     Path.Combine(baseDir, "ffmpeg", "bin", "ffmpeg.exe"),
                     Path.Combine(baseDir, "tools", "ffmpeg.exe")
                 })
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        // 3. 系统 PATH
        var pathVar = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var dir in pathVar.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                var candidate = Path.Combine(dir.Trim(), "ffmpeg.exe");
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
            catch
            {
                // 忽略非法路径
            }
        }

        return null;
    }

    private static string? TryAsExecutable(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        try
        {
            path = path.Trim().Trim('"');
            if (File.Exists(path))
            {
                return Path.GetFullPath(path);
            }

            if (Directory.Exists(path))
            {
                var exe = Path.Combine(path, "ffmpeg.exe");
                if (File.Exists(exe))
                {
                    return Path.GetFullPath(exe);
                }

                var binExe = Path.Combine(path, "bin", "ffmpeg.exe");
                if (File.Exists(binExe))
                {
                    return Path.GetFullPath(binExe);
                }
            }
        }
        catch
        {
            // 忽略
        }

        return null;
    }

    /// <summary>
    /// 读取视频的基本信息（分辨率、帧率、时长）。解析失败时返回 null。
    /// </summary>
    public static VideoInfo? Probe(string ffmpegPath, string source, int timeoutMs = 20000)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = ffmpegPath,
                UseShellExecute = false,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                CreateNoWindow = true,
                StandardErrorEncoding = Encoding.UTF8
            };
            psi.ArgumentList.Add("-hide_banner");
            psi.ArgumentList.Add("-nostdin");
            psi.ArgumentList.Add("-i");
            psi.ArgumentList.Add(source);

            using var process = Process.Start(psi);
            if (process == null)
            {
                return null;
            }

            var stderrTask = process.StandardError.ReadToEndAsync();
            _ = process.StandardOutput.ReadToEndAsync();

            if (!process.WaitForExit(timeoutMs))
            {
                try
                {
                    process.Kill(true);
                }
                catch
                {
                    // 忽略
                }

                return null;
            }

            var stderr = stderrTask.GetAwaiter().GetResult();
            return ParseProbeOutput(stderr);
        }
        catch
        {
            return null;
        }
    }

    internal static VideoInfo? ParseProbeOutput(string stderr)
    {
        if (string.IsNullOrWhiteSpace(stderr))
        {
            return null;
        }

        int width = 0, height = 0;
        double fps = 0;

        foreach (var rawLine in stderr.Split('\n'))
        {
            var line = rawLine.Trim();
            if (!line.Contains("Video:", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            // 去掉括号里的编码信息，避免 0x31637661 之类的十六进制串干扰
            var cleaned = Regex.Replace(line, @"\([^)]*\)", " ");
            var size = SizeRegex.Match(cleaned);
            if (size.Success)
            {
                width = int.Parse(size.Groups[1].Value);
                height = int.Parse(size.Groups[2].Value);
            }

            var fpsMatch = FpsRegex.Match(cleaned);
            if (fpsMatch.Success && double.TryParse(fpsMatch.Groups[1].Value, out var parsedFps) && parsedFps > 0)
            {
                fps = parsedFps;
            }

            break;
        }

        if (width <= 0 || height <= 0)
        {
            return null;
        }

        var duration = TimeSpan.Zero;
        var durationMatch = DurationRegex.Match(stderr);
        if (durationMatch.Success)
        {
            duration = new TimeSpan(0,
                int.Parse(durationMatch.Groups[1].Value),
                int.Parse(durationMatch.Groups[2].Value))
                + TimeSpan.FromSeconds(double.Parse(durationMatch.Groups[3].Value, System.Globalization.CultureInfo.InvariantCulture));
        }

        if (fps <= 0 || fps > 240)
        {
            fps = 25;
        }

        return new VideoInfo
        {
            Width = width,
            Height = height,
            Fps = fps,
            Duration = duration
        };
    }

    /// <summary>
    /// 下载 ffmpeg 并解压到插件配置目录，返回 ffmpeg.exe 的完整路径。
    /// </summary>
    public static async Task<string> DownloadAsync(string targetFolder, IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(targetFolder);
        var zipPath = Path.Combine(targetFolder, "ffmpeg-download.zip");

        using (var http = new HttpClient())
        {
            http.Timeout = TimeSpan.FromMinutes(10);
            http.DefaultRequestHeaders.Add("User-Agent", "ClassIsland.VideoPlayer");

            using var response = await http.GetAsync(DownloadUrl, HttpCompletionOption.ResponseHeadersRead,
                cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            var total = response.Content.Headers.ContentLength ?? -1L;
            await using var source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            await using var target = File.Create(zipPath);

            var buffer = new byte[81920];
            long copied = 0;
            int read;
            while ((read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
            {
                await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                copied += read;
                if (total > 0)
                {
                    progress?.Report((double)copied / total);
                }
            }
        }

        var outputPath = Path.Combine(targetFolder, "ffmpeg.exe");
        using (var archive = ZipFile.OpenRead(zipPath))
        {
            var entry = archive.Entries.FirstOrDefault(e =>
                e.FullName.Replace('\\', '/').EndsWith("/bin/ffmpeg.exe", StringComparison.OrdinalIgnoreCase));
            if (entry == null)
            {
                throw new InvalidOperationException("下载的压缩包中没有找到 ffmpeg.exe。");
            }

            entry.ExtractToFile(outputPath, true);
        }

        try
        {
            File.Delete(zipPath);
        }
        catch
        {
            // 忽略
        }

        return outputPath;
    }
}

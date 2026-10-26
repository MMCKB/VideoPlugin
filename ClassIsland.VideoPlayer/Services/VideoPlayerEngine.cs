using System.Diagnostics;
using System.Text;
using NAudio.Wave;

namespace ClassIsland.VideoPlayer.Services;

public sealed class VideoFrameEventArgs : EventArgs
{
    public VideoFrameEventArgs(byte[] buffer, int width, int height, long frameIndex)
    {
        Buffer = buffer;
        Width = width;
        Height = height;
        FrameIndex = frameIndex;
    }

    /// <summary>BGRA32 像素数据。注意：该缓冲区会被复用，处理函数需要同步拷贝。</summary>
    public byte[] Buffer { get; }

    public int Width { get; }
    public int Height { get; }
    public long FrameIndex { get; }
}

/// <summary>
/// 基于 ffmpeg 子进程的视频播放引擎。
/// 视频：ffmpeg 输出 rawvideo(bgra) → 逐帧回调；音频：另一路 ffmpeg 输出 s16le → NAudio 播放。
/// 这样视频帧可以画到任意 Avalonia 控件上（保留透明/圆角），而不依赖原生窗口叠加。
/// </summary>
public sealed class VideoPlayerEngine : IDisposable
{
    private const int AudioSampleRate = 44100;
    private const int AudioChannels = 2;

    private readonly object _sync = new();

    private CancellationTokenSource? _cts;
    private Task? _worker;
    private Process? _videoProcess;
    private Process? _audioProcess;
    private WaveOutEvent? _waveOut;
    private BufferedWaveProvider? _audioBuffer;

    private volatile bool _paused;
    private volatile bool _muted;
    private volatile float _volume = 0.8f;
    private volatile bool _disposed;

    public event EventHandler<VideoFrameEventArgs>? FrameReady;
    public event EventHandler<string>? Failed;
    public event EventHandler? Ended;

    public VideoInfo? Info { get; private set; }

    public bool IsRunning => _worker is { IsCompleted: false };

    public bool IsPaused => _paused;

    public void SetVolume(double volume)
    {
        _volume = (float)Math.Clamp(volume, 0, 1);
        ApplyVolume();
    }

    public void SetMuted(bool muted)
    {
        _muted = muted;
        ApplyVolume();
    }

    private void ApplyVolume()
    {
        try
        {
            if (_waveOut != null)
            {
                _waveOut.Volume = _muted ? 0f : _volume;
            }
        }
        catch
        {
            // 部分声卡不支持软件音量，忽略
        }
    }

    public void Pause()
    {
        _paused = true;
        try
        {
            _waveOut?.Pause();
        }
        catch
        {
            // 忽略
        }
    }

    public void Resume()
    {
        _paused = false;
        try
        {
            _waveOut?.Play();
        }
        catch
        {
            // 忽略
        }
    }

    public void Start(string ffmpegPath, string source, int maxWidth, bool loop, double volume, bool muted)
    {
        Stop();

        if (_disposed)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(source))
        {
            Failed?.Invoke(this, "还没有设置视频来源。");
            return;
        }

        if (!File.Exists(ffmpegPath))
        {
            Failed?.Invoke(this, "找不到 ffmpeg.exe，请在【视频播放器】设置页中指定路径或一键下载。");
            return;
        }

        _volume = (float)Math.Clamp(volume, 0, 1);
        _muted = muted;
        _paused = false;

        _cts = new CancellationTokenSource();
        var ct = _cts.Token;
        var options = new EngineOptions(ffmpegPath, source, maxWidth, loop);
        _worker = Task.Run(() => RunAsync(options, ct), CancellationToken.None);
    }

    private sealed record EngineOptions(string FFmpeg, string Source, int MaxWidth, bool Loop);

    private async Task RunAsync(EngineOptions options, CancellationToken ct)
    {
        // 探测可能涉及网络 IO，必须放到后台，避免阻塞调用方（UI 线程）
        var info = FFmpegHelper.Probe(options.FFmpeg, options.Source);
        if (info == null)
        {
            if (!ct.IsCancellationRequested)
            {
                Failed?.Invoke(this, "无法读取视频信息，请确认文件存在且格式受支持。");
            }

            return;
        }

        Info = info;

        var (width, height) = info.FitWidth(options.MaxWidth);
        var frameBytes = width * height * 4;
        var buffer = new byte[frameBytes];

        try
        {
            do
            {
                var produced = await PlayOnceAsync(options, width, height, buffer, frameBytes, info.Fps, ct)
                    .ConfigureAwait(false);
                if (produced == 0)
                {
                    break;
                }
            } while (options.Loop && !ct.IsCancellationRequested);

            if (!ct.IsCancellationRequested)
            {
                Ended?.Invoke(this, EventArgs.Empty);
            }
        }
        catch (OperationCanceledException)
        {
            // 正常停止
        }
        catch (Exception ex)
        {
            if (!ct.IsCancellationRequested)
            {
                Failed?.Invoke(this, ex.Message);
            }
        }
    }

    /// <summary>
    /// 播放一遍。返回实际渲染的帧数。
    /// </summary>
    private async Task<int> PlayOnceAsync(EngineOptions options, int width, int height, byte[] buffer,
        int frameBytes, double fps, CancellationToken ct)
    {
        if (fps <= 0 || fps > 240)
        {
            fps = 25;
        }

        var videoArgs = new List<string>
        {
            "-hide_banner", "-loglevel", "error", "-nostdin",
            "-i", options.Source,
            "-an", "-sn", "-dn",
            "-vf", $"scale={width}:{height}:flags=bilinear",
            "-pix_fmt", "bgra",
            "-f", "rawvideo",
            "pipe:1"
        };

        var audioArgs = new List<string>
        {
            "-hide_banner", "-loglevel", "error", "-nostdin",
            "-i", options.Source,
            "-vn", "-sn", "-dn",
            "-ac", AudioChannels.ToString(),
            "-ar", AudioSampleRate.ToString(),
            "-f", "s16le",
            "pipe:1"
        };

        var videoError = new StringBuilder();
        Process? videoProcess = null;
        Process? audioProcess = null;
        Task? audioTask = null;
        var frameCount = 0;

        try
        {
            videoProcess = StartProcess(options.FFmpeg, videoArgs, videoError);
            if (videoProcess == null)
            {
                Failed?.Invoke(this, "无法启动 ffmpeg 进程。");
                return 0;
            }

            _videoProcess = videoProcess;

            // 音频单独一路，失败也不影响画面
            try
            {
                audioProcess = StartProcess(options.FFmpeg, audioArgs, new StringBuilder());
                if (audioProcess != null)
                {
                    _audioProcess = audioProcess;
                    InitAudio();
                    audioTask = Task.Run(() => PumpAudioAsync(audioProcess, ct), CancellationToken.None);
                }
            }
            catch
            {
                audioProcess = null;
            }

            var stdout = videoProcess.StandardOutput.BaseStream;
            var stopwatch = Stopwatch.StartNew();
            var pausedTime = TimeSpan.Zero;
            long frameIndex = 0;
            var started = false;

            while (!ct.IsCancellationRequested)
            {
                if (_paused)
                {
                    var pauseStart = stopwatch.Elapsed;
                    while (_paused && !ct.IsCancellationRequested)
                    {
                        try
                        {
                            await Task.Delay(20, ct).ConfigureAwait(false);
                        }
                        catch (OperationCanceledException)
                        {
                            break;
                        }
                    }

                    pausedTime += stopwatch.Elapsed - pauseStart;
                }

                if (ct.IsCancellationRequested)
                {
                    break;
                }

                var read = await ReadFullAsync(stdout, buffer, frameBytes, ct).ConfigureAwait(false);
                if (read < frameBytes)
                {
                    break;
                }

                if (!started)
                {
                    // 以首帧到达的时刻作为计时零点，避免解码启动耗时导致开头“快进”
                    started = true;
                    stopwatch.Restart();
                    pausedTime = TimeSpan.Zero;
                    frameIndex = 0;
                    StartAudioPlayback();
                }

                frameIndex++;
                frameCount++;

                var target = TimeSpan.FromSeconds((frameIndex - 1) / fps) + pausedTime;
                var delay = target - stopwatch.Elapsed;
                if (delay > TimeSpan.Zero)
                {
                    try
                    {
                        await Task.Delay(delay, ct).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                }

                FrameReady?.Invoke(this, new VideoFrameEventArgs(buffer, width, height, frameIndex));
            }
        }
        finally
        {
            TeardownAudio();

            try
            {
                if (audioTask != null)
                {
                    await Task.WhenAny(audioTask, Task.Delay(300, CancellationToken.None)).ConfigureAwait(false);
                }
            }
            catch
            {
                // 忽略
            }

            KillProcess(ref audioProcess);
            KillProcess(ref videoProcess);
            _videoProcess = null;
            _audioProcess = null;

            if (frameCount == 0 && !ct.IsCancellationRequested)
            {
                var message = videoError.ToString().Trim();
                Failed?.Invoke(this, string.IsNullOrEmpty(message)
                    ? "播放失败：ffmpeg 没有输出任何画面。"
                    : "播放失败：" + FirstLines(message, 4));
            }
        }

        return frameCount;
    }

    private static string FirstLines(string text, int count)
    {
        var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        return string.Join(" ", lines.Take(count).Select(l => l.Trim()));
    }

    private void InitAudio()
    {
        _audioBuffer = new BufferedWaveProvider(new WaveFormat(AudioSampleRate, 16, AudioChannels))
        {
            DiscardOnBufferOverflow = true,
            BufferDuration = TimeSpan.FromSeconds(5)
        };

        var waveOut = new WaveOutEvent
        {
            DesiredLatency = 120,
            NumberOfBuffers = 3
        };
        waveOut.Init(_audioBuffer);
        waveOut.Volume = _muted ? 0f : _volume;
        // 先不播放，等首帧渲染出来再启动，保证音画同时开始
        _waveOut = waveOut;
    }

    private void StartAudioPlayback()
    {
        if (_paused)
        {
            return;
        }

        try
        {
            _waveOut?.Play();
        }
        catch
        {
            // 忽略
        }
    }

    private async Task PumpAudioAsync(Process process, CancellationToken ct)
    {
        var buffer = new byte[8192];
        var stream = process.StandardOutput.BaseStream;

        try
        {
            while (!ct.IsCancellationRequested)
            {
                var read = await stream.ReadAsync(buffer, ct).ConfigureAwait(false);
                if (read <= 0)
                {
                    break;
                }

                _audioBuffer?.AddSamples(buffer, 0, read);

                // 避免一次性把整段音频塞进缓冲区，保持与画面同步
                while (_audioBuffer != null && _audioBuffer.BufferedDuration > TimeSpan.FromMilliseconds(400))
                {
                    await Task.Delay(20, ct).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // 正常停止
        }
        catch
        {
            // 音频出错不影响画面
        }
    }

    private void TeardownAudio()
    {
        try
        {
            _waveOut?.Stop();
        }
        catch
        {
            // 忽略
        }

        try
        {
            _waveOut?.Dispose();
        }
        catch
        {
            // 忽略
        }

        _waveOut = null;
        _audioBuffer = null;
    }

    private static Process? StartProcess(string ffmpeg, IEnumerable<string> arguments, StringBuilder errorSink)
    {
        var psi = new ProcessStartInfo
        {
            FileName = ffmpeg,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardErrorEncoding = Encoding.UTF8
        };

        foreach (var argument in arguments)
        {
            psi.ArgumentList.Add(argument);
        }

        var process = Process.Start(psi);
        if (process == null)
        {
            return null;
        }

        process.ErrorDataReceived += (_, args) =>
        {
            if (!string.IsNullOrEmpty(args.Data))
            {
                lock (errorSink)
                {
                    if (errorSink.Length < 4096)
                    {
                        errorSink.AppendLine(args.Data);
                    }
                }
            }
        };
        process.BeginErrorReadLine();
        return process;
    }

    private static async Task<int> ReadFullAsync(Stream stream, byte[] buffer, int count, CancellationToken ct)
    {
        var total = 0;
        while (total < count)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(total, count - total), ct).ConfigureAwait(false);
            if (read <= 0)
            {
                break;
            }

            total += read;
        }

        return total;
    }

    private static void KillProcess(ref Process? process)
    {
        if (process == null)
        {
            return;
        }

        try
        {
            if (!process.HasExited)
            {
                process.Kill(true);
            }
        }
        catch
        {
            // 忽略
        }

        try
        {
            process.Dispose();
        }
        catch
        {
            // 忽略
        }

        process = null;
    }

    public void Stop()
    {
        lock (_sync)
        {
            try
            {
                _cts?.Cancel();
            }
            catch
            {
                // 忽略
            }

            KillProcess(ref _videoProcess);
            KillProcess(ref _audioProcess);
            TeardownAudio();

            try
            {
                _worker?.Wait(500);
            }
            catch
            {
                // 忽略
            }

            _cts?.Dispose();
            _cts = null;
            _worker = null;
            _paused = false;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Stop();
    }
}

using System.ComponentModel;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using ClassIsland.Core.Abstractions.Controls;
using ClassIsland.Core.Attributes;
using ClassIsland.VideoPlayer.Models;
using ClassIsland.VideoPlayer.Services;

namespace ClassIsland.VideoPlayer.Controls;

/// <summary>
/// 主界面（岛）上的视频播放组件。
/// </summary>
[ComponentInfo(
    "8F2A6C1D-3B4E-4F7A-9C2D-1E5B8A0F6C33",
    "视频播放器",
    "\uEDB9",
    "在主界面（岛）上播放视频"
)]
public partial class VideoPlayerComponent : ComponentBase<VideoPlayerSettings>
{
    private readonly object _frameLock = new();

    private VideoPlayerEngine? _engine;
    private WriteableBitmap? _bitmap;
    private byte[]? _pendingFrame;
    private int _pendingWidth;
    private int _pendingHeight;
    private bool _renderQueued;
    private double _aspect = 16d / 9d;
    private bool _settingsHooked;
    private bool _pausedByVisibility;
    private int _startRetry;
    private DispatcherTimer? _visibilityTimer;
    private bool _firstFrameArrived;
    private bool _alphaProbed;
    private bool _hasAlpha;

    public VideoPlayerComponent()
    {
        InitializeComponent();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        StartPlayback();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        StopPlayback();
        base.OnDetachedFromVisualTree(e);
    }

    private void StartVisibilityTimer()
    {
        _visibilityTimer ??= new DispatcherTimer(TimeSpan.FromMilliseconds(250), DispatcherPriority.Background,
            (_, _) => UpdateVisibilityPause());
        _visibilityTimer.Start();
    }

    private void StopVisibilityTimer()
    {
        _visibilityTimer?.Stop();
    }

    /// <summary>
    /// 组件被隐藏（被隐藏规则隐藏、主界面收起等）时暂停播放，重新可见后继续。
    /// </summary>
    private void UpdateVisibilityPause()
    {
        if (Settings == null)
        {
            return;
        }

        if (Settings.PauseWhenHidden && !IsEffectivelyVisible)
        {
            if (!_pausedByVisibility)
            {
                _pausedByVisibility = true;
                _engine?.Pause();
            }
        }
        else if (_pausedByVisibility)
        {
            _pausedByVisibility = false;
            _engine?.Resume();
        }
    }

    private void StartPlayback()
    {
        if (Settings == null)
        {
            // 设置尚未就绪时稍后重试（最多 50 次，约 1 秒）
            if (_startRetry++ < 50)
            {
                Dispatcher.UIThread.Post(StartPlayback, DispatcherPriority.Loaded);
            }

            return;
        }

        _startRetry = 0;
        StartVisibilityTimer();

        if (!_settingsHooked)
        {
            Settings.PropertyChanged += OnSettingsChanged;
            _settingsHooked = true;
        }

        ApplyLayout();

        var source = Settings.Source;
        if (string.IsNullOrWhiteSpace(source))
        {
            ShowStatus("在【组件设置】中选择要播放的视频");
            return;
        }

        var pluginSettings = Plugin.Instance?.Settings;
        var ffmpeg = FFmpegHelper.Locate(pluginSettings?.FFmpegPath);
        if (ffmpeg == null)
        {
            ShowStatus("未找到 ffmpeg.exe，请在【视频播放器】设置页中指定或一键下载");
            return;
        }

        var maxWidth = Settings.MaxRenderWidth > 0
            ? Settings.MaxRenderWidth
            : pluginSettings?.MaxRenderWidth ?? 640;

        _firstFrameArrived = false;
        ShowStatus("正在加载视频…");

        var engine = new VideoPlayerEngine();
        engine.FrameReady += OnFrameReady;
        engine.Failed += OnEngineFailed;
        engine.Ended += OnEngineEnded;
        _engine = engine;

        try
        {
            engine.Start(ffmpeg, source, maxWidth, Settings.Loop, Settings.Volume, Settings.Muted);
        }
        catch (Exception ex)
        {
            ShowStatus("播放失败：" + ex.Message);
        }
    }

    private void StopPlayback()
    {
        StopVisibilityTimer();

        if (_settingsHooked && Settings != null)
        {
            Settings.PropertyChanged -= OnSettingsChanged;
            _settingsHooked = false;
        }

        if (_engine != null)
        {
            var engine = _engine;
            _engine = null;
            engine.FrameReady -= OnFrameReady;
            engine.Failed -= OnEngineFailed;
            engine.Ended -= OnEngineEnded;
            // 释放时可能需要等待子进程退出，放到后台避免卡住界面
            Task.Run(engine.Dispose);
        }

        _pausedByVisibility = false;
        _alphaProbed = false;
        _hasAlpha = false;

        lock (_frameLock)
        {
            _pendingFrame = null;
            _renderQueued = false;
        }

        if (_bitmap != null)
        {
            VideoImage.Source = null;
            _bitmap.Dispose();
            _bitmap = null;
        }
    }

    private void OnSettingsChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(VideoPlayerSettings.Volume):
                _engine?.SetVolume(Settings.Volume);
                break;
            case nameof(VideoPlayerSettings.Muted):
                _engine?.SetMuted(Settings.Muted);
                break;
            case nameof(VideoPlayerSettings.DisplayHeight):
            case nameof(VideoPlayerSettings.CornerRadius):
                ApplyLayout();
                break;
            case nameof(VideoPlayerSettings.PauseWhenHidden):
                UpdateVisibilityPause();
                break;
            case nameof(VideoPlayerSettings.Source):
            case nameof(VideoPlayerSettings.Loop):
            case nameof(VideoPlayerSettings.MaxRenderWidth):
                Dispatcher.UIThread.Post(() =>
                {
                    StopPlayback();
                    StartPlayback();
                });
                break;
        }
    }

    private void ApplyLayout()
    {
        if (Settings == null || RootBorder == null || VideoImage == null)
        {
            return;
        }

        var height = Math.Max(24, Settings.DisplayHeight);
        RootBorder.Height = height;
        RootBorder.CornerRadius = new CornerRadius(Settings.CornerRadius);
        VideoImage.Height = height;
        VideoImage.Width = Math.Round(height * _aspect);
    }

    private void OnEngineFailed(object? sender, string message)
    {
        Dispatcher.UIThread.Post(() => ShowStatus(message));
    }

    private void OnEngineEnded(object? sender, EventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (Settings is { Loop: false })
            {
                ShowStatus("播放结束");
            }
        });
    }

    private void OnFrameReady(object? sender, VideoFrameEventArgs e)
    {
        if (!_firstFrameArrived)
        {
            _firstFrameArrived = true;
            Dispatcher.UIThread.Post(HideStatus, DispatcherPriority.Background);
        }

        var aspect = e.Height > 0 ? (double)e.Width / e.Height : 16d / 9d;
        if (Math.Abs(aspect - _aspect) > 0.001)
        {
            _aspect = aspect;
            Dispatcher.UIThread.Post(ApplyLayout, DispatcherPriority.Background);
        }

        lock (_frameLock)
        {
            if (_pendingFrame == null || _pendingFrame.Length != e.Buffer.Length)
            {
                _pendingFrame = new byte[e.Buffer.Length];
            }

            Buffer.BlockCopy(e.Buffer, 0, _pendingFrame, 0, e.Buffer.Length);
            _pendingWidth = e.Width;
            _pendingHeight = e.Height;

            if (_renderQueued)
            {
                return;
            }

            _renderQueued = true;
        }

        Dispatcher.UIThread.Post(RenderPendingFrame, DispatcherPriority.Render);
    }

    private void RenderPendingFrame()
    {
        byte[]? frame;
        int width;
        int height;

        lock (_frameLock)
        {
            frame = _pendingFrame;
            _pendingFrame = null;
            width = _pendingWidth;
            height = _pendingHeight;
            _renderQueued = false;
        }

        if (frame == null || width <= 0 || height <= 0)
        {
            return;
        }

        if (_bitmap == null || _bitmap.PixelSize.Width != width || _bitmap.PixelSize.Height != height)
        {
            // 首帧判定是否真的带透明通道：带 alpha 的视频（webm/mov/带透明 gif 等）
            // 必须用 Unpremul 才能把透明区域正确合成到主界面的透明背景上。
            if (!_alphaProbed)
            {
                _alphaProbed = true;
                _hasAlpha = DetectAlpha(frame);
            }

            _bitmap = new WriteableBitmap(new PixelSize(width, height), new Vector(96, 96),
                PixelFormat.Bgra8888, _hasAlpha ? AlphaFormat.Unpremul : AlphaFormat.Opaque);
            VideoImage.Source = _bitmap;
        }

        try
        {
            using (var locked = _bitmap.Lock())
            {
                var rowBytes = width * 4;
                if (locked.RowBytes == rowBytes)
                {
                    Marshal.Copy(frame, 0, locked.Address, rowBytes * height);
                }
                else
                {
                    for (var y = 0; y < height; y++)
                    {
                        Marshal.Copy(frame, y * rowBytes, locked.Address + (y * locked.RowBytes), rowBytes);
                    }
                }
            }

            // Lock() 释放后位图会失效并重新生成快照，因此只需再触发一次重绘
            VideoImage.InvalidateVisual();
        }
        catch
        {
            // 渲染失败时忽略该帧
        }
    }

    /// <summary>
    /// 判断这一帧是否真的带透明像素（BGRA，alpha 在第 4 字节）。
    /// 全 0 视为解码异常，按不透明处理，避免整块画面消失。
    /// </summary>
    private static bool DetectAlpha(byte[] frame)
    {
        var hasOpaque = false;
        var hasTransparent = false;

        for (var i = 3; i < frame.Length; i += 4)
        {
            var alpha = frame[i];
            if (alpha == 255)
            {
                hasOpaque = true;
            }
            else
            {
                hasTransparent = true;
                if (alpha != 0)
                {
                    // 半透明像素，可以确定是带 alpha 的视频
                    return true;
                }
            }

            if (hasOpaque && hasTransparent)
            {
                return true;
            }
        }

        return false;
    }

    private void ShowStatus(string message)
    {
        if (StatusText == null || StatusBorder == null)
        {
            return;
        }

        StatusText.Text = message;
        StatusBorder.IsVisible = true;
    }

    private void HideStatus()
    {
        if (StatusBorder != null)
        {
            StatusBorder.IsVisible = false;
        }
    }
}

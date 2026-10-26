using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using ClassIsland.Core.Abstractions.Controls;
using ClassIsland.Core.Attributes;
using ClassIsland.Core.Enums.SettingsWindow;
using ClassIsland.VideoPlayer.Services;
using FluentAvalonia.UI.Controls;

namespace ClassIsland.VideoPlayer.Views.SettingsPages;

[SettingsPageInfo("classisland.videoplayer", "视频播放器", "\uEDB9", "\uEDB9", SettingsPageCategory.External)]
public partial class VideoPlayerSettingsPage : SettingsPageBase
{
    private bool _downloading;

    public VideoPlayerSettingsPage()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            if (Plugin.Instance != null)
            {
                DataContext = Plugin.Instance.Settings;
            }

            RefreshStatus();
        };
    }

    private void RefreshStatus()
    {
        var configured = Plugin.Instance?.Settings.FFmpegPath;
        var path = FFmpegHelper.Locate(configured);
        if (path == null)
        {
            StatusBar.Severity = InfoBarSeverity.Warning;
            StatusBar.Message = "未找到 ffmpeg.exe。请指定其路径，或点击下方【下载】自动获取。";
        }
        else
        {
            StatusBar.Severity = InfoBarSeverity.Success;
            StatusBar.Message = "已找到 ffmpeg：" + path;
        }
    }

    private async void OnBrowseFFmpegClick(object? sender, RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel?.StorageProvider == null || Plugin.Instance == null)
        {
            return;
        }

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "选择 ffmpeg.exe",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("可执行文件") { Patterns = new[] { "ffmpeg.exe", "*.exe" } },
                FilePickerFileTypes.All
            }
        });

        if (files.Count > 0)
        {
            var local = files[0].TryGetLocalPath();
            if (!string.IsNullOrEmpty(local))
            {
                Plugin.Instance.Settings.FFmpegPath = local;
                RefreshStatus();
            }
        }
    }

    private async void OnDownloadClick(object? sender, RoutedEventArgs e)
    {
        if (_downloading || Plugin.Instance == null)
        {
            return;
        }

        _downloading = true;
        DownloadButton.IsEnabled = false;
        DownloadProgress.IsVisible = true;
        DownloadProgress.Value = 0;
        StatusBar.Severity = InfoBarSeverity.Informational;
        StatusBar.Message = "正在下载 FFmpeg，请稍候…";

        try
        {
            var target = Path.Combine(Plugin.Instance.PluginConfigFolder, "ffmpeg");
            var progress = new Progress<double>(p =>
            {
                DownloadProgress.Value = p;
                StatusBar.Message = $"正在下载 FFmpeg… {p:P0}";
            });

            var path = await FFmpegHelper.DownloadAsync(target, progress);
            Plugin.Instance.Settings.FFmpegPath = path;
            RefreshStatus();
        }
        catch (Exception ex)
        {
            StatusBar.Severity = InfoBarSeverity.Error;
            StatusBar.Message = "下载失败：" + ex.Message + "（也可以手动把 ffmpeg.exe 放到插件目录）";
        }
        finally
        {
            _downloading = false;
            DownloadButton.IsEnabled = true;
            DownloadProgress.IsVisible = false;
        }
    }
}

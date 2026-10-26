using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using ClassIsland.Core.Abstractions.Controls;
using ClassIsland.VideoPlayer.Models;

namespace ClassIsland.VideoPlayer.Controls;

/// <summary>
/// 视频播放组件的设置控件。
/// </summary>
public partial class VideoPlayerComponentSettingsControl : ComponentBase<VideoPlayerSettings>
{
    public VideoPlayerComponentSettingsControl()
    {
        InitializeComponent();
    }

    private async void OnBrowseClick(object? sender, RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel?.StorageProvider == null)
        {
            return;
        }

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "选择要播放的视频",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("视频文件")
                {
                    Patterns = new[] { "*.mp4", "*.mkv", "*.webm", "*.mov", "*.avi", "*.flv", "*.wmv", "*.m4v", "*.gif" }
                },
                FilePickerFileTypes.All
            }
        });

        if (files.Count > 0 && Settings != null)
        {
            var path = files[0].TryGetLocalPath();
            if (!string.IsNullOrEmpty(path))
            {
                Settings.Source = path;
            }
        }
    }
}

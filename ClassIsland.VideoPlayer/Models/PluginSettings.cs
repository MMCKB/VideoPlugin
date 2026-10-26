namespace ClassIsland.VideoPlayer.Models;

/// <summary>
/// 插件级别的全局设置（保存在插件配置目录下）。
/// </summary>
public class PluginSettings : NotifyObject
{
    private string _ffmpegPath = "";
    private int _maxRenderWidth = 640;
    private bool _autoDownloadFFmpeg = true;

    /// <summary>
    /// 自定义的 ffmpeg.exe 路径。留空时按“插件目录 → 系统 PATH”的顺序自动查找。
    /// </summary>
    public string FFmpegPath
    {
        get => _ffmpegPath;
        set => SetProperty(ref _ffmpegPath, value);
    }

    /// <summary>
    /// 解码时允许的最大渲染宽度（像素）。数值越小 CPU 占用越低。
    /// </summary>
    public int MaxRenderWidth
    {
        get => _maxRenderWidth;
        set => SetProperty(ref _maxRenderWidth, value);
    }

    /// <summary>
    /// 当自动查找失败时，是否允许在设置页中一键下载 FFmpeg。
    /// </summary>
    public bool AutoDownloadFFmpeg
    {
        get => _autoDownloadFFmpeg;
        set => SetProperty(ref _autoDownloadFFmpeg, value);
    }
}

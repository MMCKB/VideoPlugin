namespace ClassIsland.VideoPlayer.Models;

/// <summary>
/// 单个视频播放组件的设置。主界面上每一个该组件的实例都有独立的设置。
/// </summary>
public class VideoPlayerSettings : NotifyObject
{
    private string _source = "";
    private bool _loop = true;
    private bool _muted = false;
    private double _volume = 0.8;
    private double _displayHeight = 180;
    private double _cornerRadius = 8;
    private int _maxRenderWidth = 0;
    private bool _pauseWhenHidden = true;

    /// <summary>
    /// 视频来源。可以是本地文件路径，也可以是 http/https/rtsp 等 ffmpeg 支持的地址。
    /// </summary>
    public string Source
    {
        get => _source;
        set => SetProperty(ref _source, value);
    }

    /// <summary>
    /// 播放结束后是否从头循环播放。
    /// </summary>
    public bool Loop
    {
        get => _loop;
        set => SetProperty(ref _loop, value);
    }

    /// <summary>
    /// 是否静音。
    /// </summary>
    public bool Muted
    {
        get => _muted;
        set => SetProperty(ref _muted, value);
    }

    /// <summary>
    /// 音量，0~1。
    /// </summary>
    public double Volume
    {
        get => _volume;
        set => SetProperty(ref _volume, Math.Clamp(value, 0, 1));
    }

    /// <summary>
    /// 组件在主界面上占用的高度（像素）。
    /// </summary>
    public double DisplayHeight
    {
        get => _displayHeight;
        set => SetProperty(ref _displayHeight, Math.Clamp(value, 24, 2000));
    }

    /// <summary>
    /// 视频圆角半径（像素）。
    /// </summary>
    public double CornerRadius
    {
        get => _cornerRadius;
        set => SetProperty(ref _cornerRadius, Math.Clamp(value, 0, 200));
    }

    /// <summary>
    /// 解码时的最大渲染宽度。0 表示跟随插件全局设置。
    /// </summary>
    public int MaxRenderWidth
    {
        get => _maxRenderWidth;
        set => SetProperty(ref _maxRenderWidth, Math.Clamp(value, 0, 7680));
    }

    /// <summary>
    /// 当组件被隐藏（例如被隐藏规则隐藏、或主界面收起）时是否暂停播放。
    /// </summary>
    public bool PauseWhenHidden
    {
        get => _pauseWhenHidden;
        set => SetProperty(ref _pauseWhenHidden, value);
    }
}

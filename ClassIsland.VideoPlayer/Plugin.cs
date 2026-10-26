using System.IO;
using ClassIsland.Core.Abstractions;
using ClassIsland.Core.Attributes;
using ClassIsland.Core.Extensions.Registry;
using ClassIsland.Shared.Helpers;
using ClassIsland.VideoPlayer.Controls;
using ClassIsland.VideoPlayer.Models;
using ClassIsland.VideoPlayer.Views.SettingsPages;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace ClassIsland.VideoPlayer;

/// <summary>
/// 视频播放器插件入口。
/// </summary>
[PluginEntrance]
public class Plugin : PluginBase
{
    /// <summary>
    /// 当前插件实例，供组件与设置页读取全局设置。
    /// </summary>
    public static Plugin? Instance { get; private set; }

    public PluginSettings Settings { get; private set; } = new();

    public override void Initialize(HostBuilderContext context, IServiceCollection services)
    {
        Instance = this;

        var configPath = Path.Combine(PluginConfigFolder, "Settings.json");
        Settings = ConfigureFileHelper.LoadConfig<PluginSettings>(configPath);
        Settings.PropertyChanged += (_, _) => ConfigureFileHelper.SaveConfig(configPath, Settings);

        // 注册主界面（岛）上的视频组件
        services.AddComponent<VideoPlayerComponent, VideoPlayerComponentSettingsControl>();

        // 注册插件设置页（ffmpeg 路径等）
        services.AddSettingsPage<VideoPlayerSettingsPage>();
    }
}

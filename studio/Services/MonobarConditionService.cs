using Windows.Media.Control;

namespace Monolith.Studio.Services;

internal static class MonobarConditionService
{
    private static readonly object Gate = new();
    private static bool _mediaPlaying;
    private static bool _mediaRefreshRunning;
    private static DateTime _lastMediaRefresh = DateTime.MinValue;

    public static bool Evaluate(string condition) => condition switch
    {
        "always" or "" => true,
        "media-playing" => _mediaPlaying,
        "battery-low" => ReadBattery() is { HasBattery: true, Percent: <= 20 },
        "on-battery" => ReadBattery() is { HasBattery: true, Charging: false },
        "network-connected" => MonobarSystemStatus.GetNetworkStatus().Label == "Online",
        _ => false
    };

    public static void RefreshMediaState()
    {
        lock (Gate)
        {
            if (_mediaRefreshRunning || DateTime.UtcNow - _lastMediaRefresh < TimeSpan.FromSeconds(2)) return;
            _mediaRefreshRunning = true;
            _lastMediaRefresh = DateTime.UtcNow;
        }
        _ = RefreshMediaStateAsync();
    }

    private static async Task RefreshMediaStateAsync()
    {
        bool playing = false;
        try
        {
            var manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            playing = manager.GetCurrentSession()?.GetPlaybackInfo()?.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
        }
        catch { }
        lock (Gate)
        {
            _mediaPlaying = playing;
            _mediaRefreshRunning = false;
        }
    }

    private static (int Percent, bool Charging, bool HasBattery) ReadBattery() => MonobarSystemStatus.GetBatteryStatus();
}

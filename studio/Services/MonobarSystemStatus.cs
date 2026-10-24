using System;
using System.Linq;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Diagnostics;
using Windows.Devices.Radios;

namespace Monolith.Studio.Services;

internal static class MonobarSystemStatus
{
    private static readonly Guid AudioEndpointVolumeId = new("5CDF2C82-841E-4546-9722-0CF74078229A");
    private static readonly Guid AudioSessionManager2Id = new("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F");
    private static long _previousReceived;
    private static long _previousSent;
    private static DateTime _previousNetworkSample = DateTime.UtcNow;
    private static bool _hasNetworkSample;

    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E"), ClassInterface(ClassInterfaceType.None)]
    private class AudioDeviceEnumerator { }

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(int dataFlow, uint stateMask, out IntPtr devices);
        [PreserveSig] int GetDefaultAudioEndpoint(int dataFlow, int role, out IAudioDevice device);
        [PreserveSig] int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IAudioDevice device);
        [PreserveSig] int RegisterEndpointNotificationCallback(IntPtr callback);
        [PreserveSig] int UnregisterEndpointNotificationCallback(IntPtr callback);
    }

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioDevice
    {
        [PreserveSig] int Activate(ref Guid interfaceId, int context, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object instance);
        [PreserveSig] int OpenPropertyStore(int access, out IntPtr properties);
        [PreserveSig] int GetId(out IntPtr id);
        [PreserveSig] int GetState(out uint state);
    }

    [ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioEndpointVolume
    {
        [PreserveSig] int RegisterControlChangeNotify(IntPtr notify);
        [PreserveSig] int UnregisterControlChangeNotify(IntPtr notify);
        [PreserveSig] int GetChannelCount(out uint channelCount);
        [PreserveSig] int SetMasterVolumeLevel(float levelDb, ref Guid eventContext);
        [PreserveSig] int SetMasterVolumeLevelScalar(float level, ref Guid eventContext);
        [PreserveSig] int GetMasterVolumeLevel(out float levelDb);
        [PreserveSig] int GetMasterVolumeLevelScalar(out float level);
        [PreserveSig] int SetChannelVolumeLevel(uint channel, float levelDb, ref Guid eventContext);
        [PreserveSig] int SetChannelVolumeLevelScalar(uint channel, float level, ref Guid eventContext);
        [PreserveSig] int GetChannelVolumeLevel(uint channel, out float levelDb);
        [PreserveSig] int GetChannelVolumeLevelScalar(uint channel, out float level);
        [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, ref Guid eventContext);
        [PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
        [PreserveSig] int GetVolumeStepInfo(out uint step, out uint stepCount);
        [PreserveSig] int VolumeStepUp(ref Guid eventContext);
        [PreserveSig] int VolumeStepDown(ref Guid eventContext);
        [PreserveSig] int QueryHardwareSupport(out uint hardwareSupportMask);
        [PreserveSig] int GetVolumeRange(out float minDb, out float maxDb, out float incrementDb);
    }

    [ComImport, Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioSessionManager2
    {
        [PreserveSig] int GetAudioSessionControl(IntPtr sessionGuid, uint flags, out IntPtr sessionControl);
        [PreserveSig] int GetSimpleAudioVolume(IntPtr sessionGuid, uint flags, out IntPtr audioVolume);
        [PreserveSig] int GetSessionEnumerator(out IAudioSessionEnumerator sessionEnumerator);
        [PreserveSig] int RegisterSessionNotification(IntPtr notification);
        [PreserveSig] int UnregisterSessionNotification(IntPtr notification);
        [PreserveSig] int RegisterDuckNotification([MarshalAs(UnmanagedType.LPWStr)] string sessionId, IntPtr notification);
        [PreserveSig] int UnregisterDuckNotification(IntPtr notification);
    }

    [ComImport, Guid("E2F5BB11-0570-40CA-ACDD-3AA01277DEE8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioSessionEnumerator
    {
        [PreserveSig] int GetCount(out int count);
        [PreserveSig] int GetSession(int index, out IAudioSessionControl session);
    }

    [ComImport, Guid("F4B1A599-7266-4319-A8CA-E70ACB11E8CD"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioSessionControl
    {
        [PreserveSig] int GetState(out int state);
        [PreserveSig] int GetDisplayName(out IntPtr name);
        [PreserveSig] int SetDisplayName([MarshalAs(UnmanagedType.LPWStr)] string name, ref Guid eventContext);
        [PreserveSig] int GetIconPath(out IntPtr path);
        [PreserveSig] int SetIconPath([MarshalAs(UnmanagedType.LPWStr)] string path, ref Guid eventContext);
        [PreserveSig] int GetGroupingParam(out Guid groupingId);
        [PreserveSig] int SetGroupingParam(ref Guid groupingId, ref Guid eventContext);
        [PreserveSig] int RegisterAudioSessionNotification(IntPtr notification);
        [PreserveSig] int UnregisterAudioSessionNotification(IntPtr notification);
    }

    [ComImport, Guid("BFB7FF88-7239-4FC9-8FA2-07C950BE9C6D"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioSessionControl2
    {
        [PreserveSig] int GetState(out int state);
        [PreserveSig] int GetDisplayName(out IntPtr name);
        [PreserveSig] int SetDisplayName([MarshalAs(UnmanagedType.LPWStr)] string name, ref Guid eventContext);
        [PreserveSig] int GetIconPath(out IntPtr path);
        [PreserveSig] int SetIconPath([MarshalAs(UnmanagedType.LPWStr)] string path, ref Guid eventContext);
        [PreserveSig] int GetGroupingParam(out Guid groupingId);
        [PreserveSig] int SetGroupingParam(ref Guid groupingId, ref Guid eventContext);
        [PreserveSig] int RegisterAudioSessionNotification(IntPtr notification);
        [PreserveSig] int UnregisterAudioSessionNotification(IntPtr notification);
        [PreserveSig] int GetSessionIdentifier(out IntPtr identifier);
        [PreserveSig] int GetSessionInstanceIdentifier([MarshalAs(UnmanagedType.LPWStr)] out string identifier);
        [PreserveSig] int GetProcessId(out uint processId);
        [PreserveSig] int IsSystemSoundsSession();
        [PreserveSig] int SetDuckingPreference([MarshalAs(UnmanagedType.Bool)] bool optOut);
    }

    [ComImport, Guid("87CE5498-68D6-44E5-9215-6DA47EF883D8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ISimpleAudioVolume
    {
        [PreserveSig] int SetMasterVolume(float level, ref Guid eventContext);
        [PreserveSig] int GetMasterVolume(out float level);
        [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, ref Guid eventContext);
        [PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)] out bool muted);
    }

    internal sealed record AudioSourceVolume(string Id, string Name, int VolumePercent);

    [StructLayout(LayoutKind.Sequential)]
    private struct SystemPowerStatus
    {
        public byte ACLineStatus;
        public byte BatteryFlag;
        public byte BatteryLifePercent;
        public byte SystemStatusFlag;
        public uint BatteryLifeTime;
        public uint BatteryFullLifeTime;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetSystemPowerStatus(out SystemPowerStatus status);

    public static int GetVolumePercent()
    {
        try
        {
            var endpoint = GetAudioEndpoint();
            endpoint.GetMasterVolumeLevelScalar(out float scalar);
            return (int)Math.Round(scalar * 100);
        }
        catch { return 0; }
    }

    public static async Task<bool?> GetRadioStateAsync(RadioKind kind)
    {
        try
        {
            var radios = await Radio.GetRadiosAsync();
            var radio = radios.FirstOrDefault(item => item.Kind == kind);
            return radio is null ? null : radio.State == RadioState.On;
        }
        catch { return null; }
    }

    public static async Task<bool?> SetRadioStateAsync(RadioKind kind, bool enabled)
    {
        try
        {
            var access = await Radio.RequestAccessAsync();
            if (access != RadioAccessStatus.Allowed) return await GetRadioStateAsync(kind);
            var radios = await Radio.GetRadiosAsync();
            var radio = radios.FirstOrDefault(item => item.Kind == kind);
            if (radio is null) return null;
            await radio.SetStateAsync(enabled ? RadioState.On : RadioState.Off);
            return radio.State == RadioState.On;
        }
        catch { return await GetRadioStateAsync(kind); }
    }

    public static void SetVolumePercent(int percent)
    {
        try
        {
            var endpoint = GetAudioEndpoint();
            Guid context = Guid.Empty;
            endpoint.SetMasterVolumeLevelScalar(Math.Clamp(percent, 0, 100) / 100f, ref context);
        }
        catch { }
    }

    public static AudioSourceVolume[] GetAudioSourceVolumes()
    {
        try
        {
            var sessions = GetAudioSessionEnumerator();
            Marshal.ThrowExceptionForHR(sessions.GetCount(out int count));
            var result = new List<AudioSourceVolume>();
            for (int index = 0; index < count; index++)
            {
                if (sessions.GetSession(index, out var control) < 0 || control is not IAudioSessionControl2 control2)
                    continue;
                if (control2.GetState(out int state) < 0 || state == 2) // Expired sessions are no longer useful mixer sources.
                    continue;
                if (control2.GetSessionInstanceIdentifier(out string id) < 0 || string.IsNullOrWhiteSpace(id))
                    continue;
                if (control is not ISimpleAudioVolume volume || volume.GetMasterVolume(out float level) < 0)
                    continue;

                string name = "Audio source";
                if (control2.IsSystemSoundsSession() == 0)
                    name = "System sounds";
                else if (control2.GetProcessId(out uint processId) == 0 && processId > 0)
                {
                    try
                    {
                        using var process = Process.GetProcessById((int)processId);
                        name = string.IsNullOrWhiteSpace(process.ProcessName) ? name : process.ProcessName;
                    }
                    catch { }
                }
                result.Add(new AudioSourceVolume(id, name, (int)Math.Round(Math.Clamp(level, 0f, 1f) * 100)));
            }
            return result.OrderBy(source => source.Name, StringComparer.CurrentCultureIgnoreCase).ToArray();
        }
        catch { return Array.Empty<AudioSourceVolume>(); }
    }

    public static void SetAudioSourceVolume(string sessionId, int percent)
    {
        try
        {
            var sessions = GetAudioSessionEnumerator();
            Marshal.ThrowExceptionForHR(sessions.GetCount(out int count));
            for (int index = 0; index < count; index++)
            {
                if (sessions.GetSession(index, out var control) < 0 || control is not IAudioSessionControl2 control2)
                    continue;
                if (control2.GetSessionInstanceIdentifier(out string id) < 0 || !string.Equals(id, sessionId, StringComparison.Ordinal))
                    continue;
                if (control is ISimpleAudioVolume volume)
                {
                    Guid context = Guid.Empty;
                    volume.SetMasterVolume(Math.Clamp(percent, 0, 100) / 100f, ref context);
                }
                return;
            }
        }
        catch { }
    }

    private static IAudioSessionEnumerator GetAudioSessionEnumerator()
    {
        var enumerator = (IAudioDeviceEnumerator)new AudioDeviceEnumerator();
        Marshal.ThrowExceptionForHR(enumerator.GetDefaultAudioEndpoint(0, 1, out var device));
        var interfaceId = AudioSessionManager2Id;
        Marshal.ThrowExceptionForHR(device.Activate(ref interfaceId, 23, IntPtr.Zero, out var instance));
        var manager = (IAudioSessionManager2)instance;
        Marshal.ThrowExceptionForHR(manager.GetSessionEnumerator(out var sessions));
        return sessions;
    }

    public static (string Download, string Upload) GetNetworkRates()
    {
        long received = 0, sent = 0;
        try
        {
            foreach (var network in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (network.OperationalStatus != OperationalStatus.Up || network.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel)
                    continue;
                var stats = network.GetIPv4Statistics();
                received += stats.BytesReceived;
                sent += stats.BytesSent;
            }
        }
        catch { }

        var now = DateTime.UtcNow;
        double elapsed = (now - _previousNetworkSample).TotalSeconds;
        bool needsBaseline = !_hasNetworkSample || elapsed > 5;
        double seconds = Math.Max(0.1, elapsed);
        double download = Math.Max(0, received - _previousReceived) / seconds;
        double upload = Math.Max(0, sent - _previousSent) / seconds;
        _previousReceived = received;
        _previousSent = sent;
        _previousNetworkSample = now;
        _hasNetworkSample = true;
        if (needsBaseline) return ("0 KB/s", "0 KB/s");
        return (FormatRate(download), FormatRate(upload));
    }

    public static int? GetBrightnessPercent()
    {
        try
        {
            dynamic locator = Activator.CreateInstance(Type.GetTypeFromProgID("WbemScripting.SWbemLocator")!);
            dynamic services = locator.ConnectServer(".", @"root\wmi");
            dynamic monitors = services.ExecQuery("SELECT * FROM WmiMonitorBrightness");
            foreach (dynamic monitor in monitors) return Convert.ToInt32(monitor.CurrentBrightness);
        }
        catch { }
        return null;
    }

    public static void SetBrightnessPercent(int percent)
    {
        try
        {
            dynamic locator = Activator.CreateInstance(Type.GetTypeFromProgID("WbemScripting.SWbemLocator")!);
            dynamic services = locator.ConnectServer(".", @"root\wmi");
            dynamic monitors = services.ExecQuery("SELECT * FROM WmiMonitorBrightnessMethods");
            foreach (dynamic monitor in monitors) monitor.WmiSetBrightness(1u, (byte)Math.Clamp(percent, 0, 100));
        }
        catch { }
    }

    private static string FormatRate(double bytesPerSecond) => bytesPerSecond >= 1024 * 1024
        ? $"{bytesPerSecond / (1024 * 1024):0.0} MB/s"
        : $"{bytesPerSecond / 1024:0} KB/s";

    private static IAudioEndpointVolume GetAudioEndpoint()
    {
        var enumerator = (IAudioDeviceEnumerator)new AudioDeviceEnumerator();
        Marshal.ThrowExceptionForHR(enumerator.GetDefaultAudioEndpoint(0, 1, out var device));
        var interfaceId = AudioEndpointVolumeId;
        Marshal.ThrowExceptionForHR(device.Activate(ref interfaceId, 23, IntPtr.Zero, out var instance));
        return (IAudioEndpointVolume)instance;
    }

    public static (string Glyph, string Label, string Description) GetNetworkStatus()
    {
        try
        {
            var active = NetworkInterface.GetAllNetworkInterfaces()
                .Where(network => network.OperationalStatus == OperationalStatus.Up
                    && network.NetworkInterfaceType is not NetworkInterfaceType.Loopback and not NetworkInterfaceType.Tunnel)
                .ToList();
            bool wifi = active.Any(network => network.NetworkInterfaceType == NetworkInterfaceType.Wireless80211);
            bool connected = active.Count > 0;
            return (connected ? (wifi ? "\uE701" : "\uE774") : "\uE704", connected ? "Online" : "Offline", connected ? "Network connected" : "No network connection");
        }
        catch
        {
            return ("\uE704", "Network unavailable", "Network status unavailable");
        }
    }

    public static (int Percent, bool Charging, bool HasBattery) GetBatteryStatus()
    {
        if (!GetSystemPowerStatus(out var status)) return (100, false, false);
        bool hasBattery = status.BatteryFlag != 128 && status.BatteryLifePercent != 255;
        return (hasBattery ? status.BatteryLifePercent : 100, hasBattery && status.ACLineStatus == 1, hasBattery);
    }
}

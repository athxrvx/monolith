using System;
using System.Linq;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Windows.Devices.Radios;

namespace Monolith.Studio.Services;

internal static class MonobarSystemStatus
{
    private static readonly Guid AudioEndpointVolumeId = new("5CDF2C82-841E-4546-9722-0CF74078229A");
    private static long _previousReceived;
    private static long _previousSent;
    private static DateTime _previousNetworkSample = DateTime.UtcNow;

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
        double seconds = Math.Max(0.1, (now - _previousNetworkSample).TotalSeconds);
        double download = Math.Max(0, received - _previousReceived) / seconds;
        double upload = Math.Max(0, sent - _previousSent) / seconds;
        _previousReceived = received;
        _previousSent = sent;
        _previousNetworkSample = now;
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

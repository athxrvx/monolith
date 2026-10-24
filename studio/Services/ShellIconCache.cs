using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace Monolith.Studio.Services;

/// <summary>Gets Windows Shell icons and stores small ICO files for WinUI Image controls.</summary>
internal static class ShellIconCache
{
    private const uint ShgfiIcon = 0x00000100;
    private const uint ShgfiLargeIcon = 0x00000000;
    private const uint ShgfiUseFileAttributes = 0x00000010;
    private const uint FileAttributeNormal = 0x00000080;
    private const uint DibRgbColors = 0;
    private static readonly string CacheDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Monolith", "IconCache");

    public static string? GetIconPath(string target)
    {
        if (target.StartsWith("explorer|", StringComparison.OrdinalIgnoreCase))
            target = target["explorer|".Length..];
        if (target.StartsWith("shell:", StringComparison.OrdinalIgnoreCase))
        {
            string namespaceCacheName = "v2_" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(target.ToUpperInvariant()))) + ".ico";
            string namespaceCachePath = Path.Combine(CacheDirectory, namespaceCacheName);
            try
            {
                if (File.Exists(namespaceCachePath)) return namespaceCachePath;
                Directory.CreateDirectory(CacheDirectory);
                if (ExtractNamespaceIcon(target, namespaceCachePath)) return namespaceCachePath;
            }
            catch { }
        }

        string? path = null;
        int? iconIndex = null;
        if (Path.GetExtension(target).Equals(".lnk", StringComparison.OrdinalIgnoreCase))
            (path, iconIndex) = ResolveShortcutIcon(target);
        path ??= ResolveTarget(target);
        if (path is null) return null;
        string cacheKey = $"v2|{path.ToUpperInvariant()}|{iconIndex?.ToString() ?? "default"}";
        string cacheName = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(cacheKey))) + ".ico";
        string cachePath = Path.Combine(CacheDirectory, cacheName);
        try
        {
            if (File.Exists(cachePath)) return cachePath;
            Directory.CreateDirectory(CacheDirectory);
            if (iconIndex is int index && ExtractIconByIndex(path, index, cachePath)) return cachePath;
            if (ExtractIcon(path, cachePath)) return cachePath;
        }
        catch { }
        return null;
    }

    private static (string? Path, int? Index) ResolveShortcutIcon(string shortcutPath)
    {
        object? shellObject = null;
        object? shortcutObject = null;
        try
        {
            var shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType is null) return (null, null);
            shellObject = Activator.CreateInstance(shellType);
            if (shellObject is null) return (null, null);
            dynamic shell = shellObject;
            shortcutObject = shell.CreateShortcut(shortcutPath);
            if (shortcutObject is null) return (null, null);
            dynamic shortcut = shortcutObject;
            string location = Environment.ExpandEnvironmentVariables(Convert.ToString(shortcut.IconLocation) ?? string.Empty).Trim();

            int? index = null;
            int comma = location.LastIndexOf(',');
            if (comma > 0 && int.TryParse(location[(comma + 1)..].Trim(), out int parsedIndex))
            {
                index = parsedIndex;
                location = location[..comma].Trim();
            }
            location = Environment.ExpandEnvironmentVariables(location.Trim().Trim('"'));
            if (location.Length > 0 && File.Exists(location)) return (location, index);

            // Many Start Menu links leave IconLocation empty. The shell uses the target
            // executable's embedded icon in that case, so use the same source.
            string target = Environment.ExpandEnvironmentVariables(Convert.ToString(shortcut.TargetPath) ?? string.Empty)
                .Trim().Trim('"');
            if (target.Length > 0 && File.Exists(target)) return (target, null);
            string? resolvedTarget = ResolveTarget(target);
            return resolvedTarget is null ? (null, null) : (resolvedTarget, null);
        }
        catch { return (null, null); }
        finally
        {
            if (shortcutObject is not null && Marshal.IsComObject(shortcutObject)) Marshal.FinalReleaseComObject(shortcutObject);
            if (shellObject is not null && Marshal.IsComObject(shellObject)) Marshal.FinalReleaseComObject(shellObject);
        }
    }

    private static string? ResolveTarget(string target)
    {
        if (string.IsNullOrWhiteSpace(target) || target.Contains(':') && !Path.IsPathRooted(target)) return null;
        if (File.Exists(target) || Directory.Exists(target)) return target;
        var buffer = new StringBuilder(32768);
        uint length = SearchPath(null, target, null, (uint)buffer.Capacity, buffer, out _);
        return length > 0 && length < buffer.Capacity ? buffer.ToString() : null;
    }

    private static bool ExtractIcon(string path, string destination)
    {
        var info = new ShellFileInfo { DisplayName = string.Empty, TypeName = string.Empty };
        uint flags = ShgfiIcon | ShgfiLargeIcon;
        uint attributes = FileAttributeNormal;
        if (Directory.Exists(path) && !IsDriveRoot(path))
        {
            // Query by directory attributes so redirected known folders still get
            // the standard folder icon when the shell cannot resolve their path.
            flags |= ShgfiUseFileAttributes;
            attributes = 0x00000010; // FILE_ATTRIBUTE_DIRECTORY
        }
        else if (!File.Exists(path) && !Directory.Exists(path))
        {
            flags |= ShgfiUseFileAttributes;
        }
        IntPtr result = ShellGetFileInfo(path, attributes, out info, (uint)Marshal.SizeOf<ShellFileInfo>(), flags);
        if (result == IntPtr.Zero || info.Icon == IntPtr.Zero) return false;

        try { return SaveIcon(info.Icon, destination); }
        finally { DestroyIcon(info.Icon); }
    }

    private static bool IsDriveRoot(string path)
    {
        string? root = Path.GetPathRoot(path);
        return !string.IsNullOrEmpty(root)
            && string.Equals(Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar),
                root.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase);
    }

    private static bool ExtractNamespaceIcon(string target, string destination)
    {
        const uint ShgfiPidl = 0x00000008;
        const string AppsFolderPrefix = "shell:AppsFolder\\";
        const string AppsFolderClsid = "::{4234d49b-0245-4df3-b780-3893943456e1}";
        var parsingNames = new System.Collections.Generic.List<string> { target };
        if (target.StartsWith(AppsFolderPrefix, StringComparison.OrdinalIgnoreCase))
        {
            string appId = target[AppsFolderPrefix.Length..];
            parsingNames.Add($"{AppsFolderClsid}\\{appId}");
        }

        foreach (string parsingName in parsingNames)
        {
            IntPtr pidl = IntPtr.Zero;
            try
            {
                int parsed = ShellParseDisplayName(parsingName, IntPtr.Zero, out pidl, 0, out _);
                if (parsed < 0 || pidl == IntPtr.Zero) continue;
                IntPtr result = ShellGetFileInfoFromPidl(pidl, 0, out var info,
                    (uint)Marshal.SizeOf<ShellFileInfo>(), ShgfiPidl | ShgfiIcon | ShgfiLargeIcon);
                if (result == IntPtr.Zero || info.Icon == IntPtr.Zero) continue;
                try
                {
                    if (SaveIcon(info.Icon, destination)) return true;
                }
                finally { DestroyIcon(info.Icon); }
            }
            catch { /* Try the alternate shell parsing name below. */ }
            finally
            {
                if (pidl != IntPtr.Zero) Marshal.FreeCoTaskMem(pidl);
            }
        }
        return false;
    }

    private static bool ExtractIconByIndex(string path, int index, string destination)
    {
        IntPtr[] large = new IntPtr[1];
        IntPtr[] small = new IntPtr[1];
        try
        {
            if (ExtractIconEx(path, index, large, small, 1) == 0) return false;
            IntPtr icon = large[0] != IntPtr.Zero ? large[0] : small[0];
            return icon != IntPtr.Zero && SaveIcon(icon, destination);
        }
        finally
        {
            if (large[0] != IntPtr.Zero) DestroyIcon(large[0]);
            if (small[0] != IntPtr.Zero) DestroyIcon(small[0]);
        }
    }

    private static bool SaveIcon(IntPtr icon, string destination)
    {
        IntPtr colorBitmap = IntPtr.Zero;
        IntPtr maskBitmap = IntPtr.Zero;
        IntPtr deviceContext = IntPtr.Zero;
        try
        {
            if (!GetIconInfo(icon, out var iconInfo)) return false;
            colorBitmap = iconInfo.ColorBitmap;
            maskBitmap = iconInfo.MaskBitmap;
            if (colorBitmap == IntPtr.Zero || GetObject(colorBitmap, Marshal.SizeOf<NativeBitmap>(), out var bitmap) == 0)
                return false;

            int width = Math.Clamp(bitmap.Width, 1, 256);
            int height = Math.Clamp(bitmap.Height, 1, 256);
            var header = new BitmapInfoHeader
            {
                Size = (uint)Marshal.SizeOf<BitmapInfoHeader>(),
                Width = width,
                Height = height,
                Planes = 1,
                BitCount = 32,
                Compression = 0,
                SizeImage = (uint)(width * height * 4)
            };
            var bitmapInfo = new BitmapInfo { Header = header };
            byte[] pixels = new byte[width * height * 4];
            deviceContext = GetDC(IntPtr.Zero);
            if (deviceContext == IntPtr.Zero || GetDIBits(deviceContext, colorBitmap, 0, (uint)height,
                    pixels, ref bitmapInfo, DibRgbColors) == 0)
                return false;

            bool hasAlpha = false;
            for (int i = 3; i < pixels.Length; i += 4) hasAlpha |= pixels[i] != 0;
            if (!hasAlpha)
                for (int i = 3; i < pixels.Length; i += 4) pixels[i] = 255;

            int maskStride = ((width + 31) / 32) * 4;
            byte[] mask = new byte[maskStride * height];
            uint imageBytes = (uint)(Marshal.SizeOf<BitmapInfoHeader>() + pixels.Length + mask.Length);
            using var stream = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None);
            using var writer = new BinaryWriter(stream);
            writer.Write((ushort)0); writer.Write((ushort)1); writer.Write((ushort)1);
            writer.Write((byte)(width == 256 ? 0 : width)); writer.Write((byte)(height == 256 ? 0 : height));
            writer.Write((byte)0); writer.Write((byte)0); writer.Write((ushort)1); writer.Write((ushort)32);
            writer.Write(imageBytes); writer.Write((uint)22);
            writer.Write(header.Size); writer.Write(header.Width); writer.Write(header.Height * 2);
            writer.Write(header.Planes); writer.Write(header.BitCount); writer.Write(header.Compression);
            writer.Write((uint)(pixels.Length + mask.Length)); writer.Write(0); writer.Write(0); writer.Write(0); writer.Write(0);
            writer.Write(pixels); writer.Write(mask);
            return true;
        }
        finally
        {
            if (deviceContext != IntPtr.Zero) ReleaseDC(IntPtr.Zero, deviceContext);
            if (colorBitmap != IntPtr.Zero) DeleteObject(colorBitmap);
            if (maskBitmap != IntPtr.Zero) DeleteObject(maskBitmap);
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ShellFileInfo
    {
        public IntPtr Icon;
        public int IconIndex;
        public uint Attributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string DisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string TypeName;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IconInfo
    {
        [MarshalAs(UnmanagedType.Bool)] public bool IsIcon;
        public uint HotspotX;
        public uint HotspotY;
        public IntPtr MaskBitmap;
        public IntPtr ColorBitmap;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeBitmap
    {
        public int Type;
        public int Width;
        public int Height;
        public int WidthBytes;
        public ushort Planes;
        public ushort BitsPixel;
        public IntPtr Bits;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfoHeader
    {
        public uint Size;
        public int Width;
        public int Height;
        public ushort Planes;
        public ushort BitCount;
        public uint Compression;
        public uint SizeImage;
        public int XPelsPerMeter;
        public int YPelsPerMeter;
        public uint ColorsUsed;
        public uint ColorsImportant;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfo
    {
        public BitmapInfoHeader Header;
        public uint Color;
    }

    [DllImport("shell32.dll", EntryPoint = "SHGetFileInfoW", CharSet = CharSet.Unicode)]
    private static extern IntPtr ShellGetFileInfo(string path, uint attributes, out ShellFileInfo info, uint infoSize, uint flags);
    [DllImport("shell32.dll", EntryPoint = "SHGetFileInfoW", CharSet = CharSet.Unicode)]
    private static extern IntPtr ShellGetFileInfoFromPidl(IntPtr pidl, uint attributes, out ShellFileInfo info, uint infoSize, uint flags);
    [DllImport("shell32.dll", EntryPoint = "SHParseDisplayName", CharSet = CharSet.Unicode)]
    private static extern int ShellParseDisplayName(string name, IntPtr bindContext, out IntPtr itemIdList, uint attributesIn, out uint attributesOut);
    [DllImport("shell32.dll", EntryPoint = "ExtractIconExW", CharSet = CharSet.Unicode)]
    private static extern uint ExtractIconEx(string file, int index, [Out] IntPtr[] largeIcons, [Out] IntPtr[] smallIcons, uint iconCount);
    [DllImport("user32.dll", EntryPoint = "GetIconInfo")] private static extern bool GetIconInfo(IntPtr icon, out IconInfo info);
    [DllImport("user32.dll")] private static extern bool DestroyIcon(IntPtr icon);
    [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr hwnd, IntPtr dc);
    [DllImport("gdi32.dll", EntryPoint = "GetObjectW", CharSet = CharSet.Unicode)] private static extern int GetObject(IntPtr handle, int size, out NativeBitmap bitmap);
    [DllImport("gdi32.dll")] private static extern int GetDIBits(IntPtr dc, IntPtr bitmap, uint startScan, uint scanLines, [Out] byte[] bits, ref BitmapInfo info, uint usage);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr handle);
    [DllImport("kernel32.dll", EntryPoint = "SearchPathW", CharSet = CharSet.Unicode)] private static extern uint SearchPath(string? path, string fileName, string? extension, uint bufferLength, StringBuilder buffer, out IntPtr filePart);
}

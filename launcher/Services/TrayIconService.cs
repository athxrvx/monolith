using System;
using System.Drawing;
using System.Windows.Forms;
using Monolith.ViewModels;

namespace Monolith.Services;

public sealed class TrayIconService : IDisposable
{
    private readonly NotifyIcon _notifyIcon;
    private readonly MonolithViewModel _viewModel;
    private readonly Action _showMainWindow;
    private readonly Action _showLauncher;
    private readonly Action _exitApp;

    public TrayIconService(
        MonolithViewModel viewModel,
        Action showMainWindow,
        Action showLauncher,
        Action exitApp)
    {
        _viewModel = viewModel;
        _showMainWindow = showMainWindow;
        _showLauncher = showLauncher;
        _exitApp = exitApp;

        _notifyIcon = new NotifyIcon
        {
            Text = "Monolith Desktop Environment",
            Visible = true,
            Icon = CreateMonolithTrayIcon()
        };

        _notifyIcon.DoubleClick += (_, _) => _showMainWindow();
        _notifyIcon.Click += (s, e) =>
        {
            if (e is MouseEventArgs me && me.Button == MouseButtons.Left)
            {
                _showMainWindow();
            }
        };

        RebuildContextMenu();

        _viewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName is nameof(MonolithViewModel.IsTilerEnabled) or nameof(MonolithViewModel.IsLauncherEnabled))
            {
                RebuildContextMenu();
            }
        };
    }

    private void RebuildContextMenu()
    {
        var menu = new ContextMenuStrip();

        var openItem = new ToolStripMenuItem("Open Monolith Dashboard", null, (_, _) => _showMainWindow())
        {
            Font = new Font(menu.Font, FontStyle.Bold)
        };
        menu.Items.Add(openItem);

        var launcherItem = new ToolStripMenuItem("Open Launcher (Alt + Space)", null, (_, _) => _showLauncher());
        menu.Items.Add(launcherItem);

        menu.Items.Add(new ToolStripSeparator());

        var tilerToggle = new ToolStripMenuItem("Monolith Tiler Enabled")
        {
            Checked = _viewModel.IsTilerEnabled,
            CheckOnClick = true
        };
        tilerToggle.Click += (_, _) =>
        {
            _viewModel.IsTilerEnabled = tilerToggle.Checked;
        };
        menu.Items.Add(tilerToggle);

        var launcherToggle = new ToolStripMenuItem("Monolith Launcher Enabled")
        {
            Checked = _viewModel.IsLauncherEnabled,
            CheckOnClick = true
        };
        launcherToggle.Click += (_, _) =>
        {
            _viewModel.IsLauncherEnabled = launcherToggle.Checked;
        };
        menu.Items.Add(launcherToggle);

        menu.Items.Add(new ToolStripSeparator());

        var restartItem = new ToolStripMenuItem("Restart Tiler", null, async (_, _) =>
        {
            await _viewModel.RestartTilerAsync();
        });
        menu.Items.Add(restartItem);

        var exitItem = new ToolStripMenuItem("Exit Monolith", null, (_, _) => _exitApp());
        menu.Items.Add(exitItem);

        _notifyIcon.ContextMenuStrip = menu;
    }

    private static Icon CreateMonolithTrayIcon()
    {
        // 16x16 crisp vector-drawn tray icon (cyan 'M' badge)
        using var bmp = new Bitmap(16, 16);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using var brush = new SolidBrush(Color.FromArgb(0, 229, 255));
            g.FillRectangle(brush, 1, 1, 14, 14);

            using var font = new Font("Arial", 8.5f, FontStyle.Bold, GraphicsUnit.Pixel);
            using var textBrush = new SolidBrush(Color.FromArgb(10, 10, 12));
            var sf = new StringFormat
            {
                Alignment = StringAlignment.Center,
                LineAlignment = StringAlignment.Center
            };
            g.DrawString("M", font, textBrush, new RectangleF(0, 1, 16, 15), sf);
        }

        IntPtr hIcon = bmp.GetHicon();
        return Icon.FromHandle(hIcon);
    }

    public void Dispose()
    {
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
    }
}


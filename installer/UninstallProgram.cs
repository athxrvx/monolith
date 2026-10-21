using System.Diagnostics;
using System.Drawing;

internal static class UninstallProgram
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.Run(new UninstallForm());
    }
}

internal sealed class UninstallForm : Form
{
    private readonly CheckBox _removeData;

    internal UninstallForm()
    {
        Text = "Monolith Uninstaller";
        ClientSize = new Size(440, 220);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.FromArgb(32, 32, 32);
        ForeColor = Color.White;

        var title = new Label { Text = "Uninstall Monolith", Font = new Font("Segoe UI", 16, FontStyle.Bold), AutoSize = true, Location = new Point(24, 22) };
        var description = new Label { Text = "Remove Monolith and its Start menu shortcut from this user account.", AutoSize = true, Location = new Point(26, 66), ForeColor = Color.Gainsboro };
        _removeData = new CheckBox { Text = "Also remove settings, logs, and cached data", AutoSize = true, Location = new Point(26, 112) };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Location = new Point(220, 166), Size = new Size(88, 32), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(48, 48, 48), ForeColor = Color.White };
        var uninstall = new Button { Text = "Uninstall", Location = new Point(316, 166), Size = new Size(98, 32), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(0, 170, 220), ForeColor = Color.Black };
        uninstall.Click += UninstallClicked;
        Controls.AddRange([title, description, _removeData, cancel, uninstall]);
        CancelButton = cancel;
        AcceptButton = uninstall;
    }

    private void UninstallClicked(object? sender, EventArgs e)
    {
        var answer = MessageBox.Show(this,
            "Uninstall Monolith from this PC? This closes Monolith and removes the application files.",
            "Confirm uninstall", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
        if (answer != DialogResult.Yes) return;

        string installDirectory = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        try
        {
            InstallCommon.Uninstall(installDirectory, _removeData.Checked);
            ScheduleInstallDirectoryRemoval(installDirectory);
            MessageBox.Show(this, "Monolith has been uninstalled. Application files will finish clearing shortly.",
                "Monolith Uninstaller", MessageBoxButtons.OK, MessageBoxIcon.Information);
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Uninstall failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private static void ScheduleInstallDirectoryRemoval(string installDirectory)
    {
        string arguments = "/c timeout /t 2 /nobreak >nul & rmdir /s /q \"" + installDirectory + "\"";
        Process.Start(new ProcessStartInfo("cmd.exe", arguments)
        {
            WorkingDirectory = Path.GetTempPath(),
            CreateNoWindow = true,
            UseShellExecute = false,
            WindowStyle = ProcessWindowStyle.Hidden
        });
    }
}

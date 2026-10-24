using System.Reflection;
using System.Drawing;

internal static class SetupProgram
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.Run(new SetupForm());
    }
}

internal sealed class SetupForm : Form
{
    private readonly TextBox _path;
    private readonly Button _install;
    private readonly ProgressBar _progress;
    private readonly Label _status;
    private readonly bool _isUpdate;

    internal SetupForm()
    {
        _isUpdate = InstallCommon.IsInstalled;
        Text = _isUpdate ? "Monolith Update" : "Monolith Setup";
        ClientSize = new Size(520, 300);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.FromArgb(32, 32, 32);
        ForeColor = Color.White;

        var title = new Label { Text = _isUpdate ? "Update Monolith" : "Install Monolith", Font = new Font("Segoe UI", 18, FontStyle.Bold), AutoSize = true, Location = new Point(28, 24) };
        var description = new Label { Text = _isUpdate ? "Replace the installed Monolith files with this version. Your settings and workspaces will be kept." : "A native Windows desktop environment with scrolling window management.", AutoSize = true, Location = new Point(30, 68), ForeColor = Color.Gainsboro };
        var pathLabel = new Label { Text = _isUpdate ? "Update existing installation" : "Install for the current Windows user", AutoSize = true, Location = new Point(30, 116) };
        _path = new TextBox { Text = InstallCommon.DefaultInstallDirectory, Location = new Point(30, 142), Width = 455, ReadOnly = true, BackColor = Color.FromArgb(48, 48, 48), ForeColor = Color.White, BorderStyle = BorderStyle.FixedSingle };
        _status = new Label { Text = "Settings will be stored in " + InstallCommon.ConfigDirectory, Location = new Point(30, 178), Size = new Size(455, 38), ForeColor = Color.Silver };
        _progress = new ProgressBar { Location = new Point(30, 222), Size = new Size(455, 8), Visible = false };
        _install = new Button { Text = _isUpdate ? "Update" : "Install", Location = new Point(365, 246), Size = new Size(120, 34), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(0, 170, 220), ForeColor = Color.Black };
        _install.Click += InstallClicked;
        Controls.AddRange([title, description, pathLabel, _path, _status, _progress, _install]);
    }

    private async void InstallClicked(object? sender, EventArgs e)
    {
        _install.Enabled = false;
        _progress.Visible = true;
        _status.Text = _isUpdate ? "Updating Monolith…" : "Installing Monolith…";
        string destination = _path.Text;
        try
        {
            await Task.Run(() =>
            {
                using Stream payload = Assembly.GetExecutingAssembly().GetManifestResourceStream("Monolith.Payload.zip")
                    ?? throw new InvalidOperationException("The installation files are missing from this setup package.");
                InstallCommon.Install(payload, destination, value => BeginInvoke(new Action(() => _progress.Value = value)));
            });
            _status.Text = _isUpdate ? "Updated. Monolith is available from the Start menu." : "Installed. Monolith is available from the Start menu.";
            _install.Text = "Close";
            _install.Enabled = true;
            _install.Click -= InstallClicked;
            _install.Click += (_, _) => Close();
            string action = _isUpdate ? "updated" : "installed";
            if (MessageBox.Show(this, $"Monolith {action} successfully. Start it now?", "Monolith Setup", MessageBoxButtons.YesNo, MessageBoxIcon.Information) == DialogResult.Yes)
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Path.Combine(destination, "Monolith.exe")) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            _status.Text = "Installation failed.";
            MessageBox.Show(this, ex.Message, "Monolith Setup", MessageBoxButtons.OK, MessageBoxIcon.Error);
            _install.Enabled = true;
        }
    }
}

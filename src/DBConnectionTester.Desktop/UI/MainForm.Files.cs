using System.Diagnostics;

namespace DBConnectionTester.UI;

public sealed partial class MainForm
{
    private void BrowseSqliteFile()
    {
        using var dialog = new OpenFileDialog
        {
            Filter = "Bancos SQLite (*.db;*.sqlite;*.sqlite3)|*.db;*.sqlite;*.sqlite3|Todos os arquivos (*.*)|*.*",
            CheckFileExists = true,
            FileName = Path.GetFileName(txtSqliteFile.Text),
            InitialDirectory = GetExistingDirectory(txtSqliteFile.Text)
        };
        if (dialog.ShowDialog(this) == DialogResult.OK)
            txtSqliteFile.Text = dialog.FileName;
    }

    private void HideToTray(string? balloonText = null)
    {
        trayIcon.Visible = true;
        Hide();
        ShowInTaskbar = false;
        if (string.IsNullOrWhiteSpace(balloonText))
            return;
        trayIcon.BalloonTipTitle = "DB Connection Tester";
        trayIcon.BalloonTipText = balloonText;
        trayIcon.BalloonTipIcon = ToolTipIcon.Info;
        trayIcon.ShowBalloonTip(2000);
    }

    private void ShowPanel()
    {
        ShowInTaskbar = true;
        Show();
        if (WindowState == FormWindowState.Minimized)
            WindowState = FormWindowState.Normal;
        Activate();
        BringToFront();
    }

    private void OpenOutputFolder()
    {
        var directory = !string.IsNullOrWhiteSpace(currentCsvPath)
            ? Path.GetDirectoryName(currentCsvPath)
            : applicationSettings.LegacyOutputEnabled
                ? applicationSettings.LegacyOutputDirectory
                : Path.GetDirectoryName(applicationStore.Descriptor.DatabasePath);
        if (!string.IsNullOrWhiteSpace(directory) && Directory.Exists(directory))
            Process.Start(new ProcessStartInfo("explorer.exe", directory) { UseShellExecute = true });
    }

    private static void OpenFile(string path)
    {
        if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
    }

    private static string GetExistingDirectory(string path)
    {
        var directory = Path.GetDirectoryName(path);
        return !string.IsNullOrWhiteSpace(directory) && Directory.Exists(directory)
            ? directory
            : Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
    }

    private void SafeTrayText(string text) => trayIcon.Text = text.Length <= 63 ? text : text[..63];
}

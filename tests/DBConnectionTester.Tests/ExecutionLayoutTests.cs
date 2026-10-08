using System.Drawing;
using System.Reflection;
using System.Windows.Forms;
using DBConnectionTester.Models;
using DBConnectionTester.Services.Storage;
using DBConnectionTester.UI;

namespace DBConnectionTester.Tests;

[Collection("WinForms UI")]
public sealed class ExecutionLayoutTests
{
    [Fact]
    public async Task ConnectionStartsImmediatelyBelowExecutionStatus()
    {
        var directory = Path.Combine(Path.GetTempPath(), "DBCT-layout", Guid.NewGuid().ToString("N"));
        var store = await SqliteApplicationStore.OpenOrCreateAsync(Path.Combine(directory, "data.db"), StorageScope.Custom);
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                using var form = new MainForm(store, ApplicationSettings.Default);
                form.ClientSize = new Size(1100, 820);
                form.Show(); form.NavigateTo("Nova execução");
                System.Windows.Forms.Application.DoEvents();
                var connection = Descendants(form).OfType<GroupBox>().Single(c => c.Text == "Conexão");
                var status = (Label)typeof(MainForm).GetField("lblStatus", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(form)!;
                var output = Environment.GetEnvironmentVariable("DBCT_UI_SNAPSHOT_DIR");
                if (output is not null)
                {
                    Directory.CreateDirectory(output);
                    File.WriteAllLines(Path.Combine(output, "execution.layout"), Descendants(form).Where(c => c.Visible)
                        .Select(c => $"{c.GetType().Name} [{c.Text}] bounds={c.Bounds} screen={c.PointToScreen(Point.Empty)} parent={c.Parent?.GetType().Name}"));
                }
                var gap = connection.PointToScreen(Point.Empty).Y - status.PointToScreen(new Point(0, status.Height)).Y;
                Assert.InRange(gap, 0, 40 * form.DeviceDpi / 96);
                form.Close();
            }
            catch (Exception error) { failure = error; }
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
        try { if (failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw(); }
        finally { Directory.Delete(directory, true); }
    }

    private static IEnumerable<Control> Descendants(Control control)
    {
        foreach (Control child in control.Controls)
        {
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }
}

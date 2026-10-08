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
                var connection = Descendants(form).OfType<GroupBox>().Single(c => c.Text == "Conexão");
                var status = (Label)typeof(MainForm).GetField("lblStatus", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(form)!;
                form.Shown += async (_, _) =>
                {
                    try
                    {
                        foreach (var width in new[] { 900, 1100, 1350, 1100 })
                        {
                            form.NavigateTo("Início");
                            form.ClientSize = new Size(width, 820);
                            form.NavigateTo("Nova execução");
                            await Task.Delay(80);
                            var output = Environment.GetEnvironmentVariable("DBCT_UI_SNAPSHOT_DIR");
                            if (output is not null)
                            {
                                Directory.CreateDirectory(output);
                                using var bitmap = new Bitmap(form.Width, form.Height);
                                form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.Size));
                                bitmap.Save(Path.Combine(output, $"execution-live-{width}.png"));
                                File.WriteAllLines(Path.Combine(output, $"execution-live-{width}.layout"), Descendants(form).Where(c => c.Visible)
                                    .Select(c => $"{c.GetType().Name} [{c.Text}] bounds={c.Bounds} screen={c.PointToScreen(Point.Empty)}"));
                            }
                            var statusHeight = status.Font.Height * 2 + 8;
                            Assert.InRange(status.Height, 1, statusHeight);
                            // Measure from the start of the text region, not from an
                            // oversized label's bottom, which previously hid the bug.
                            var gap = connection.PointToScreen(Point.Empty).Y - status.PointToScreen(Point.Empty).Y;
                            Assert.InRange(gap, 0, statusHeight + 40 * form.DeviceDpi / 96);
                        }
                    }
                    catch (Exception error) { failure = error; }
                    finally { form.Close(); }
                };
                System.Windows.Forms.Application.Run(form);
                if (failure is not null) return;
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

using System.Drawing;
using System.Reflection;
using System.Windows.Forms;
using DBConnectionTester.Models;
using DBConnectionTester.Services.Storage;
using DBConnectionTester.UI;

namespace DBConnectionTester.Tests;

[Collection("WinForms UI")]
public sealed class UiSmokeTests
{
    [Theory]
    [InlineData(ApplicationTheme.Light, 1.0f)]
    [InlineData(ApplicationTheme.Dark, 1.5f)]
    [InlineData(ApplicationTheme.System, 2.0f)]
    public async Task MainShellRendersAtCommonScaleFactors(ApplicationTheme theme, float scale)
    {
        var root = Path.Combine(Path.GetTempPath(), "DBConnectionTester.Tests", Guid.NewGuid().ToString("N"));
        var store = await SqliteApplicationStore.OpenOrCreateAsync(Path.Combine(root, "data.db"), StorageScope.Custom);
        Exception? failure = null;
        var renderedPixels = 0;
        var thread = new Thread(() =>
        {
            try
            {
                using var form = new MainForm(store, ApplicationSettings.Default with { Theme = theme });
                form.Size = new Size(1120, 900);
                if (scale != 1)
                    form.Scale(new SizeF(scale, scale));
                form.Show();
                System.Windows.Forms.Application.DoEvents();
                Assert.Equal(theme, form.ConfiguredTheme);
                var snapshotDirectory = Environment.GetEnvironmentVariable("DBCT_UI_SNAPSHOT_DIR");
                foreach (var page in new[] { "Início", "Nova execução", "Histórico", "Perfis", "Configurações" })
                {
                    form.NavigateTo(page);
                    System.Windows.Forms.Application.DoEvents();
                    form.PerformLayout();
                    using var bitmap = new Bitmap(form.ClientSize.Width, form.ClientSize.Height);
                    form.DrawToBitmap(bitmap, form.ClientRectangle);
                    if (!string.IsNullOrWhiteSpace(snapshotDirectory))
                    {
                        Directory.CreateDirectory(snapshotDirectory);
                        bitmap.Save(Path.Combine(snapshotDirectory, $"{page.Replace(' ', '-')}-{theme}-{scale:0.0}.png"));
                    }
                    renderedPixels += bitmap.Width * bitmap.Height;
                }
                Assert.Equal(AutoScaleMode.Dpi, form.AutoScaleMode);
                Assert.True(form.ClientSize.Width >= 900);
                Assert.True(form.ClientSize.Height >= 650);
                if (scale == 1)
                {
                    typeof(MainForm).GetMethod("HideToTray", BindingFlags.Instance | BindingFlags.NonPublic)!
                        .Invoke(form, new object?[] { null });
                    Assert.False(form.Visible);
                    Assert.False(form.ShowInTaskbar);
                    typeof(MainForm).GetMethod("ShowPanel", BindingFlags.Instance | BindingFlags.NonPublic)!
                        .Invoke(form, null);
                    Assert.True(form.Visible);
                    Assert.True(form.ShowInTaskbar);
                }
                form.Close();
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        try
        {
            Assert.Null(failure);
            Assert.True(renderedPixels > 0);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }
}

[CollectionDefinition("WinForms UI", DisableParallelization = true)]
public sealed class WinFormsUiCollection;

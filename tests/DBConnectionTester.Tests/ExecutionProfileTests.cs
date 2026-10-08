using System.Drawing;
using System.Reflection;
using System.Windows.Forms;
using DBConnectionTester.Models;
using DBConnectionTester.Services.Storage;
using DBConnectionTester.UI;

namespace DBConnectionTester.Tests;

[Collection("WinForms UI")]
public sealed class ExecutionProfileTests
{
    [Fact]
    public async Task ProfileSelectionTracksOverridesKeepsManualValuesAndOpensMatchingEditor()
    {
        var directory = Path.Combine(Path.GetTempPath(), "DBCT-profiles-ui", Guid.NewGuid().ToString("N"));
        var store = await SqliteApplicationStore.OpenOrCreateAsync(Path.Combine(directory, "data.db"), StorageScope.Custom);
        var repository = new PersistentSettingsRepository(store);
        var a = await repository.SaveAsync(new ConnectionProfileDraft(null, "Perfil A", DatabaseType.MySqlMariaDb, "host-a", 3306, "user-a", "db-a", "",
            SqlServerAuthentication.Windows, "", ProfileExecutionDefaults.Default));
        var b = await repository.SaveAsync(new ConnectionProfileDraft(null, "Perfil B", DatabaseType.TcpOnly, "host-b", 443, "", "", "",
            SqlServerAuthentication.Windows, "", ProfileExecutionDefaults.Default));
        var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                using var form = new MainForm(store, ApplicationSettings.Default);
                form.Shown += async (_, _) =>
                {
                    try
                    {
                        var selector = Field<ComboBox>(form, "executionProfile");
                        var deadline = Environment.TickCount64 + 5000;
                        while (selector.Items.Count < 3 && Environment.TickCount64 < deadline) await Task.Delay(20);
                        Assert.Equal(3, selector.Items.Count);
                        form.NavigateTo("Nova execução");
                        Field<TextBox>(form, "txtPassword").Text = "old-password";
                        selector.SelectedIndex = 1;
                        Assert.Equal("host-a", Field<TextBox>(form, "txtHost").Text);
                        Assert.Empty(Field<TextBox>(form, "txtPassword").Text);
                        var state = Field<Label>(form, "executionProfileState");
                        Assert.False(state.Visible);
                        Field<TextBox>(form, "txtPassword").Text = "new-password";
                        Assert.False(state.Visible);
                        Field<TextBox>(form, "txtHost").Text = "override";
                        Assert.True(state.Visible);
                        Assert.Equal(a.ProfileId, Field<Guid?>(form, "selectedProfileId"));
                        Field<TextBox>(form, "txtHost").Text = "host-a";
                        Assert.False(state.Visible);
                        Field<NumericUpDown>(form, "numInterval").Value = 7;
                        selector.SelectedIndex = 0;
                        Assert.Equal("host-a", Field<TextBox>(form, "txtHost").Text);
                        Assert.Equal(7, Field<NumericUpDown>(form, "numInterval").Value);
                        Assert.Equal("new-password", Field<TextBox>(form, "txtPassword").Text);
                        Assert.Null(Field<Guid?>(form, "selectedProfileId"));
                        selector.SelectedIndex = 1;
                        Assert.Equal(5, Field<NumericUpDown>(form, "numInterval").Value);
                        Field<Button>(form, "manageProfiles").PerformClick();
                        var profiles = Field<ProfilesPage>(form, "profilesPage");
                        Assert.True(profiles.Visible);
                        var name = (TextBox)typeof(ProfilesPage).GetField("name", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(profiles)!;
                        Assert.Equal("Perfil A", name.Text);
                        name.Text = "Perfil A editado";
                        profiles.EditDecision = _ => DialogResult.Cancel;
                        form.NavigateTo("Início");
                        Assert.True(profiles.Visible);
                        Assert.Equal("Perfil A editado", name.Text);
                        form.Close();
                        Assert.False(form.IsDisposed);
                        Assert.True(profiles.HasUnsavedChanges);
                        profiles.EditDecision = _ => DialogResult.No;
                        form.NavigateTo("Nova execução");
                        Assert.False(profiles.Visible);
                        Assert.False(profiles.HasUnsavedChanges);
                        Assert.Equal("Perfil A", name.Text);
                        var stateType = typeof(MainForm).GetField("runUiState", BindingFlags.Instance | BindingFlags.NonPublic)!.FieldType;
                        Invoke(form, "ApplyRunUiState", Enum.Parse(stateType, "Running"), null);
                        Assert.False(selector.Enabled);
                        Assert.False(Field<Button>(form, "manageProfiles").Enabled);
                        Invoke(form, "ApplyRunUiState", Enum.Parse(stateType, "Idle"), null);
                        Assert.True(selector.Enabled);
                        Field<TextBox>(form, "txtHost").Text = "override";
                        foreach (var size in new[] { new Size(800, 600), new Size(1100, 860) })
                        {
                            form.ClientSize = size;
                            await Task.Delay(80);
                            var area = selector.Parent!.Parent!;
                            var viewport = Descendants(form).Single(c => c.Name == "ExecutionConfigurationViewport");
                            Assert.True(viewport.PointToScreen(Point.Empty).Y >= area.PointToScreen(Point.Empty).Y + area.Height);
                            var output = Environment.GetEnvironmentVariable("DBCT_UI_SNAPSHOT_DIR");
                            if (output is not null)
                            {
                                Directory.CreateDirectory(output);
                                using var bitmap = new Bitmap(form.Width, form.Height);
                                form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.Size));
                                bitmap.Save(Path.Combine(output, $"execution-profile-{size.Width}x{size.Height}.png"));
                            }
                        }
                        Invoke(form, "SetExecutionProfiles", (IReadOnlyList<SavedConnectionProfile>)new[] { b });
                        Assert.Equal(0, selector.SelectedIndex);
                        Assert.Equal(2, selector.Items.Count);
                        Assert.Equal("override", Field<TextBox>(form, "txtHost").Text);
                        Assert.Null(Field<Guid?>(form, "selectedProfileId"));
                        var settingsPage = Field<SettingsPage>(form, "settingsPage");
                        var releaseSettings = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                        var settingsWork = settingsPage.RunOperationAsync("Working", async () => { await releaseSettings.Task; return true; });
                        try
                        {
                            Assert.False(Field<Button>(form, "btnStart").Enabled);
                            Assert.False(Field<Button>(form, "btnTestOnce").Enabled);
                            var start = (Task)typeof(MainForm).GetMethod("StartAsync", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(form, new object[] { true })!;
                            await start;
                            Assert.Equal("Idle", Field<object>(form, "runUiState").ToString());
                            form.Close();
                            Assert.False(form.IsDisposed);
                        }
                        finally { releaseSettings.TrySetResult(); }
                        Assert.True(await settingsWork);
                        Assert.True(Field<Button>(form, "btnStart").Enabled);
                        form.NavigateTo("Configurações");
                        var theme = (ComboBox)typeof(SettingsPage).GetField("theme", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(settingsPage)!;
                        theme.SelectedItem = ApplicationTheme.Dark;
                        settingsPage.EditDecision = () => DialogResult.Cancel;
                        form.NavigateTo("Início");
                        Assert.True(settingsPage.Visible);
                        form.Close();
                        Assert.False(form.IsDisposed);
                        Assert.True(settingsPage.HasUnsavedChanges);
                        settingsPage.EditDecision = () => DialogResult.No;
                        form.NavigateTo("Nova execução");
                        Assert.False(settingsPage.Visible);
                        Assert.False(settingsPage.HasUnsavedChanges);
                        finished.TrySetResult();
                    }
                    catch (Exception error) { finished.TrySetException(error); }
                    finally { form.Close(); }
                };
                System.Windows.Forms.Application.Run(form);
            }
            catch (Exception error) { finished.TrySetException(error); }
        });
        thread.SetApartmentState(ApartmentState.STA); thread.IsBackground = true; thread.Start();
        try { await finished.Task.WaitAsync(TimeSpan.FromSeconds(20)); }
        finally { thread.Join(5000); Directory.Delete(directory, true); }
    }

    private static T Field<T>(MainForm form, string name) => (T)typeof(MainForm).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(form)!;
    private static void Invoke(MainForm form, string name, params object?[] args) => typeof(MainForm).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(form, args);
    private static IEnumerable<Control> Descendants(Control control)
    {
        foreach (Control child in control.Controls) { yield return child; foreach (var item in Descendants(child)) yield return item; }
    }
}

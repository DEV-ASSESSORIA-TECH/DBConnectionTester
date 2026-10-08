using System.Reflection;
using System.Windows.Forms;
using DBConnectionTester.Models;
using DBConnectionTester.Services.Storage;
using DBConnectionTester.UI;

namespace DBConnectionTester.Tests;

[Collection("WinForms UI")]
public sealed class SettingsPageTests
{
    [Fact]
    public Task SaveRunsOutsideUiAndBlocksConcurrentOperations() => RunUi(async (page, repository, _) =>
    {
        var uiThread = Environment.CurrentManagedThreadId;
        var entered = Signal(); var release = Signal();
        var writes = 0;
        repository.Save = async _ =>
        {
            Assert.NotEqual(uiThread, Environment.CurrentManagedThreadId);
            writes++; entered.TrySetResult(); await release.Task;
        };
        var saved = false;
        page.SettingsSaved += _ => { Assert.Equal(uiThread, Environment.CurrentManagedThreadId); saved = true; };
        var saving = page.SaveAsync();
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(page.IsBusy);
            Assert.False(Field<Button>(page, "save").Enabled);
            Assert.False(Field<ComboBox>(page, "theme").Enabled);
            Assert.False(await page.SaveAsync());
            var ran = false;
            Assert.False(await page.RunOperationAsync("Duplicate", () => { ran = true; return Task.FromResult(true); }));
            Assert.False(ran);
            var tick = Signal();
            page.BeginInvoke((Action)(() => tick.TrySetResult()));
            await tick.Task.WaitAsync(TimeSpan.FromSeconds(2));
            release.TrySetResult();
            Assert.True(await saving);
            Assert.True(saved);
            Assert.Equal(1, writes);
            Assert.False(page.IsBusy);
            Assert.True(Field<Button>(page, "save").Enabled);
        }
        finally { release.TrySetResult(); }
    });

    [Fact]
    public Task RunLockRemainsAfterOperationCompletes() => RunUi(async (page, _, _) =>
    {
        var release = Signal();
        var work = page.RunOperationAsync("Working", async () => { await release.Task; return true; });
        page.SetOperationsEnabled(false);
        release.TrySetResult();
        Assert.True(await work);
        Assert.False(page.IsBusy);
        Assert.False(Field<Button>(page, "save").Enabled);
        Assert.False(await page.SaveAsync());
        page.SetOperationsEnabled(true);
        Assert.True(Field<Button>(page, "save").Enabled);
    });

    [Theory]
    [InlineData("Permission")]
    [InlineData("UacCancel")]
    [InlineData("UacFailure")]
    [InlineData("Manifest")]
    [InlineData("Checksum")]
    [InlineData("Sqlite")]
    public Task ExpectedFailuresReleaseControlsWithoutApplyingSettings(string failure) => RunUi(async (page, repository, _) =>
    {
        Exception error = failure switch
        {
            "Permission" => new UnauthorizedAccessException("Denied"),
            "UacCancel" => new System.ComponentModel.Win32Exception(1223),
            "UacFailure" => new System.ComponentModel.Win32Exception(5),
            "Manifest" => new System.Text.Json.JsonException("Invalid manifest"),
            "Checksum" => new InvalidDataException("Invalid checksum"),
            _ => new Microsoft.Data.Sqlite.SqliteException("Busy", 5)
        };
        repository.Save = _ => Task.FromException(error);
        var applied = false; var reported = false;
        page.SettingsSaved += _ => applied = true;
        page.MessageReporter = (_, _, warning) => reported = warning;
        Field<ComboBox>(page, "theme").SelectedItem = ApplicationTheme.Dark;
        Assert.False(await page.SaveAsync());
        Assert.False(applied);
        Assert.False(page.IsBusy);
        Assert.True(Field<Button>(page, "save").Enabled);
        Assert.Equal(ApplicationTheme.Dark, page.SelectedTheme);
        Assert.Equal(failure != "UacCancel", reported);
    });

    [Fact]
    public Task InvalidOutputDirectoryDoesNotPersistSettings() => RunUi(async (page, repository, _) =>
    {
        var writes = 0;
        repository.Save = _ => { writes++; return Task.CompletedTask; };
        Field<CheckBox>(page, "legacyEnabled").Checked = true;
        Field<TextBox>(page, "legacyDirectory").Text = "";
        Assert.False(await page.SaveAsync());
        Assert.Equal(0, writes);
    });

    [Theory]
    [InlineData(DialogResult.Yes)]
    [InlineData(DialogResult.No)]
    [InlineData(DialogResult.Cancel)]
    public Task UnsavedSettingsCanBeSavedDiscardedOrKept(DialogResult choice) => RunUi(async (page, repository, _) =>
    {
        var writes = 0; var applied = false;
        repository.Save = _ => { writes++; return Task.CompletedTask; };
        page.SettingsSaved += _ => applied = true;
        Field<ComboBox>(page, "theme").SelectedItem = ApplicationTheme.Dark;
        Assert.True(page.HasUnsavedChanges);
        Assert.Equal("Alterações não salvas.", Field<Label>(page, "settingsState").Text);
        page.EditDecision = () => choice;
        Assert.Equal(choice != DialogResult.Cancel, await page.TryLeaveAsync());
        Assert.Equal(choice == DialogResult.Yes ? 1 : 0, writes);
        Assert.Equal(choice == DialogResult.Yes, applied);
        Assert.Equal(choice == DialogResult.Cancel, page.HasUnsavedChanges);
        Assert.Equal(choice == DialogResult.No ? ApplicationTheme.System : ApplicationTheme.Dark, page.SelectedTheme);
        Assert.False(page.IsBusy);
    });

    [Fact]
    public Task FailedSavePreventsLeavingAndPreservesDraft() => RunUi(async (page, repository, _) =>
    {
        repository.Save = _ => Task.FromException(new IOException("Disk failure"));
        Field<ComboBox>(page, "theme").SelectedItem = ApplicationTheme.Dark;
        page.EditDecision = () => DialogResult.Yes;
        Assert.False(await page.TryLeaveAsync());
        Assert.True(page.HasUnsavedChanges);
        Assert.Equal(ApplicationTheme.Dark, page.SelectedTheme);
        Assert.False(page.IsBusy);
        Assert.True(Field<Button>(page, "save").Enabled);
        page.EditDecision = () => DialogResult.No;
        Assert.True(await page.TryLeaveAsync());
        Assert.Equal(ApplicationTheme.System, page.SelectedTheme);
    });

    [Fact]
    public Task RefinedSettingsKeepGroupsOrderedAndExplainPendingStorage() => RunUi(async (page, _, form) =>
    {
        Assert.False(Field<TextBox>(page, "legacyDirectory").Enabled);
        Assert.False(Field<Button>(page, "browseLegacy").Enabled);
        Field<CheckBox>(page, "legacyEnabled").Checked = true;
        Assert.True(Field<TextBox>(page, "legacyDirectory").Enabled);
        page.SetOperationsEnabled(false);
        Assert.False(Field<TextBox>(page, "legacyDirectory").Enabled);
        page.SetOperationsEnabled(true);
        Assert.True(Field<TextBox>(page, "legacyDirectory").Enabled);
        var theme = Field<ComboBox>(page, "theme");
        Assert.Equal("Claro", theme.GetItemText(ApplicationTheme.Light));
        var scope = Field<ComboBox>(page, "targetScope");
        scope.SelectedItem = StorageScope.Custom;
        Assert.Contains("escolhida", Field<TextBox>(page, "targetPath").Text);
        scope.SelectedItem = StorageScope.LocalUser;
        Assert.Equal(StorageLocations.CreateDefault().LocalDatabasePath, Field<TextBox>(page, "targetPath").Text);
        Assert.True(Field<TextBox>(page, "currentDatabasePath").ReadOnly);
        var store = Field<SqliteApplicationStore>(page, "store");
        var selected = store.Descriptor with { DatabasePath = Path.Combine(Path.GetTempPath(), "selected-data.db") };
        var activated = false;
        page.StorageSelected += descriptor => activated = descriptor == selected;
        await (Task)typeof(SettingsPage).GetMethod("ActivateAsync", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(page, new object[] { selected })!;
        Assert.True(activated);
        Assert.Contains(selected.DatabasePath, Field<Label>(page, "pendingStorage").Text);
        Assert.True(Field<Label>(page, "pendingStorage").Visible);
        Assert.Equal(Field<bool>(page, "executableAvailable"), Field<CheckBox>(page, "includeExecutable").Enabled);
        var detailsButton = Descendants(page).OfType<Button>().Single(button => button.Text == "Mostrar detalhes");
        detailsButton.PerformClick();
        Assert.Contains(Descendants(page).OfType<TextBox>(), text => text.Text == store.Descriptor.StoreId.ToString("D") && text.Visible);
        detailsButton.PerformClick();
        foreach (var size in new[] { new System.Drawing.Size(620, 600), new System.Drawing.Size(920, 860), new System.Drawing.Size(1300, 950) })
        {
            form.ClientSize = size;
            page.AutoScrollPosition = System.Drawing.Point.Empty;
            await Task.Delay(40);
            var groups = Descendants(page).OfType<GroupBox>().OrderBy(group => group.PointToScreen(System.Drawing.Point.Empty).Y).ToArray();
            Assert.Equal(4, groups.Length);
            foreach (var group in groups)
                foreach (var label in Descendants(group).OfType<Label>().Where(label => label.Visible))
                    Assert.True(label.PointToScreen(System.Drawing.Point.Empty).Y + label.Height <= group.PointToScreen(System.Drawing.Point.Empty).Y + group.Height, $"Texto cortado em {group.Text}: {label.Text}");
            for (var index = 1; index < groups.Length; index++)
                Assert.True(groups[index].PointToScreen(System.Drawing.Point.Empty).Y >= groups[index - 1].PointToScreen(System.Drawing.Point.Empty).Y + groups[index - 1].Height);
            var path = Field<TextBox>(page, "currentDatabasePath");
            Assert.True(path.Width > 200);
            var output = Environment.GetEnvironmentVariable("DBCT_UI_SNAPSHOT_DIR");
            if (output is not null)
            {
                Directory.CreateDirectory(output);
                using var image = new System.Drawing.Bitmap(form.Width, form.Height);
                form.DrawToBitmap(image, new System.Drawing.Rectangle(System.Drawing.Point.Empty, form.Size));
                image.Save(Path.Combine(output, $"settings-refined-{size.Width}x{size.Height}.png"));
            }
            page.ScrollControlIntoView(Field<Button>(page, "restore"));
            var restore = Field<Button>(page, "restore");
            var position = page.PointToClient(restore.PointToScreen(System.Drawing.Point.Empty));
            Assert.True(position.Y >= 0 && position.Y + restore.Height <= page.ClientSize.Height);
            if (output is not null)
            {
                using var image = new System.Drawing.Bitmap(form.Width, form.Height);
                form.DrawToBitmap(image, new System.Drawing.Rectangle(System.Drawing.Point.Empty, form.Size));
                image.Save(Path.Combine(output, $"settings-refined-bottom-{size.Width}x{size.Height}.png"));
            }
        }
    });

    [Fact]
    public Task VisibilityBatchReducesLayoutAndAlwaysRestoresResize() => RunUi(async (page, _, form) =>
    {
        var layouts = 0;
        foreach (var control in Descendants(page).Prepend(page)) control.Layout += (_, _) => layouts++;
        page.Visible = false;
        layouts = 0;
        page.Visible = true;
        var normal = layouts;
        page.Visible = false;
        layouts = 0;
        page.BatchLayout(() => page.Visible = true);
        Assert.True(layouts < normal, $"Normal: {normal}; agrupado: {layouts}");
        Assert.Throws<InvalidOperationException>(() => page.BatchLayout(() => throw new InvalidOperationException("Probe")));
        var path = Field<TextBox>(page, "currentDatabasePath");
        var originalWidth = path.Width;
        form.Width -= 200;
        await Task.Delay(30);
        Assert.True(path.Width < originalWidth);
        form.Width += 200;
        await Task.Delay(30);
        Assert.True(path.Width >= originalWidth);
        Field<CheckBox>(page, "legacyEnabled").Checked = true;
        Assert.True(Field<TextBox>(page, "legacyDirectory").Enabled);
        Assert.True(page.HasUnsavedChanges);
    });

    private static IEnumerable<Control> Descendants(Control control)
    {
        foreach (Control child in control.Controls) { yield return child; foreach (var descendant in Descendants(child)) yield return descendant; }
    }

    private static Task RunUi(Func<SettingsPage, Repository, Form, Task> action)
    {
        var complete = Signal();
        var thread = new Thread(() =>
        {
            var directory = Path.Combine(Path.GetTempPath(), "DBCT-settings-ui", Guid.NewGuid().ToString("N"));
            try
            {
                var store = SqliteApplicationStore.OpenOrCreateAsync(Path.Combine(directory, "data.db"), StorageScope.Custom).GetAwaiter().GetResult();
                var repository = new Repository();
                using var page = new SettingsPage(store, ApplicationSettings.Default, repository, new Preferences());
                page.MessageReporter = (_, _, _) => { };
                using var form = new Form { Width = 1100, Height = 850 };
                form.Controls.Add(page);
                form.Shown += async (_, _) =>
                {
                    try { await action(page, repository, form); complete.TrySetResult(); }
                    catch (Exception error) { complete.TrySetException(error); }
                    finally { form.Close(); }
                };
                System.Windows.Forms.Application.Run(form);
            }
            catch (Exception error) { complete.TrySetException(error); }
            finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
        });
        thread.SetApartmentState(ApartmentState.STA); thread.IsBackground = true; thread.Start();
        return complete.Task.WaitAsync(TimeSpan.FromSeconds(20));
    }

    private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static T Field<T>(SettingsPage page, string name) => (T)typeof(SettingsPage).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(page)!;
    private sealed class Repository : IApplicationSettingsRepository
    {
        public Func<ApplicationSettings, Task> Save = _ => Task.CompletedTask;
        public Task<ApplicationSettings> GetAsync(CancellationToken token = default) => Task.FromResult(ApplicationSettings.Default);
        public Task SaveAsync(ApplicationSettings settings, CancellationToken token = default) => Save(settings);
    }
    private sealed class Preferences : IStoragePreferenceStore
    {
        public StoragePreference? Read() => null;
        public void Write(StoragePreference preference) { }
    }
}

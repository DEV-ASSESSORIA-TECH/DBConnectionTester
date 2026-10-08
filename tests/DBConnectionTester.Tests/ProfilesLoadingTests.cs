using System.Reflection;
using System.Windows.Forms;
using DBConnectionTester.Models;
using DBConnectionTester.Services.Storage;
using DBConnectionTester.UI;

namespace DBConnectionTester.Tests;

[Collection("WinForms UI")]
public sealed class ProfilesLoadingTests
{
    [Fact]
    public Task NewProfileRestoresEventsAndLayoutAfterRepeatedResets() => RunUi(async (page, repository, form) =>
    {
        await page.RefreshAsync();
        for (var i = 0; i < 3; i++)
        {
            Field<ListBox>(page, "profiles").SelectedIndex = 0;
            Assert.Equal("Saved", Field<TextBox>(page, "name").Text);
            Field<Button>(page, "create").PerformClick();
            Assert.Empty(Field<TextBox>(page, "name").Text);
            var type = Field<ComboBox>(page, "databaseType");
            type.SelectedItem = DatabaseProfiles.Get(DatabaseType.Sqlite);
            Assert.False(Field<TextBox>(page, "host").Visible);
            Assert.True(Field<TextBox>(page, "sqliteFile").Visible);
            Assert.False(Field<CheckBox>(page, "dns").Checked);
            type.SelectedItem = DatabaseProfiles.Get(DatabaseType.MySqlMariaDb);
            Assert.True(Field<TextBox>(page, "host").Visible);
            Assert.False(Field<TextBox>(page, "sqliteFile").Visible);
            Assert.Equal(3306, Field<NumericUpDown>(page, "port").Value);
        }
        var editor = Field<TableLayoutPanel>(page, "editor");
        var input = Field<TextBox>(page, "host");
        var width = input.Width;
        form.Width += 200;
        await Task.Delay(30);
        Assert.True(input.Width > width);
        Assert.True(input.Right <= editor.ClientSize.Width);
    });

    [Fact]
    public Task QueryRunsOutsideUiAndDoesNotLoadFirstProfileWhileBinding() => RunUi(async (page, repository, _) =>
    {
        var uiThread = Environment.CurrentManagedThreadId;
        var started = Signal();
        using var release = new ManualResetEventSlim();
        repository.List = () =>
        {
            Assert.NotEqual(uiThread, Environment.CurrentManagedThreadId);
            started.TrySetResult();
            if (!release.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException();
            return Task.FromResult<IReadOnlyList<SavedConnectionProfile>>([repository.Saved]);
        };
        var populated = 0;
        Field<TextBox>(page, "name").TextChanged += (_, _) => populated++;
        var loading = page.RefreshAsync();
        try
        {
            await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(uiThread, Environment.CurrentManagedThreadId);
            Assert.False(Field<Button>(page, "save").Enabled);
            release.Set();
            await loading;
            Assert.True(Field<Button>(page, "save").Enabled);
            Assert.Equal(0, populated);
        }
        finally { release.Set(); }
    });

    [Fact]
    public Task SaveRunsOutsideUiAndPreventsDuplicateWrites() => RunUi(async (page, repository, _) =>
    {
        await page.RefreshAsync();
        var uiThread = Environment.CurrentManagedThreadId;
        var started = Signal();
        var release = Signal();
        var writes = 0;
        repository.Save = async draft =>
        {
            Assert.NotEqual(uiThread, Environment.CurrentManagedThreadId);
            Interlocked.Increment(ref writes);
            started.TrySetResult();
            await release.Task;
            return repository.Saved;
        };
        Field<TextBox>(page, "name").Text = "Saved";
        var saving = (Task)typeof(ProfilesPage).GetMethod("SaveAsync", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(page, null)!;
        try
        {
            await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var button = Field<Button>(page, "save");
            Assert.False(button.Enabled);
            button.PerformClick();
            Assert.Equal(1, writes);
            release.TrySetResult();
            await saving;
            Assert.True(button.Enabled);
            Assert.Equal(repository.Saved.ProfileId, ((SavedConnectionProfile)Field<ListBox>(page, "profiles").SelectedItem!).ProfileId);
            Assert.Equal("Saved", Field<TextBox>(page, "name").Text);
        }
        finally { release.TrySetResult(); }
    });

    [Fact]
    public Task CancelAndDiscardProtectNewAndSelectedProfileEdits() => RunUi(async (page, repository, _) =>
    {
        await page.RefreshAsync();
        var list = Field<ListBox>(page, "profiles");
        list.SelectedIndex = 0;
        Field<TextBox>(page, "host").Text = "edited-host";
        Assert.True(page.HasUnsavedChanges);
        Assert.Equal("Alterações não salvas", Field<Label>(page, "editorState").Text);
        Assert.Equal("Saved", Field<Label>(page, "editorTitle").Text);
        page.EditDecision = _ => DialogResult.Cancel;
        Field<Button>(page, "create").PerformClick();
        Assert.Equal("edited-host", Field<TextBox>(page, "host").Text);
        Assert.False(await page.TryLeaveAsync());
        list.ClearSelected();
        Assert.Equal(repository.Saved.ProfileId, ((SavedConnectionProfile)list.SelectedItem!).ProfileId);
        Assert.Equal("edited-host", Field<TextBox>(page, "host").Text);
        page.EditDecision = _ => DialogResult.No;
        Assert.True(await page.TryLeaveAsync());
        Assert.Equal("localhost", Field<TextBox>(page, "host").Text);
        Assert.False(page.HasUnsavedChanges);
        Field<Button>(page, "create").PerformClick();
        Field<TextBox>(page, "name").Text = "New unsaved";
        page.EditDecision = _ => DialogResult.Cancel;
        list.SelectedIndex = 0;
        Assert.Null(list.SelectedItem);
        Assert.Equal("Novo perfil", Field<Label>(page, "editorTitle").Text);
        Assert.Equal("New unsaved", Field<TextBox>(page, "name").Text);
    });

    [Theory]
    [InlineData(DialogResult.Yes)]
    [InlineData(DialogResult.No)]
    [InlineData(DialogResult.Cancel)]
    public Task UsingEditedProfileRequiresExplicitChoice(DialogResult choice) => RunUi(async (page, repository, _) =>
    {
        await page.RefreshAsync();
        Field<ListBox>(page, "profiles").SelectedIndex = 0;
        Field<TextBox>(page, "host").Text = "edited-host";
        page.EditDecision = _ => choice;
        var writes = 0;
        repository.Save = draft =>
        {
            writes++;
            repository.Saved = repository.Saved with { Host = draft.Host };
            return Task.FromResult(repository.Saved);
        };
        SavedConnectionProfile? used = null;
        page.UseRequested += profile => used = profile;
        await (Task)typeof(ProfilesPage).GetMethod("UseSelectionAsync", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(page, null)!;
        Assert.Equal(choice == DialogResult.Yes ? 1 : 0, writes);
        Assert.Equal(choice == DialogResult.Yes ? "Perfil salvo" : choice == DialogResult.Cancel ? "Alterações não salvas" : "", Field<Label>(page, "editorState").Text);
        if (choice == DialogResult.Cancel)
        {
            Assert.Null(used);
            Assert.Equal("edited-host", Field<TextBox>(page, "host").Text);
            Assert.True(page.HasUnsavedChanges);
        }
        else
        {
            Assert.Equal(choice == DialogResult.Yes ? "edited-host" : "localhost", used!.Host);
            Assert.False(page.HasUnsavedChanges);
        }
    });

    [Fact]
    public Task FailedSaveKeepsDraftAndPreventsLeavingOrApplying() => RunUi(async (page, repository, _) =>
    {
        await page.RefreshAsync();
        Field<ListBox>(page, "profiles").SelectedIndex = 0;
        Field<TextBox>(page, "host").Text = "edited-host";
        page.EditDecision = _ => DialogResult.Yes;
        repository.Save = _ => Task.FromException<SavedConnectionProfile>(new ArgumentException("Invalid draft"));
        Assert.False(await page.TryLeaveAsync());
        Assert.True(page.HasUnsavedChanges);
        Assert.Equal("edited-host", Field<TextBox>(page, "host").Text);
        var used = false;
        page.UseRequested += _ => used = true;
        await (Task)typeof(ProfilesPage).GetMethod("UseSelectionAsync", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(page, null)!;
        Assert.False(used);
        Assert.True(Field<Button>(page, "save").Enabled);
    });

    [Fact]
    public Task CompactFieldsAdaptWithoutOverlapAndHideForSqlite() => RunUi(async (page, _, form) =>
    {
        await page.RefreshAsync();
        Field<ListBox>(page, "profiles").SelectedIndex = 0;
        var host = Field<TextBox>(page, "host");
        var port = Field<NumericUpDown>(page, "port");
        foreach (var size in new[] { new System.Drawing.Size(1300, 850), new System.Drawing.Size(640, 620), new System.Drawing.Size(1000, 760) })
        {
            form.ClientSize = size;
            await Task.Delay(40);
            var editor = Field<TableLayoutPanel>(page, "editor");
            var compact = editor.Width * 96d / editor.DeviceDpi >= 650;
            Assert.Equal(compact, ReferenceEquals(host.Parent, port.Parent) && host.Parent != editor);
            if (compact) Assert.True(host.Right <= port.Left);
            Assert.True(host.Width > 100);
            var output = Environment.GetEnvironmentVariable("DBCT_UI_SNAPSHOT_DIR");
            if (output is not null)
            {
                Directory.CreateDirectory(output);
                using var image = new System.Drawing.Bitmap(form.Width, form.Height);
                form.DrawToBitmap(image, new System.Drawing.Rectangle(System.Drawing.Point.Empty, form.Size));
                image.Save(Path.Combine(output, $"profiles-{size.Width}x{size.Height}.png"));
            }
        }
        Field<ComboBox>(page, "databaseType").SelectedItem = DatabaseProfiles.Get(DatabaseType.Sqlite);
        Assert.False(host.Visible);
        Assert.False(port.Visible);
        Assert.True(Field<TextBox>(page, "sqliteFile").Visible);
        Field<ComboBox>(page, "databaseType").SelectedItem = DatabaseProfiles.Get(DatabaseType.MySqlMariaDb);
        Assert.True(host.Visible);
        Assert.True(port.Visible);
    });

    private static Task RunUi(Func<ProfilesPage, Repository, Form, Task> action)
    {
        var finished = Signal();
        var thread = new Thread(() =>
        {
            try
            {
                var repository = new Repository();
                using var page = new ProfilesPage(repository);
                page.EditDecision = _ => DialogResult.No;
                page.ErrorReporter = _ => { };
                using var form = new Form { Width = 1300, Height = 850 };
                form.Controls.Add(page);
                form.Shown += async (_, _) =>
                {
                    try { await action(page, repository, form); finished.TrySetResult(); }
                    catch (Exception error) { finished.TrySetException(error); }
                    finally { form.Close(); }
                };
                System.Windows.Forms.Application.Run(form);
            }
            catch (Exception error) { finished.TrySetException(error); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();
        return finished.Task.WaitAsync(TimeSpan.FromSeconds(20));
    }

    private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static T Field<T>(ProfilesPage page, string name) =>
        (T)typeof(ProfilesPage).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(page)!;

    private sealed class Repository : IConnectionProfileRepository
    {
        public SavedConnectionProfile Saved { get; set; } = new(Guid.NewGuid(), "Saved", DatabaseType.MySqlMariaDb, "localhost", 3306,
            "", "", "", SqlServerAuthentication.Windows, "", ProfileExecutionDefaults.Default, DateTimeOffset.Now, DateTimeOffset.Now);
        public Func<Task<IReadOnlyList<SavedConnectionProfile>>>? List;
        public Func<ConnectionProfileDraft, Task<SavedConnectionProfile>>? Save;
        public Task<IReadOnlyList<SavedConnectionProfile>> ListAsync(CancellationToken token = default) =>
            List?.Invoke() ?? Task.FromResult<IReadOnlyList<SavedConnectionProfile>>([Saved]);
        public Task<SavedConnectionProfile?> GetAsync(Guid id, CancellationToken token = default) => throw new NotSupportedException();
        public Task<SavedConnectionProfile> SaveAsync(ConnectionProfileDraft draft, CancellationToken token = default) =>
            Save?.Invoke(draft) ?? Task.FromResult(Saved);
        public Task DeleteAsync(Guid id, CancellationToken token = default) => Task.CompletedTask;
    }
}

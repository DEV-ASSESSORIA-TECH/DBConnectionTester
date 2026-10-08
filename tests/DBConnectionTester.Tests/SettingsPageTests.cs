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

using DBConnectionTester.Models;
using DBConnectionTester.Services.Storage;

namespace DBConnectionTester.Tests;

public sealed class PersistentSettingsRepositoryTests
{
    [Fact]
    public async Task ReturnsDefaultsAndPersistsApplicationSettings()
    {
        using var fixture = await RepositoryFixture.CreateAsync();

        Assert.Equal(ApplicationSettings.Default, await fixture.Repository.GetAsync());

        var expected = new ApplicationSettings(ApplicationTheme.Dark, true, Path.Combine(fixture.Root, "legacy"));
        await fixture.Repository.SaveAsync(expected);

        Assert.Equal(expected, await fixture.Repository.GetAsync());
    }

    [Fact]
    public async Task CreatesUpdatesListsAndDeletesProfilesWithoutPasswords()
    {
        using var fixture = await RepositoryFixture.CreateAsync();
        var draft = Draft("Produção");

        var created = await fixture.Repository.SaveAsync(draft);
        var updated = await fixture.Repository.SaveAsync(draft with
        {
            ProfileId = created.ProfileId,
            Host = "db.internal",
            UserName = "monitor"
        });

        Assert.Equal(created.ProfileId, updated.ProfileId);
        Assert.Equal(created.CreatedAt, updated.CreatedAt);
        Assert.Equal("db.internal", updated.Host);
        Assert.Single(await fixture.Repository.ListAsync());

        await using (var connection = await fixture.Store.OpenConnectionAsync())
        {
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT GROUP_CONCAT(name, ',') FROM pragma_table_info('connection_profiles');";
            var columns = (string)(await command.ExecuteScalarAsync())!;
            Assert.DoesNotContain("password", columns, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("connection_string", columns, StringComparison.OrdinalIgnoreCase);
        }

        await fixture.Repository.DeleteAsync(created.ProfileId);
        Assert.Empty(await fixture.Repository.ListAsync());
    }

    [Fact]
    public async Task ProfileNamesAreUniqueIgnoringCaseAndWhitespace()
    {
        using var fixture = await RepositoryFixture.CreateAsync();
        await fixture.Repository.SaveAsync(Draft("Produção"));

        var exception = await Assert.ThrowsAsync<ArgumentException>(() =>
            fixture.Repository.SaveAsync(Draft("  produção  ")));

        Assert.Contains("Já existe", exception.Message);
    }

    private static ConnectionProfileDraft Draft(string name) => new(
        null,
        name,
        DatabaseType.PostgreSql,
        "localhost",
        5432,
        "user",
        "database",
        "",
        SqlServerAuthentication.SqlLogin,
        "",
        ProfileExecutionDefaults.Default);

    private sealed class RepositoryFixture : IDisposable
    {
        private RepositoryFixture(string root, SqliteApplicationStore store)
        {
            Root = root;
            Store = store;
            Repository = new PersistentSettingsRepository(store);
        }

        public string Root { get; }
        public SqliteApplicationStore Store { get; }
        public PersistentSettingsRepository Repository { get; }

        public static async Task<RepositoryFixture> CreateAsync()
        {
            var root = Path.Combine(Path.GetTempPath(), "DBConnectionTester.Tests", Guid.NewGuid().ToString("N"));
            var store = await SqliteApplicationStore.OpenOrCreateAsync(
                Path.Combine(root, "data.db"),
                StorageScope.Custom);
            return new RepositoryFixture(root, store);
        }

        public void Dispose()
        {
            if (Directory.Exists(Root))
                Directory.Delete(Root, recursive: true);
        }
    }
}

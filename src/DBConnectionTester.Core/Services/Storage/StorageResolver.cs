using DBConnectionTester.Models;

namespace DBConnectionTester.Services.Storage;

public sealed record StorageDiscoveryResult(
    SqliteApplicationStore? SelectedStore,
    IReadOnlyList<StoreDescriptor> Candidates,
    IReadOnlyList<StoreInspection> Problems)
{
    public bool RequiresSelection => SelectedStore is null && Candidates.Count > 1;
}

public sealed class StorageResolver
{
    private static readonly StringComparer PathComparer = StringComparer.OrdinalIgnoreCase;
    private readonly StorageLocations locations;
    private readonly IStoragePreferenceStore preferences;

    public StorageResolver(StorageLocations locations, IStoragePreferenceStore preferences)
    {
        this.locations = locations;
        this.preferences = preferences;
    }

    public async Task<StorageDiscoveryResult> ResolveAsync(
        IReadOnlyList<string> arguments,
        CancellationToken token = default)
    {
        var explicitDirectory = ReadExplicitDataDirectory(arguments);
        if (explicitDirectory is not null)
        {
            var explicitPath = Path.Combine(Path.GetFullPath(explicitDirectory), StorageLocations.DatabaseFileName);
            var scope = ScopeForPath(explicitPath);
            var store = await OpenExplicitAsync(explicitPath, scope, token);
            SavePreference(store.Descriptor, observedPortableStoreId: null);
            return new StorageDiscoveryResult(store, [store.Descriptor], []);
        }

        var preference = preferences.Read();
        var paths = CandidatePaths(preference).ToArray();
        var inspections = new List<StoreInspection>();
        foreach (var path in paths)
            inspections.Add(await SqliteApplicationStore.InspectAsync(path.Path, token));

        var compatible = inspections
            .Where(item => item.IsCompatible)
            .Select(item => item.Descriptor!)
            .GroupBy(item => item.StoreId)
            // Manual copies share an identity. Preserve the explicitly remembered path before deduplicating.
            .Select(group => group.FirstOrDefault(item => preference is not null &&
                PathsEqual(item.DatabasePath, preference.DatabasePath)) ?? group.First())
            .ToArray();
        var problems = inspections
            .Where(item => item.Status is StoreInspectionStatus.FutureVersion or StoreInspectionStatus.Invalid)
            .ToArray();
        var portable = compatible.FirstOrDefault(item =>
            PathsEqual(item.DatabasePath, locations.PortableDatabasePath) && item.Scope == StorageScope.Portable);
        var preferred = preference is null
            ? null
            : compatible.FirstOrDefault(item => item.StoreId == preference.StoreId &&
                PathsEqual(item.DatabasePath, preference.DatabasePath));

        var unseenPortable = portable is not null &&
            portable.StoreId != preferred?.StoreId &&
            portable.StoreId != preference?.ObservedPortableStoreId;
        if (preferred is not null && !unseenPortable)
        {
            var selected = await ActivateAsync(preferred, compatible, token);
            return new StorageDiscoveryResult(selected, compatible, problems);
        }

        if (compatible.Length == 1 && !unseenPortable)
        {
            var selected = await ActivateAsync(compatible[0], compatible, token);
            return new StorageDiscoveryResult(selected, compatible, problems);
        }

        if (compatible.Length > 1 || unseenPortable)
            return new StorageDiscoveryResult(null, compatible, problems);

        if (problems.Length > 0)
        {
            var details = string.Join(Environment.NewLine,
                problems.Select(item => $"{item.DatabasePath}: {item.ErrorMessage}"));
            throw new ApplicationStoreException(
                "Foram encontrados bancos incompatíveis ou inválidos. Nenhum arquivo foi alterado." +
                Environment.NewLine + details);
        }

        var created = await SqliteApplicationStore.OpenOrCreateAsync(
            locations.LocalDatabasePath,
            StorageScope.LocalUser,
            token: token);
        SavePreference(created.Descriptor, observedPortableStoreId: null);
        return new StorageDiscoveryResult(created, [created.Descriptor], []);
    }

    public async Task<SqliteApplicationStore> ActivateAsync(
        StoreDescriptor descriptor,
        IReadOnlyCollection<StoreDescriptor> discoveredCandidates,
        CancellationToken token = default)
    {
        var store = await SqliteApplicationStore.OpenOrCreateAsync(
            descriptor.DatabasePath,
            descriptor.Scope,
            descriptor.ClonedFromStoreId,
            token);
        var observedPortable = discoveredCandidates.FirstOrDefault(item =>
            PathsEqual(item.DatabasePath, locations.PortableDatabasePath) && item.Scope == StorageScope.Portable)?.StoreId;
        SavePreference(store.Descriptor, observedPortable);
        return store;
    }

    private async Task<SqliteApplicationStore> OpenExplicitAsync(
        string path,
        StorageScope scope,
        CancellationToken token)
    {
        var inspection = await SqliteApplicationStore.InspectAsync(path, token);
        return inspection.Status switch
        {
            StoreInspectionStatus.Missing => await SqliteApplicationStore.OpenOrCreateAsync(path, scope, token: token),
            StoreInspectionStatus.Compatible => await SqliteApplicationStore.OpenOrCreateAsync(
                path,
                inspection.Descriptor!.Scope,
                inspection.Descriptor.ClonedFromStoreId,
                token),
            _ => throw new ApplicationStoreException(
                $"O armazenamento explicitamente informado não pode ser aberto: {inspection.ErrorMessage}")
        };
    }

    private IEnumerable<(string Path, StorageScope Scope)> CandidatePaths(StoragePreference? preference)
    {
        var seen = new HashSet<string>(PathComparer);
        foreach (var candidate in new[]
                 {
                     (locations.PortableDatabasePath, StorageScope.Portable),
                     (locations.LocalDatabasePath, StorageScope.LocalUser),
                     (locations.SharedDatabasePath, StorageScope.SharedMachine)
                 })
        {
            var fullPath = Path.GetFullPath(candidate.Item1);
            if (seen.Add(fullPath) && File.Exists(fullPath))
                yield return (fullPath, candidate.Item2);
        }

        if (preference is not null)
        {
            var fullPath = Path.GetFullPath(preference.DatabasePath);
            if (seen.Add(fullPath) && File.Exists(fullPath))
                yield return (fullPath, preference.Scope);
        }
    }

    private StorageScope ScopeForPath(string path)
    {
        if (PathsEqual(path, locations.LocalDatabasePath))
            return StorageScope.LocalUser;
        if (PathsEqual(path, locations.SharedDatabasePath))
            return StorageScope.SharedMachine;
        if (PathsEqual(path, locations.PortableDatabasePath))
            return StorageScope.Portable;
        return StorageScope.Custom;
    }

    private void SavePreference(StoreDescriptor descriptor, Guid? observedPortableStoreId) =>
        preferences.Write(new StoragePreference(
            descriptor.StoreId,
            descriptor.DatabasePath,
            descriptor.Scope,
            observedPortableStoreId));

    private static string? ReadExplicitDataDirectory(IReadOnlyList<string> arguments)
    {
        for (var index = 0; index < arguments.Count; index++)
        {
            var argument = arguments[index];
            if (argument.StartsWith("--data-dir=", StringComparison.OrdinalIgnoreCase))
            {
                var value = argument["--data-dir=".Length..].Trim();
                return string.IsNullOrWhiteSpace(value)
                    ? throw new ArgumentException("O argumento --data-dir exige uma pasta.")
                    : value;
            }

            if (!argument.Equals("--data-dir", StringComparison.OrdinalIgnoreCase))
                continue;
            if (index + 1 >= arguments.Count || string.IsNullOrWhiteSpace(arguments[index + 1]))
                throw new ArgumentException("O argumento --data-dir exige uma pasta.");
            return arguments[index + 1].Trim();
        }

        return null;
    }

    private static bool PathsEqual(string left, string right) =>
        PathComparer.Equals(Path.GetFullPath(left), Path.GetFullPath(right));
}

using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using DBConnectionTester.Models;
using DBConnectionTester.Services.Storage;

namespace DBConnectionTester.Services.Portability;

public interface IApplicationBinaryProvider
{
    string? GetSingleFileExecutablePath();
}

public sealed class CurrentApplicationBinaryProvider : IApplicationBinaryProvider
{
    public string? GetSingleFileExecutablePath()
    {
        var assemblyLocation = Assembly.GetEntryAssembly()?.Location;
        var processPath = Environment.ProcessPath;
        return string.IsNullOrEmpty(assemblyLocation) &&
               !string.IsNullOrWhiteSpace(processPath) &&
               string.Equals(Path.GetExtension(processPath), ".exe", StringComparison.OrdinalIgnoreCase)
            ? processPath
            : null;
    }
}

public sealed class PortablePackageService
{
    private const string ManifestEntry = "manifest.json";
    private const string DatabaseEntry = "Data/data.db";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
        WriteIndented = true
    };
    private readonly SqliteApplicationStore currentStore;
    private readonly IApplicationBinaryProvider binaryProvider;

    public PortablePackageService(SqliteApplicationStore currentStore, IApplicationBinaryProvider? binaryProvider = null)
    {
        this.currentStore = currentStore;
        this.binaryProvider = binaryProvider ?? new CurrentApplicationBinaryProvider();
    }

    public async Task<PortablePackageResult> CreateAsync(
        string packagePath,
        bool includeExecutable,
        CancellationToken token = default)
    {
        using var lease = RunWriteLease.Acquire(currentStore);
        var executable = includeExecutable ? binaryProvider.GetSingleFileExecutablePath() : null;
        if (includeExecutable && (executable is null || !File.Exists(executable)))
            throw new InvalidOperationException("O executável só pode ser incluído quando a aplicação está publicada como single-file.");

        var temporaryDirectory = CreateTemporaryDirectory();
        try
        {
            var databasePath = Path.Combine(temporaryDirectory, "Data", "data.db");
            var clone = await StoreCloneService.CloneAsync(currentStore, databasePath, StorageScope.Portable, token);
            var executableEntry = executable is null ? null : Path.GetFileName(executable);
            var manifest = new PortablePackageManifest(
                1,
                DateTimeOffset.UtcNow,
                Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0.0.0",
                currentStore.Descriptor.StoreId,
                clone.Descriptor.StoreId,
                DatabaseEntry,
                await Sha256Async(databasePath, token),
                executable is not null,
                executableEntry,
                executable is null ? null : await Sha256Async(executable, token));
            var manifestPath = Path.Combine(temporaryDirectory, ManifestEntry);
            await File.WriteAllTextAsync(manifestPath, JsonSerializer.Serialize(manifest, JsonOptions), new UTF8Encoding(false), token);

            var fullPackagePath = Path.GetFullPath(packagePath);
            Directory.CreateDirectory(Path.GetDirectoryName(fullPackagePath)!);
            await using var stream = new FileStream(fullPackagePath, new FileStreamOptions
            {
                Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.Read,
                Options = FileOptions.Asynchronous | FileOptions.SequentialScan
            });
            using var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true, Encoding.UTF8);
            archive.CreateEntryFromFile(databasePath, DatabaseEntry, CompressionLevel.Optimal);
            archive.CreateEntryFromFile(manifestPath, ManifestEntry, CompressionLevel.Optimal);
            if (executable is not null)
                archive.CreateEntryFromFile(executable, executableEntry!, CompressionLevel.Optimal);
            return new PortablePackageResult(fullPackagePath, clone.Descriptor.StoreId, executable is not null);
        }
        finally
        {
            if (Directory.Exists(temporaryDirectory))
                Directory.Delete(temporaryDirectory, recursive: true);
        }
    }

    public async Task<PortablePackageRestoreResult> RestoreAsync(
        string packagePath,
        string destinationDirectory,
        CancellationToken token = default)
    {
        using var lease = RunWriteLease.Acquire(currentStore);
        var package = Path.GetFullPath(packagePath);
        if (!File.Exists(package))
            throw new FileNotFoundException("O pacote portátil não existe.", package);
        var destination = Path.GetFullPath(destinationDirectory);
        EnsureEmptyDestination(destination);
        var staging = CreateTemporaryDirectory(Path.GetDirectoryName(destination));
        try
        {
            PortablePackageManifest manifest;
            using (var archive = ZipFile.OpenRead(package))
            {
                ValidateEntries(archive);
                var manifestEntry = archive.GetEntry(ManifestEntry)
                    ?? throw new InvalidDataException("O manifesto do pacote está ausente.");
                await using (var stream = manifestEntry.Open())
                    manifest = await JsonSerializer.DeserializeAsync<PortablePackageManifest>(stream, JsonOptions, token)
                        ?? throw new InvalidDataException("O manifesto do pacote é inválido.");
                ValidateManifest(manifest, archive);
                foreach (var entry in archive.Entries.Where(entry => entry.Length > 0))
                {
                    var output = SafeDestinationPath(staging, entry.FullName);
                    Directory.CreateDirectory(Path.GetDirectoryName(output)!);
                    await using var input = entry.Open();
                    await using var file = new FileStream(output, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true);
                    await input.CopyToAsync(file, token);
                }
            }

            var databasePath = Path.Combine(staging, DatabaseEntry.Replace('/', Path.DirectorySeparatorChar));
            if (!FixedTimeEquals(manifest.DatabaseSha256, await Sha256Async(databasePath, token)))
                throw new InvalidDataException("O checksum do banco não corresponde ao manifesto.");
            if (manifest.IncludesExecutable)
            {
                var executablePath = Path.Combine(staging, manifest.ExecutablePath!);
                if (!FixedTimeEquals(manifest.ExecutableSha256!, await Sha256Async(executablePath, token)))
                    throw new InvalidDataException("O checksum do executável não corresponde ao manifesto.");
            }
            var inspection = await SqliteApplicationStore.InspectAsync(databasePath, token);
            if (!inspection.IsCompatible || inspection.Descriptor?.StoreId != manifest.PortableStoreId || inspection.Descriptor.Scope != StorageScope.Portable)
                throw new InvalidDataException("O banco do pacote não é um armazenamento portátil compatível.");

            if (Directory.Exists(destination))
                Directory.Delete(destination, recursive: false);
            Directory.Move(staging, destination);
            staging = "";
            var restoredDatabase = Path.Combine(destination, DatabaseEntry.Replace('/', Path.DirectorySeparatorChar));
            var store = await SqliteApplicationStore.OpenOrCreateAsync(restoredDatabase, StorageScope.Portable, token: token);
            var restoredExecutable = manifest.IncludesExecutable ? Path.Combine(destination, manifest.ExecutablePath!) : null;
            return new PortablePackageRestoreResult(destination, store.Descriptor, restoredExecutable);
        }
        finally
        {
            if (!string.IsNullOrEmpty(staging) && Directory.Exists(staging))
                Directory.Delete(staging, recursive: true);
        }
    }

    private static void ValidateEntries(ZipArchive archive)
    {
        foreach (var entry in archive.Entries)
        {
            var normalized = entry.FullName.Replace('\\', '/');
            if (Path.IsPathRooted(normalized) || normalized.StartsWith('/') || normalized.Split('/').Any(part => part is ".." or ""))
                throw new InvalidDataException("O pacote contém um caminho inseguro.");
        }
    }

    private static void ValidateManifest(PortablePackageManifest manifest, ZipArchive archive)
    {
        if (manifest.FormatVersion != 1 || manifest.DatabasePath != DatabaseEntry || string.IsNullOrWhiteSpace(manifest.DatabaseSha256))
            throw new InvalidDataException("A versão ou o conteúdo do manifesto não é suportado.");
        if (archive.GetEntry(DatabaseEntry) is null)
            throw new InvalidDataException("O banco do pacote está ausente.");
        if (manifest.IncludesExecutable &&
            (string.IsNullOrWhiteSpace(manifest.ExecutablePath) || string.IsNullOrWhiteSpace(manifest.ExecutableSha256) || archive.GetEntry(manifest.ExecutablePath) is null))
            throw new InvalidDataException("O executável descrito no manifesto está ausente.");
    }

    private static void EnsureEmptyDestination(string destination)
    {
        if (File.Exists(destination))
            throw new IOException("O destino já está ocupado por um arquivo.");
        if (Directory.Exists(destination) && Directory.EnumerateFileSystemEntries(destination).Any())
            throw new IOException("O diretório de destino não está vazio.");
    }

    private static string SafeDestinationPath(string root, string entryName)
    {
        var rootPrefix = Path.GetFullPath(root) + Path.DirectorySeparatorChar;
        var destination = Path.GetFullPath(Path.Combine(root, entryName.Replace('/', Path.DirectorySeparatorChar)));
        if (!destination.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("O pacote contém um caminho fora do destino.");
        return destination;
    }

    private static string CreateTemporaryDirectory(string? parent = null)
    {
        parent ??= Path.Combine(Path.GetTempPath(), "DBConnectionTester", "packages");
        Directory.CreateDirectory(parent);
        var path = Path.Combine(parent, ".dbct-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static async Task<string> Sha256Async(string path, CancellationToken token)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, token)).ToLowerInvariant();
    }

    private static bool FixedTimeEquals(string expected, string actual)
    {
        try { return CryptographicOperations.FixedTimeEquals(Convert.FromHexString(expected), Convert.FromHexString(actual)); }
        catch (FormatException) { return false; }
    }
}

namespace DBConnectionTester.Models;

public sealed record PortablePackageManifest(
    int FormatVersion,
    DateTimeOffset CreatedAt,
    string ApplicationVersion,
    Guid SourceStoreId,
    Guid PortableStoreId,
    string DatabasePath,
    string DatabaseSha256,
    bool IncludesExecutable,
    string? ExecutablePath,
    string? ExecutableSha256);

public sealed record PortablePackageResult(
    string PackagePath,
    Guid PortableStoreId,
    bool IncludesExecutable);

public sealed record PortablePackageRestoreResult(
    string DestinationDirectory,
    StoreDescriptor Store,
    string? ExecutablePath);

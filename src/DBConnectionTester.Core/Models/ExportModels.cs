namespace DBConnectionTester.Models;

public enum RunExportFormat
{
    Csv,
    Text,
    Json
}

public sealed record RunExportResult(Guid RunId, IReadOnlyList<string> Files);

public sealed record RunExportManifest(
    int FormatVersion,
    Guid RunId,
    DateTimeOffset CreatedAt,
    IReadOnlyList<RunExportManifestFile> Files);

public sealed record RunExportManifestFile(string Path, string Sha256);

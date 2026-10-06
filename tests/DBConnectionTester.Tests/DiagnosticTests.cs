using System.Net.Sockets;
using DBConnectionTester.Models;
using DBConnectionTester.Services;
using Microsoft.Data.Sqlite;

namespace DBConnectionTester.Tests;

public sealed class DiagnosticTests
{
    [Fact]
    public void CatalogCodesAreUniqueAndEveryDiagnosticHasKnownSuggestion()
    {
        Assert.Equal(
            DiagnosticCatalog.Diagnostics.Count,
            DiagnosticCatalog.Diagnostics.Select(item => item.Code).Distinct().Count());
        Assert.Equal(
            DiagnosticCatalog.Suggestions.Count,
            DiagnosticCatalog.Suggestions.Select(item => item.Code).Distinct().Count());

        var suggestions = DiagnosticCatalog.Suggestions.Select(item => item.Code).ToHashSet();
        Assert.All(DiagnosticCatalog.Diagnostics, item => Assert.Contains(item.DefaultSuggestionCode, suggestions));
    }

    [Fact]
    public void TcpDiagnosticPreservesOriginalSocketCode()
    {
        var diagnostic = DiagnosticClassifier.Tcp(
            new SocketException((int)SocketError.ConnectionRefused));

        Assert.Equal(DiagnosticCodes.TcpRefused, diagnostic.DiagnosticCode);
        Assert.Equal("ConnectionRefused", diagnostic.ProviderError?.OriginalCode);
        Assert.False(string.IsNullOrWhiteSpace(diagnostic.ProviderError?.NativeCode));
    }

    [Fact]
    public void DatabaseDiagnosticRedactsPassword()
    {
        const string secret = "very-secret-password";
        var diagnostic = DiagnosticClassifier.Database(
            new InvalidOperationException($"Access denied; Password={secret}"),
            DiagnosticLayer.DatabaseConnect,
            Settings(secret));

        Assert.Equal(DiagnosticCodes.DatabaseAuthentication, diagnostic.DiagnosticCode);
        Assert.Equal(DiagnosticConfidence.Heuristic, diagnostic.Confidence);
        Assert.DoesNotContain(secret, diagnostic.TechnicalMessage);
        Assert.All(diagnostic.ProviderError!.Errors, error => Assert.DoesNotContain(secret, error.Message));
    }

    [Fact]
    public void DatabaseDiagnosticKeepsTypedProviderInsteadOfInnerException()
    {
        var providerException = new SqliteException("file is not a database", 26, 26);

        var diagnostic = DiagnosticClassifier.Database(
            providerException,
            DiagnosticLayer.DatabaseConnect,
            Settings("secret") with { DatabaseType = DatabaseType.Sqlite });

        Assert.Equal(DiagnosticCodes.SqliteInvalid, diagnostic.DiagnosticCode);
        Assert.Equal("SQLite", diagnostic.ProviderError?.Provider);
        Assert.Equal("26", diagnostic.ProviderError?.OriginalCode);
        Assert.Equal("26", diagnostic.ProviderError?.NativeCode);
    }

    [Fact]
    public void DedicatedDocumentationContainsEveryCatalogCode()
    {
        var root = FindRepositoryRoot();
        var documentation = string.Join(
            "\n",
            Directory.GetFiles(Path.Combine(root, "docs", "diagnostics"), "*.md")
                .Select(File.ReadAllText));

        Assert.All(DiagnosticCatalog.Diagnostics, item => Assert.Contains(item.Code, documentation));
        Assert.All(DiagnosticCatalog.Suggestions, item => Assert.Contains(item.Code, documentation));
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "DBConnectionTester.sln")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Raiz do repositório não encontrada.");
    }

    private static TestSettings Settings(string password) => new(
        DatabaseType.MySqlMariaDb,
        "127.0.0.1",
        NetworkPort.Create(3306),
        "user",
        password,
        "database",
        "",
        SqlServerAuthentication.SqlLogin,
        "",
        RunCount.Create(1),
        false,
        TestInterval.Create(TimeSpan.Zero),
        StageTimeout.Create(TimeSpan.FromSeconds(1)),
        true,
        true,
        true,
        true,
        "result.csv",
        "result.txt");
}

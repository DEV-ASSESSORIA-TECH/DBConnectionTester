namespace DBConnectionTester.Application;

public sealed record OutputPaths(string CsvPath, string TxtPath);

public sealed class OutputPathPolicy
{
    public OutputPaths PrepareLegacy(string directory, Guid runId, DateTimeOffset startedAt)
    {
        if (string.IsNullOrWhiteSpace(directory))
            throw new ArgumentException("Informe a pasta para a saída CSV/TXT contínua.", nameof(directory));

        directory = Path.GetFullPath(directory.Trim());
        Directory.CreateDirectory(directory);
        var baseName = $"connection_test_{startedAt:yyyyMMdd_HHmmss}_{runId:N}"[..48];
        var csvPath = FindAvailablePath(Path.Combine(directory, baseName + ".csv"));
        return new OutputPaths(csvPath, Path.ChangeExtension(csvPath, ".txt"));
    }

    private static string FindAvailablePath(string requestedPath)
    {
        if (PairIsAvailable(requestedPath))
            return requestedPath;

        var directory = Path.GetDirectoryName(requestedPath)!;
        var name = Path.GetFileNameWithoutExtension(requestedPath);
        var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");

        for (var suffix = 0; ; suffix++)
        {
            var suffixText = suffix == 0 ? "" : $"_{suffix}";
            var candidate = Path.Combine(directory, $"{name}_{timestamp}{suffixText}.csv");
            if (PairIsAvailable(candidate))
                return candidate;
        }
    }

    private static bool PairIsAvailable(string csvPath) =>
        !File.Exists(csvPath) && !File.Exists(Path.ChangeExtension(csvPath, ".txt"));
}

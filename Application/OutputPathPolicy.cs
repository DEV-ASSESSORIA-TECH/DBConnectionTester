namespace DBConnectionTester.Application;

public sealed record OutputPaths(string CsvPath, string TxtPath);

public sealed class OutputPathPolicy
{
    public OutputPaths Prepare(string requestedPath)
    {
        if (string.IsNullOrWhiteSpace(requestedPath))
            throw new ArgumentException("Informe o arquivo CSV de saída.", nameof(requestedPath));

        var csvPath = requestedPath.Trim();
        if (!csvPath.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
            csvPath += ".csv";

        csvPath = Path.GetFullPath(csvPath);
        var directory = Path.GetDirectoryName(csvPath)
            ?? throw new IOException("Não foi possível determinar a pasta de saída.");
        Directory.CreateDirectory(directory);

        csvPath = FindAvailablePath(csvPath);
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

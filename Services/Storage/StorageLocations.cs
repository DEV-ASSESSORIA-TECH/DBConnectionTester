namespace DBConnectionTester.Services.Storage;

public sealed record StorageLocations(
    string LocalDatabasePath,
    string SharedDatabasePath,
    string PortableDatabasePath)
{
    public const string DatabaseFileName = "data.db";

    public static StorageLocations CreateDefault() => new(
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DBConnectionTester",
            DatabaseFileName),
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "DBConnectionTester",
            DatabaseFileName),
        Path.Combine(AppContext.BaseDirectory, "Data", DatabaseFileName));
}

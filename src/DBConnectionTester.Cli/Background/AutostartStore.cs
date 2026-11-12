using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using DBConnectionTester.Application;
using DBConnectionTester.Models;

namespace DBConnectionTester.Cli.Background;

/// <summary>Who runs the continuous execution started with Windows.</summary>
internal enum AutostartScope
{
    /// <summary>SYSTEM, at boot, without anyone logged on. Requires administrator.</summary>
    System,
    /// <summary>The current user, at logon. No administrator needed.</summary>
    User
}

/// <summary>Target and stages of an autostart run: the run parameters, never a password.</summary>
internal sealed record AutostartSettings(
    DatabaseType DatabaseType, string Host, int Port, string User, string Database, string SqliteFile,
    SqlServerAuthentication SqlServerAuthentication, string OdbcDriver, double IntervalSeconds,
    double TimeoutSeconds, bool Dns, bool Ping, bool Tcp, bool DatabaseTest, Guid? ProfileId)
{
    public static AutostartSettings From(TestSettingsInput input) => new(
        input.DatabaseType, input.Host, input.Port, input.User, input.Database, input.SqliteFile,
        input.SqlServerAuthentication, input.OdbcDriver, input.Interval.TotalSeconds, input.Timeout.TotalSeconds,
        input.Dns, input.Ping, input.Tcp, input.DatabaseTest, input.ProfileId);

    public TestSettingsInput ToInput(string password) => new(
        DatabaseType, Host, Port, User, password, Database, SqliteFile, SqlServerAuthentication, OdbcDriver,
        ProfileExecutionDefaults.Default.TestCount, Continuous: true, TimeSpan.FromSeconds(IntervalSeconds),
        TimeSpan.FromSeconds(TimeoutSeconds), Dns, Ping, Tcp, DatabaseTest, ProfileId);
}

internal sealed record AutostartConfig(
    int FormatVersion, AutostartScope Scope, string? UserName, string TaskName, string Executable,
    bool HasPassword, DateTimeOffset CreatedAt, AutostartSettings Settings);

/// <summary>Loop state written by "dbct autostart run" and read by status commands.</summary>
internal sealed record AutostartState(
    int? ProcessId, DateTimeOffset? StartedAt, int Restarts, int? LastExitCode, DateTimeOffset? LastExitAt, string? LastError);

/// <summary>
/// Autostart files, kept next to the store (folder "autostart" beside data.db) so each store
/// has its own configuration: config.json, password.dpapi (DPAPI) and state.json.
/// </summary>
internal sealed class AutostartStore(string storeDirectory)
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("DBConnectionTester-Autostart-v1");
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    public string Directory { get; } = Path.Combine(storeDirectory, "autostart");
    private string ConfigPath => Path.Combine(Directory, "config.json");
    private string PasswordPath => Path.Combine(Directory, "password.dpapi");
    private string StatePath => Path.Combine(Directory, "state.json");

    /// <summary>Task name unique per store, so two stores never share a task.</summary>
    public static string TaskNameFor(string storeDirectory)
    {
        var key = Path.GetFullPath(storeDirectory).TrimEnd(Path.DirectorySeparatorChar).ToUpperInvariant();
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)))[..8].ToLowerInvariant();
        return $@"\DB Connection Tester\Execucao continua {hash}";
    }

    public AutostartConfig? Load()
    {
        if (!File.Exists(ConfigPath)) return null;
        return JsonSerializer.Deserialize<AutostartConfig>(File.ReadAllText(ConfigPath), Json);
    }

    public void Save(AutostartConfig config, string? password)
    {
        CreatePrivateDirectory(config.Scope);
        if (password is not null)
            File.WriteAllBytes(PasswordPath, Protect(password, config.Scope));
        else if (File.Exists(PasswordPath))
            File.Delete(PasswordPath);
        File.WriteAllText(ConfigPath, JsonSerializer.Serialize(config, Json));
    }

    public string? ReadPassword(AutostartConfig config)
    {
        if (!config.HasPassword) return null;
        var bytes = File.ReadAllBytes(PasswordPath);
        if (!OperatingSystem.IsWindows()) return Encoding.UTF8.GetString(bytes);
        var scope = config.Scope == AutostartScope.System ? DataProtectionScope.LocalMachine : DataProtectionScope.CurrentUser;
        return Encoding.UTF8.GetString(ProtectedData.Unprotect(bytes, Entropy, scope));
    }

    public AutostartState? LoadState()
    {
        try { return File.Exists(StatePath) ? JsonSerializer.Deserialize<AutostartState>(File.ReadAllText(StatePath), Json) : null; }
        catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException) { return null; }
    }

    public void SaveState(AutostartState state)
    {
        try
        {
            var temporary = StatePath + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(state, Json));
            File.Move(temporary, StatePath, overwrite: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
    }

    public void Delete()
    {
        if (System.IO.Directory.Exists(Directory)) System.IO.Directory.Delete(Directory, recursive: true);
    }

    private static byte[] Protect(string password, AutostartScope scope)
    {
        var bytes = Encoding.UTF8.GetBytes(password);
        if (!OperatingSystem.IsWindows()) return bytes;
        // SYSTEM cannot read a user's DPAPI secret: machine scope plus the folder ACL below.
        var protectionScope = scope == AutostartScope.System ? DataProtectionScope.LocalMachine : DataProtectionScope.CurrentUser;
        return ProtectedData.Protect(bytes, Entropy, protectionScope);
    }

    // Only SYSTEM, Administrators and the configuring user: a shared store folder must not
    // expose a machine-scope secret to other local users.
    private void CreatePrivateDirectory(AutostartScope scope)
    {
        if (!OperatingSystem.IsWindows())
        {
            System.IO.Directory.CreateDirectory(Directory);
            return;
        }
        var security = new DirectorySecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        var inheritance = InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit;
        var owners = new List<IdentityReference>
        {
            new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
            new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null)
        };
        if (WindowsIdentity.GetCurrent().User is { } current) owners.Add(current);
        foreach (var identity in owners)
            security.AddAccessRule(new FileSystemAccessRule(identity, FileSystemRights.FullControl, inheritance,
                PropagationFlags.None, AccessControlType.Allow));
        var directory = new DirectoryInfo(Directory);
        if (directory.Exists) directory.SetAccessControl(security);
        else directory.Create(security);
    }
}

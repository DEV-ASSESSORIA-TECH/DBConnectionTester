using System.Diagnostics;
using System.Security.Principal;
using System.Text;
using System.Xml.Linq;

namespace DBConnectionTester.Cli.Background;

/// <summary>Windows task that starts the autostart loop. Replaceable in tests.</summary>
public interface IAutostartScheduler
{
    bool IsElevated { get; }
    string CurrentUser { get; }
    void Register(string taskName, string taskXml);
    void Run(string taskName);
    void End(string taskName);
    void Delete(string taskName);
    bool Exists(string taskName);
}

/// <summary>Task Scheduler through schtasks.exe and a task XML (full control of the settings).</summary>
internal sealed class SchtasksScheduler : IAutostartScheduler
{
    public bool IsElevated => OperatingSystem.IsWindows() &&
        new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);

    public string CurrentUser => OperatingSystem.IsWindows() ? WindowsIdentity.GetCurrent().Name : Environment.UserName;

    public void Register(string taskName, string taskXml)
    {
        var file = Path.Combine(Path.GetTempPath(), "dbct-task-" + Guid.NewGuid().ToString("N") + ".xml");
        try
        {
            // schtasks reads the XML as UTF-16, as its declaration states.
            File.WriteAllText(file, taskXml, Encoding.Unicode);
            Invoke(["/Create", "/TN", taskName, "/XML", file, "/F"], "registrar a tarefa");
        }
        finally { File.Delete(file); }
    }

    public void Run(string taskName) => Invoke(["/Run", "/TN", taskName], "iniciar a tarefa");

    public void End(string taskName) => Invoke(["/End", "/TN", taskName], "encerrar a tarefa", ignoreFailure: true);

    public void Delete(string taskName) => Invoke(["/Delete", "/TN", taskName, "/F"], "remover a tarefa");

    public bool Exists(string taskName) => Invoke(["/Query", "/TN", taskName], "consultar a tarefa", ignoreFailure: true) == 0;

    private static int Invoke(string[] arguments, string action, bool ignoreFailure = false)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("A execução automática usa o Agendador de Tarefas do Windows.");
        var info = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "schtasks.exe"))
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true
        };
        foreach (var argument in arguments) info.ArgumentList.Add(argument);
        using var process = Process.Start(info) ?? throw new InvalidOperationException("schtasks.exe não iniciou.");
        var output = process.StandardOutput.ReadToEndAsync();
        var errors = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(30_000))
        {
            process.Kill();
            throw new InvalidOperationException($"schtasks.exe não respondeu ao {action}.");
        }
        if (process.ExitCode != 0 && !ignoreFailure)
            throw new InvalidOperationException($"Não foi possível {action}: {errors.Result.Trim()} {output.Result.Trim()}".Trim());
        return process.ExitCode;
    }
}

internal static class AutostartTask
{
    private static readonly XNamespace Ns = "http://schemas.microsoft.com/windows/2004/02/mit/task";

    /// <summary>
    /// System: boot trigger as SYSTEM, without anyone logged on. User: logon trigger for that
    /// user. No time limit; the scheduler restarts the loop if it fails.
    /// </summary>
    public static string Build(AutostartScope scope, string userName, string executable, string storeDirectory)
    {
        var trigger = scope == AutostartScope.System
            ? new XElement(Ns + "BootTrigger", new XElement(Ns + "Enabled", "true"))
            : new XElement(Ns + "LogonTrigger", new XElement(Ns + "Enabled", "true"), new XElement(Ns + "UserId", userName));
        var principal = scope == AutostartScope.System
            ? new XElement(Ns + "Principal", new XAttribute("id", "Author"),
                new XElement(Ns + "UserId", "S-1-5-18"), new XElement(Ns + "RunLevel", "HighestAvailable"))
            : new XElement(Ns + "Principal", new XAttribute("id", "Author"),
                new XElement(Ns + "UserId", userName), new XElement(Ns + "LogonType", "InteractiveToken"),
                new XElement(Ns + "RunLevel", "LeastPrivilege"));
        var document = new XDocument(
            new XDeclaration("1.0", "UTF-16", null),
            new XElement(Ns + "Task", new XAttribute("version", "1.2"),
                new XElement(Ns + "RegistrationInfo",
                    new XElement(Ns + "Description",
                        $"Execução contínua do DB Connection Tester no banco {storeDirectory}. Gerenciada por: dbct autostart.")),
                new XElement(Ns + "Triggers", trigger),
                new XElement(Ns + "Principals", principal),
                new XElement(Ns + "Settings",
                    new XElement(Ns + "MultipleInstancesPolicy", "IgnoreNew"),
                    new XElement(Ns + "DisallowStartIfOnBatteries", "false"),
                    new XElement(Ns + "StopIfGoingOnBatteries", "false"),
                    new XElement(Ns + "StartWhenAvailable", "true"),
                    new XElement(Ns + "ExecutionTimeLimit", "PT0S"),
                    new XElement(Ns + "RestartOnFailure", new XElement(Ns + "Interval", "PT1M"), new XElement(Ns + "Count", "999")),
                    new XElement(Ns + "AllowHardTerminate", "true")),
                new XElement(Ns + "Actions", new XAttribute("Context", "Author"),
                    new XElement(Ns + "Exec",
                        new XElement(Ns + "Command", executable),
                        new XElement(Ns + "Arguments",
                            "autostart run --data-dir " + DetachedProcess.Quote(storeDirectory)),
                        new XElement(Ns + "WorkingDirectory", Path.GetDirectoryName(executable) ?? "")))));
        using var writer = new Utf16StringWriter();
        document.Save(writer);
        return writer.ToString();
    }

    private sealed class Utf16StringWriter : StringWriter
    {
        public override Encoding Encoding => Encoding.Unicode;
    }
}

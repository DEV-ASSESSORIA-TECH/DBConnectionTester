using System.CommandLine;
using System.Text.Json;
using System.Text.Json.Serialization;
using DBConnectionTester.Models;
using DBConnectionTester.Services.Storage;
using Microsoft.Data.Sqlite;

namespace DBConnectionTester.Cli;

public sealed partial class CliApplication
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
        WriteIndented = true
    };
    private readonly TextWriter output;
    private readonly TextWriter error;
    private readonly TextReader input;
    private readonly StorageLocations locations;
    private readonly IStoragePreferenceStore preferences;
    private string? secret;

    public CliApplication(TextWriter? output = null, TextWriter? error = null, TextReader? input = null,
        StorageLocations? locations = null, IStoragePreferenceStore? preferences = null)
    {
        this.output = output ?? Console.Out;
        this.error = error ?? Console.Error;
        this.input = input ?? Console.In;
        this.locations = locations ?? StorageLocations.CreateDefault();
        this.preferences = preferences ?? new RegistryStoragePreferenceStore();
    }

    public async Task<int> InvokeAsync(string[] args, CancellationToken token = default)
    {
        secret = null;
        try
        {
            var root = CreateCommand();
            var parsed = root.Parse(args);
            if (parsed.Errors.Count > 0)
            {
                // Do not echo rejected tokens: they may contain an accidentally supplied secret.
                error.WriteLine("Argumentos inválidos. Consulte dbct --help ou a ajuda do comando.");
                return 2;
            }
            return await parsed.InvokeAsync(new InvocationConfiguration
            {
                Output = output, Error = error, EnableDefaultExceptionHandler = false,
                ProcessTerminationTimeout = null
            }, token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            error.WriteLine("Operação cancelada.");
            return 130;
        }
        catch (RunAlreadyActiveException)
        {
            error.WriteLine("Outra execução está gravando neste armazenamento.");
            return 4;
        }
        catch (CliException exception)
        {
            error.WriteLine(Sanitize(exception.Message));
            return exception.ExitCode;
        }
        catch (ApplicationStoreException exception)
        {
            error.WriteLine(Sanitize(exception.Message));
            return 3;
        }
        catch (Exception exception) when (exception is ArgumentException or OverflowException)
        {
            error.WriteLine("Configuração inválida. Consulte a ajuda do comando.");
            return 2;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or SqliteException)
        {
            error.WriteLine("Falha operacional de acesso a dados ou arquivos. Verifique caminhos e permissões.");
            return 5;
        }
        catch (Exception)
        {
            error.WriteLine("Falha operacional. Consulte o histórico e verifique a configuração.");
            return 5;
        }
        finally { secret = null; }
    }

    private async Task<SqliteApplicationStore> OpenStoreAsync(ParseResult parse, bool allowCreate,
        CancellationToken token)
    {
        try
        {
            var directory = parse.GetValue<string?>("--data-dir");
            if (directory is not null && string.IsNullOrWhiteSpace(directory))
                throw new CliException(2, "--data-dir exige uma pasta.");
            var resolver = new StorageResolver(locations, preferences,
                new StorageResolutionOptions(allowCreate, PersistPreference: false));
            var resolution = await resolver.ResolveAsync(
                directory is null ? [] : ["--data-dir", directory], token);
            if (resolution.SelectedStore is null)
                throw new CliException(3, "Armazenamento ambíguo. Informe --data-dir. Candidatos:" +
                    Environment.NewLine + string.Join(Environment.NewLine,
                        resolution.Candidates.Select(item => item.DatabasePath)));
            return resolution.SelectedStore;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or SqliteException)
        {
            throw new CliException(3, "Não foi possível abrir o armazenamento. Verifique o caminho e as permissões.");
        }
    }

    private void WriteResult(ParseResult parse, object data)
    {
        var options = new JsonSerializerOptions(JsonOptions);
        if (!string.IsNullOrEmpty(secret)) options.Converters.Insert(0, new SecretStringConverter(secret));
        var element = JsonSerializer.SerializeToElement(data, options);
        if (parse.GetValue<string>("--format") == "json")
            output.WriteLine(JsonSerializer.Serialize(new { formatVersion = 1, data = element }, JsonOptions));
        else
            WriteText(element, "");
    }

    private void WriteText(JsonElement element, string indent)
    {
        if (element.ValueKind == JsonValueKind.Object)
            foreach (var property in element.EnumerateObject())
            {
                if (property.Value.ValueKind is JsonValueKind.Object or JsonValueKind.Array)
                {
                    output.WriteLine($"{indent}{property.Name}:");
                    WriteText(property.Value, indent + "  ");
                }
                else output.WriteLine($"{indent}{property.Name}: {property.Value}");
            }
        else if (element.ValueKind == JsonValueKind.Array)
            foreach (var item in element.EnumerateArray())
            {
                output.WriteLine(indent + "-");
                WriteText(item, indent + "  ");
            }
        else output.WriteLine(indent + element);
    }

    private sealed class SecretStringConverter(string password) : JsonConverter<string>
    {
        public override string? Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options) =>
            reader.GetString();

        public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options) =>
            writer.WriteStringValue(value.Replace(password, "***", StringComparison.Ordinal));
    }

    private string Sanitize(string message) => string.IsNullOrEmpty(secret)
        ? message : message.Replace(secret, "***", StringComparison.Ordinal);

    private sealed class CliException(int exitCode, string message) : Exception(message)
    {
        public int ExitCode { get; } = exitCode;
    }
}

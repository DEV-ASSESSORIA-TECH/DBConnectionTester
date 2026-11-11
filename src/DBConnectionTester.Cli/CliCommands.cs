using System.CommandLine;
using DBConnectionTester.Models;

namespace DBConnectionTester.Cli;

public sealed partial class CliApplication
{
    private RootCommand CreateCommand()
    {
        var root = new RootCommand("DB Connection Tester — diagnósticos, histórico e relatórios sem interface gráfica.");
        root.Options.Add(new Option<string?>("--data-dir")
        {
            Description = "Pasta do data.db (caminho relativo ao diretório corrente). Não altera a seleção da GUI.",
            Recursive = true
        });
        root.SetAction(_ => { output.WriteLine("Use dbct --help para consultar os comandos."); return 2; });

        var run = new Command("run", "Executar diagnósticos por parâmetros ou perfil salvo.");
        AddFormat(run);
        Add<Guid?>(run, "--profile", "GUID do perfil salvo.");
        var type = Add<string?>(run, "--type", "Tipo do destino.");
        type.AcceptOnlyFromAmong("mysql", "postgresql", "sqlserver", "sqlanywhere", "sqlite", "tcp");
        Add<string?>(run, "--host", "Servidor ou endereço IP.");
        Add<int?>(run, "--port", "Porta TCP.");
        Add<string?>(run, "--user", "Usuário do banco.");
        Add<string?>(run, "--database", "Nome do banco.");
        Add<string?>(run, "--sqlite-file", "Arquivo SQLite existente.");
        Add<string?>(run, "--odbc-driver", "Nome do driver ODBC x64 do SQL Anywhere.");
        Add<string?>(run, "--auth", "Autenticação SQL Server: windows ou sql-login.")
            .AcceptOnlyFromAmong("windows", "sql-login");
        Add<string?>(run, "--password-env", "Nome da variável de ambiente com a senha.");
        Add<bool>(run, "--password-stdin", "Ler uma linha de senha da entrada padrão.");
        Add<long?>(run, "--count", "Quantidade de ciclos (padrão: 1000).");
        Add<bool?>(run, "--continuous", "Executar até Ctrl+C; aceita true/false.");
        Add<double?>(run, "--interval", "Intervalo em segundos (padrão: 5).");
        Add<double?>(run, "--timeout", "Timeout em segundos (padrão: 5).");
        foreach (var stage in new[] { "dns", "ping", "tcp", "database-test" })
            Add<bool?>(run, "--" + stage, "Habilitar/desabilitar etapa; aceita true/false.");
        run.SetAction((parse, token) => RunAsync(parse, token));
        root.Subcommands.Add(run);

        var profiles = new Command("profiles", "Consultar perfis salvos, sem editar.");
        var listProfiles = new Command("list", "Listar perfis.");
        AddFormat(listProfiles);
        listProfiles.SetAction((parse, token) => ListProfilesAsync(parse, token));
        var showProfile = new Command("show", "Consultar um perfil pelo GUID.");
        showProfile.Arguments.Add(new Argument<Guid>("id"));
        AddFormat(showProfile);
        showProfile.SetAction((parse, token) => ShowProfileAsync(parse, token));
        profiles.Subcommands.Add(listProfiles);
        profiles.Subcommands.Add(showProfile);
        root.Subcommands.Add(profiles);

        var history = new Command("history", "Consultar execuções persistidas.");
        var listHistory = new Command("list", "Pesquisar histórico com filtros e paginação.");
        AddFormat(listHistory);
        AddPage(listHistory);
        Add<DateTimeOffset?>(listHistory, "--from", "Início do período (ISO 8601 com fuso).");
        Add<DateTimeOffset?>(listHistory, "--until", "Fim do período (ISO 8601 com fuso).");
        Add<Guid?>(listHistory, "--profile", "Filtrar pelo GUID do perfil.");
        Add<string?>(listHistory, "--target", "Filtrar destino.");
        Add<PersistedRunStatus?>(listHistory, "--status", "Running, Completed, Stopped, Failed ou Interrupted.");
        Add<string?>(listHistory, "--diagnostic", "Código do diagnóstico.");
        listHistory.SetAction((parse, token) => ListHistoryAsync(parse, token));
        var showHistory = new Command("show", "Consultar detalhes de uma execução.");
        showHistory.Arguments.Add(new Argument<Guid>("id"));
        AddFormat(showHistory);
        AddPage(showHistory);
        Add<bool>(showHistory, "--cycles", "Incluir uma página de ciclos.");
        showHistory.SetAction((parse, token) => ShowHistoryAsync(parse, token));
        history.Subcommands.Add(listHistory);
        history.Subcommands.Add(showHistory);
        root.Subcommands.Add(history);

        var export = new Command("export", "Exportar execução finalizada sem sobrescrever arquivos.");
        export.Arguments.Add(new Argument<Guid>("id"));
        Add(export, "--format", "Formato do relatório.", "json").AcceptOnlyFromAmong("csv", "txt", "json", "zip");
        export.Options.Add(new Option<string>("--output") { Required = true, Description = "Novo arquivo de destino." });
        export.SetAction((parse, token) => ExportAsync(parse, token));
        root.Subcommands.Add(export);
        return root;
    }

    private static Option<T> Add<T>(Command command, string name, string description, T defaultValue = default!)
    {
        var option = new Option<T>(name) { Description = description, DefaultValueFactory = _ => defaultValue };
        command.Options.Add(option);
        return option;
    }

    private static void AddFormat(Command command) =>
        Add(command, "--format", "Formato da saída.", "text").AcceptOnlyFromAmong("text", "json");

    private static void AddPage(Command command)
    {
        Add(command, "--page", "Página, começando em 1.", 1);
        Add(command, "--page-size", "Itens por página, de 1 a 500.", 50);
    }

    private static PageRequest Page(ParseResult parse)
    {
        var page = new PageRequest(parse.GetValue<int>("--page"), parse.GetValue<int>("--page-size"));
        page.Validate();
        return page;
    }
}

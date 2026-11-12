namespace DBConnectionTester.Models;

public static class DiagnosticFormatting
{
    public static string Compact(DiagnosticIssue? issue) =>
        issue is null ? "" : $"[{issue.DiagnosticCode}] {issue.UserMessage}";

    public static string Detailed(DiagnosticIssue? issue)
    {
        if (issue is null)
            return "";

        var suggestion = DiagnosticCatalog.GetSuggestion(issue.SuggestionCode);
        var provider = issue.ProviderError;
        var providerText = provider is null
            ? ""
            : $"\nProvider: {provider.Provider}" +
              Value(" | Código original: ", provider.OriginalCode) +
              Value(" | SQLSTATE: ", provider.SqlState) +
              Value(" | Nativo: ", provider.NativeCode);

        return $"[{issue.DiagnosticCode}] {issue.UserMessage}\n" +
               $"Sugestão [{suggestion.Code}]: {suggestion.Action}" +
               providerText +
               Value("\nDetalhe: ", issue.TechnicalMessage);
    }

    private static string Value(string prefix, string? value) =>
        string.IsNullOrWhiteSpace(value) ? "" : prefix + value;
}

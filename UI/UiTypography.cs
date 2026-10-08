namespace DBConnectionTester.UI;

internal static class UiTypography
{
    // Shared for the process lifetime; no font allocation when changing pages or themes.
    public static Font Body { get; } = new("Segoe UI", 9f);
    public static Font Emphasis { get; } = new("Segoe UI", 9f, FontStyle.Bold);
}

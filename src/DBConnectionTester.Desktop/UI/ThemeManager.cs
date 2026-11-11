using DBConnectionTester.Models;
using System.Collections.Immutable;

namespace DBConnectionTester.UI;

public sealed record ThemePalette(
    Color Window,
    Color Surface,
    Color SurfaceAlternative,
    Color Text,
    Color MutedText,
    Color Border,
    Color Accent,
    Color Selection)
{
    public MetricColors Metrics { get; init; } = new();
    public Color SelectionText { get; init; } = Text;
    public Color DisabledBackground { get; init; } = SurfaceAlternative;
    public Color DisabledText { get; init; } = MutedText;
    public Color Field { get; init; } = Surface;
    public Color Hover { get; init; } = Selection;
    public bool HighContrast { get; init; }
    public System.Collections.Immutable.ImmutableDictionary<(UiRole Role, UiState State), ControlColors> Roles { get; init; }
        = System.Collections.Immutable.ImmutableDictionary<(UiRole, UiState), ControlColors>.Empty;

    public ControlColors ColorsFor(Control control)
    {
        if (control is Button && !control.Enabled) return new(DisabledBackground, DisabledText);
        var role = UiStyle.GetRole(control);
        var state = UiStyle.GetState(control);
        if (Roles.TryGetValue((role, state), out var colors) || Roles.TryGetValue((role, UiState.Normal), out colors))
            return colors with { Background = colors.Background.IsEmpty ? control.Parent?.BackColor ?? Window : colors.Background };
        return new ControlColors(control switch
        {
            TextBoxBase or ComboBox or NumericUpDown => Field,
            ListBox or ListView or DataGridView or TabPage => Surface,
            GroupBox => Surface,
            Button => SurfaceAlternative,
            Form or UserControl => Window,
            _ => control.Parent?.BackColor ?? Window
        }, Text);
    }
}

public readonly record struct ControlColors(Color Background, Color Foreground);

public static class ThemeManager
{
    private static readonly ThemePalette lightPalette = CreatePalette(false);
    private static readonly ThemePalette darkPalette = CreatePalette(true);
    public static ThemePalette PaletteFor(ApplicationTheme theme)
    {
        if (SystemInformation.HighContrast)
            return new(SystemColors.Control, SystemColors.Window, SystemColors.ControlLight,
                SystemColors.ControlText, SystemColors.GrayText, SystemColors.ControlDark,
                SystemColors.Highlight, SystemColors.Highlight) { HighContrast = true };
        var dark = theme == ApplicationTheme.Dark || theme == ApplicationTheme.System && SystemUsesDarkTheme();
        return dark ? darkPalette : lightPalette;
    }

    private static ThemePalette CreatePalette(bool dark)
    {
        var palette = dark
            ? new ThemePalette(Color.FromArgb(23, 26, 32), Color.FromArgb(31, 35, 42), Color.FromArgb(42, 48, 58),
                Color.FromArgb(234, 239, 246), Color.FromArgb(166, 178, 195), Color.FromArgb(67, 77, 93),
                Color.FromArgb(37, 99, 235), Color.FromArgb(39, 65, 104))
            : new ThemePalette(Color.FromArgb(245, 247, 251), Color.White, Color.FromArgb(235, 240, 247),
                Color.FromArgb(25, 36, 52), Color.FromArgb(85, 101, 122), Color.FromArgb(203, 213, 225),
                Color.FromArgb(29, 78, 216), Color.FromArgb(219, 234, 254));
        var warning = dark ? Color.FromArgb(250, 204, 88) : Color.FromArgb(137, 83, 8);
        var error = dark ? Color.FromArgb(255, 151, 151) : Color.FromArgb(180, 35, 45);
        var success = dark ? Color.FromArgb(109, 217, 162) : Color.FromArgb(20, 115, 72);
        var danger = dark ? Color.FromArgb(176, 45, 56) : Color.FromArgb(185, 35, 49);
        palette = palette with
        {
            Field = dark ? Color.FromArgb(26, 30, 37) : Color.White,
            SelectionText = palette.Text,
            DisabledBackground = dark ? Color.FromArgb(35, 40, 48) : Color.FromArgb(237, 241, 246),
            DisabledText = dark ? Color.FromArgb(133, 145, 161) : Color.FromArgb(103, 119, 139),
            Metrics = new MetricColors
            {
                GridBackground = palette.Surface, GridHeader = palette.SurfaceAlternative,
                NormalStatisticsBackground = palette.Surface,
                FailureBackground = dark ? Color.FromArgb(75, 35, 43) : Color.FromArgb(255, 235, 237),
                Muted = palette.MutedText, Attention = error,
                Stable = dark ? Color.FromArgb(132, 186, 255) : Color.FromArgb(34, 100, 184),
                Improving = success, Degrading = warning, ChartGrid = palette.Border,
                Median = dark ? Color.FromArgb(166, 198, 246) : Color.FromArgb(59, 104, 169),
                P95 = dark ? Color.FromArgb(250, 204, 88) : Color.FromArgb(153, 91, 8),
                Series = dark ? Color.FromArgb(104, 172, 255) : Color.FromArgb(36, 105, 202),
                Failure = error, Skipped = palette.MutedText
            }
        };
        var roles = ImmutableDictionary<(UiRole, UiState), ControlColors>.Empty
            .Add((UiRole.Card, UiState.Normal), new(palette.Surface, palette.Text))
            .Add((UiRole.PrimaryAction, UiState.Normal), new(palette.Accent, Color.White))
            .Add((UiRole.DestructiveAction, UiState.Normal), new(danger, Color.White))
            .Add((UiRole.SecondaryText, UiState.Normal), new(Color.Empty, palette.MutedText))
            .Add((UiRole.Heading, UiState.Normal), new(Color.Empty, palette.Text))
            .Add((UiRole.NavigationContainer, UiState.Normal), new(dark ? Color.FromArgb(20, 23, 29) : Color.FromArgb(234, 239, 246), palette.Text))
            .Add((UiRole.Navigation, UiState.Normal), new(Color.Empty, palette.MutedText))
            .Add((UiRole.Navigation, UiState.Selected), new(palette.Selection, palette.Text))
            .Add((UiRole.Warning, UiState.Normal), new(Color.Empty, warning))
            .Add((UiRole.Error, UiState.Normal), new(Color.Empty, error))
            .Add((UiRole.Success, UiState.Normal), new(Color.Empty, success));
        foreach (var role in new[] { UiRole.Status, UiRole.SecondaryText })
        {
            roles = roles.Add((role, UiState.Warning), new(Color.Empty, warning))
                .Add((role, UiState.Error), new(Color.Empty, error))
                .Add((role, UiState.Success), new(Color.Empty, success))
                .Add((role, UiState.Busy), new(Color.Empty, palette.MutedText));
        }
        return palette with { Roles = roles };
    }

    private static bool SystemUsesDarkTheme()
    {
        try
        {
            return Microsoft.Win32.Registry.GetValue(
                @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
                "AppsUseLightTheme", 1) is int value && value == 0;
        }
        catch (Exception error) when (error is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        { return SystemColors.Window.GetBrightness() < 0.5f; }
    }

    public static void ConfigureNativeMode(ApplicationTheme theme)
    {
        System.Windows.Forms.Application.SetColorMode(theme switch
        {
            ApplicationTheme.Dark => SystemColorMode.Dark,
            ApplicationTheme.Light => SystemColorMode.Classic,
            _ => SystemColorMode.System
        });
    }

    public static void Apply(Form form, ApplicationTheme theme)
    {
        ConfigureNativeMode(theme);
        var palette = PaletteFor(theme);
        ApplyPalette(form, palette);
        form.BackColor = palette.Window;
        form.ForeColor = palette.Text;
        form.Invalidate(true);
    }

    /// <summary>Styles an existing control tree on theme changes, without rebuilding it or loading data.</summary>
    public static void ApplyPalette(Control control, ThemePalette palette) => ApplyCore(control, palette);

    internal static void ApplyControlColors(Control control, ThemePalette palette)
    {
        var colors = palette.ColorsFor(control);
        control.ForeColor = colors.Foreground;
        control.BackColor = colors.Background;
        if (control is Button button)
        {
            button.FlatAppearance.BorderColor = palette.Border;
            button.FlatAppearance.BorderSize = UiStyle.GetRole(button) is UiRole.Navigation or UiRole.PrimaryAction or UiRole.DestructiveAction ? 0 : 1;
            var semantic = UiStyle.GetRole(button) is UiRole.PrimaryAction or UiRole.DestructiveAction;
            button.FlatAppearance.MouseOverBackColor = semantic ? Shade(colors.Background, 0.08f) : palette.Hover;
            button.FlatAppearance.MouseDownBackColor = semantic ? Shade(colors.Background, 0.16f) : palette.Selection;
        }
    }

    private static Color Shade(Color color, float amount) => Color.FromArgb(
        (int)(color.R * (1 - amount)), (int)(color.G * (1 - amount)), (int)(color.B * (1 - amount)));

    private static void ApplyCore(Control control, ThemePalette palette)
    {
        control.SuspendLayout();
        try
        {
            UiStyle.BindPalette(control, palette);
            ApplyControlColors(control, palette);
            if (control is Button button)
            {
                button.UseVisualStyleBackColor = false;
                button.FlatStyle = FlatStyle.Flat;
                var navigation = UiStyle.GetRole(button) == UiRole.Navigation;
                if (!navigation)
                {
                    var scale = button.DeviceDpi / 96f;
                    button.MinimumSize = new Size(button.MinimumSize.Width, Math.Max(button.MinimumSize.Height, (int)(32 * scale)));
                    button.Padding = new Padding((int)(10 * scale), (int)(3 * scale), (int)(10 * scale), (int)(3 * scale));
                }
            }
            if (control is TextBoxBase textBox) textBox.BorderStyle = BorderStyle.FixedSingle;
            if (control is UpDownBase upDown) upDown.BorderStyle = BorderStyle.FixedSingle;
            if (control is DataGridView grid)
            {
                grid.BorderStyle = BorderStyle.FixedSingle;
                grid.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.Single;
                grid.BackgroundColor = palette.Surface;
                grid.GridColor = palette.Border;
                grid.DefaultCellStyle.BackColor = palette.Surface;
                grid.DefaultCellStyle.ForeColor = palette.Text;
                grid.DefaultCellStyle.SelectionBackColor = palette.Selection;
                grid.DefaultCellStyle.SelectionForeColor = palette.SelectionText;
                grid.AlternatingRowsDefaultCellStyle.BackColor = palette.SurfaceAlternative;
                grid.ColumnHeadersDefaultCellStyle.BackColor = palette.SurfaceAlternative;
                grid.ColumnHeadersDefaultCellStyle.ForeColor = palette.Text;
                grid.ColumnHeadersDefaultCellStyle.WrapMode = DataGridViewTriState.False;
                grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = palette.Selection;
                grid.ColumnHeadersDefaultCellStyle.SelectionForeColor = palette.SelectionText;
                grid.EnableHeadersVisualStyles = false;
            }
            if (control is ToolStrip strip)
            {
                strip.BackColor = palette.Surface;
                strip.ForeColor = palette.Text;
                foreach (ToolStripItem item in strip.Items)
                {
                    item.BackColor = palette.Surface;
                    item.ForeColor = palette.Text;
                }
            }

            NativeTheme.Bind(control, palette);

            foreach (Control child in control.Controls)
                ApplyCore(child, palette);

            if (control is IThemePaletteAware aware) aware.ApplyPalette(palette);
        }
        finally { control.ResumeLayout(true); }
    }
}

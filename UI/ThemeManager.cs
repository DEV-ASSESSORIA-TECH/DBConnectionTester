using DBConnectionTester.Models;

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
    public System.Collections.Immutable.ImmutableDictionary<(UiRole Role, UiState State), ControlColors> Roles { get; init; }
        = System.Collections.Immutable.ImmutableDictionary<(UiRole, UiState), ControlColors>.Empty;

    public ControlColors ColorsFor(Control control)
    {
        var role = UiStyle.GetRole(control);
        var state = UiStyle.GetState(control);
        if (Roles.TryGetValue((role, state), out var colors) || Roles.TryGetValue((role, UiState.Normal), out colors))
            return colors;
        // Semantic roles deliberately keep the current native appearance in this phase.
        return new ControlColors(control switch
        {
            TextBoxBase or ComboBox or NumericUpDown or ListBox or DataGridView => Surface,
            Button => SurfaceAlternative,
            _ => Window
        }, Text);
    }
}

public readonly record struct ControlColors(Color Background, Color Foreground);

public static class ThemeManager
{
    public static ThemePalette PaletteFor(ApplicationTheme theme) => theme switch
    {
        ApplicationTheme.Dark => new ThemePalette(
            Color.FromArgb(30, 32, 36), Color.FromArgb(39, 42, 47), Color.FromArgb(48, 52, 58),
            Color.FromArgb(238, 240, 244), Color.FromArgb(173, 179, 188), Color.FromArgb(72, 77, 85),
            Color.FromArgb(72, 132, 220), Color.FromArgb(58, 92, 142)) { SelectionText = Color.White },
        ApplicationTheme.Light => new ThemePalette(
            Color.FromArgb(246, 247, 249), Color.White, Color.FromArgb(238, 241, 245),
            Color.FromArgb(30, 33, 38), Color.FromArgb(100, 106, 116), Color.FromArgb(211, 216, 223),
            Color.FromArgb(36, 103, 194), Color.FromArgb(215, 230, 250)),
        _ => new ThemePalette(
            SystemColors.Control, SystemColors.Window, SystemColors.ControlLight,
            SystemColors.ControlText, SystemColors.GrayText, SystemColors.ControlDark,
            SystemColors.Highlight, SystemColors.Highlight)
    };

    public static void Apply(Form form, ApplicationTheme theme)
    {
        System.Windows.Forms.Application.SetColorMode(theme switch
        {
            ApplicationTheme.Dark => SystemColorMode.Dark,
            ApplicationTheme.Light => SystemColorMode.Classic,
            _ => SystemColorMode.System
        });
        var palette = PaletteFor(theme);
        ApplyPalette(form, palette);
        form.BackColor = palette.Window;
        form.ForeColor = palette.Text;
        form.Invalidate(true);
    }

    /// <summary>Styles an existing control tree on theme changes, without rebuilding it or loading data.</summary>
    public static void ApplyPalette(Control control, ThemePalette palette)
    {
        UiStyle.BindPalette(control, palette);
        var colors = palette.ColorsFor(control);
        control.ForeColor = colors.Foreground;
        control.BackColor = colors.Background;
        if (control is DataGridView grid)
        {
            grid.BackgroundColor = palette.Surface;
            grid.GridColor = palette.Border;
            grid.DefaultCellStyle.BackColor = palette.Surface;
            grid.DefaultCellStyle.ForeColor = palette.Text;
            grid.DefaultCellStyle.SelectionBackColor = palette.Selection;
            grid.DefaultCellStyle.SelectionForeColor = palette.SelectionText;
            grid.AlternatingRowsDefaultCellStyle.BackColor = palette.SurfaceAlternative;
            grid.ColumnHeadersDefaultCellStyle.BackColor = palette.SurfaceAlternative;
            grid.ColumnHeadersDefaultCellStyle.ForeColor = palette.Text;
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

        foreach (Control child in control.Controls)
            ApplyPalette(child, palette);

        if (control is IThemePaletteAware aware) aware.ApplyPalette(palette);

    }
}

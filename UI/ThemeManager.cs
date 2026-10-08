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
    Color Selection);

public static class ThemeManager
{
    public static ThemePalette PaletteFor(ApplicationTheme theme) => theme switch
    {
        ApplicationTheme.Dark => new ThemePalette(
            Color.FromArgb(30, 32, 36), Color.FromArgb(39, 42, 47), Color.FromArgb(48, 52, 58),
            Color.FromArgb(238, 240, 244), Color.FromArgb(173, 179, 188), Color.FromArgb(72, 77, 85),
            Color.FromArgb(72, 132, 220), Color.FromArgb(58, 92, 142)),
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
        ApplyControl(form, palette, theme == ApplicationTheme.Dark);
        form.BackColor = palette.Window;
        form.ForeColor = palette.Text;
        form.Invalidate(true);
    }

    private static void ApplyControl(Control control, ThemePalette palette, bool dark)
    {
        control.ForeColor = palette.Text;
        control.BackColor = control switch
        {
            TextBoxBase or ComboBox or NumericUpDown or ListBox or DataGridView => palette.Surface,
            Button => palette.SurfaceAlternative,
            _ => palette.Window
        };

        if (control is GroupBox or TabPage or UserControl or Panel or TableLayoutPanel or FlowLayoutPanel)
            control.BackColor = palette.Window;
        if (control is DataGridView grid)
        {
            grid.BackgroundColor = palette.Surface;
            grid.GridColor = palette.Border;
            grid.DefaultCellStyle.BackColor = palette.Surface;
            grid.DefaultCellStyle.ForeColor = palette.Text;
            grid.DefaultCellStyle.SelectionBackColor = palette.Selection;
            grid.DefaultCellStyle.SelectionForeColor = dark ? Color.White : palette.Text;
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
            ApplyControl(child, palette, dark);

    }
}

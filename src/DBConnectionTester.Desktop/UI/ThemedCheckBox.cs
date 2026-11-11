using System.Windows.Forms.VisualStyles;
using DBConnectionTester.Models;

namespace DBConnectionTester.UI;

/// <summary>Keeps native interaction; uses the palette for disabled text that native painting ignores.</summary>
internal sealed class ThemedCheckBox : CheckBox, IThemePaletteAware
{
    private ThemePalette palette = ThemeManager.PaletteFor(ApplicationTheme.Light);

    void IThemePaletteAware.ApplyPalette(ThemePalette value) { palette = value; Invalidate(); }

    protected override void OnPaint(PaintEventArgs e)
    {
        if (Enabled || palette.HighContrast) { base.OnPaint(e); return; }
        e.Graphics.Clear(BackColor);
        var state = Checked ? CheckBoxState.CheckedDisabled : CheckBoxState.UncheckedDisabled;
        var size = CheckBoxRenderer.GetGlyphSize(e.Graphics, state);
        var glyph = new Point(0, (Height - size.Height) / 2);
        CheckBoxRenderer.DrawCheckBox(e.Graphics, glyph, state);
        TextRenderer.DrawText(e.Graphics, Text, Font,
            new Rectangle(size.Width + 3, 0, Math.Max(0, Width - size.Width - 3), Height),
            palette.DisabledText, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
    }
}

using DBConnectionTester.Models;

namespace DBConnectionTester.UI;

/// <summary>Native tab navigation and pages with a small, buffered header paint.</summary>
internal sealed class ThemedTabControl : TabControl, IThemePaletteAware
{
    private ThemePalette palette = ThemeManager.PaletteFor(ApplicationTheme.Light);

    public ThemedTabControl()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        Padding = new Point(12, 6);
    }

    void IThemePaletteAware.ApplyPalette(ThemePalette value)
    {
        palette = value;
        Invalidate();
    }

    protected override void OnSelectedIndexChanged(EventArgs e)
    {
        base.OnSelectedIndexChanged(e);
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(palette.Surface);
        using var border = new Pen(palette.Border);
        var content = DisplayRectangle;
        if (content.Width > 0 && content.Height > 0)
            e.Graphics.DrawRectangle(border, content.X - 1, content.Y - 1, content.Width + 1, content.Height + 1);
        using var selected = new SolidBrush(palette.Selection);
        using var accent = new SolidBrush(palette.Accent);
        var scale = DeviceDpi / 96f;
        for (var index = 0; index < TabCount; index++)
        {
            var bounds = GetTabRect(index);
            if (!bounds.IntersectsWith(e.ClipRectangle)) continue;
            if (index == SelectedIndex)
            {
                e.Graphics.FillRectangle(selected, bounds);
                e.Graphics.FillRectangle(accent, bounds.Left, bounds.Bottom - Math.Max(2, (int)(2 * scale)), bounds.Width, Math.Max(2, (int)(2 * scale)));
            }
            TextRenderer.DrawText(e.Graphics, TabPages[index].Text, Font, bounds,
                index == SelectedIndex ? palette.Text : palette.MutedText,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            if (Focused && index == SelectedIndex)
            {
                var focus = Rectangle.Inflate(bounds, -4, -4);
                ControlPaint.DrawFocusRectangle(e.Graphics, focus, palette.Text, palette.Selection);
            }
        }
        base.OnPaint(e);
    }
}

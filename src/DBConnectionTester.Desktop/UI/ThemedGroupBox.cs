using System.Drawing.Drawing2D;
using DBConnectionTester.Models;

namespace DBConnectionTester.UI;

/// <summary>Keeps native group layout and accessibility; paints a subtle cached card outline.</summary>
internal sealed class ThemedGroupBox : GroupBox, IThemePaletteAware
{
    private ThemePalette palette = ThemeManager.PaletteFor(ApplicationTheme.Light);
    private GraphicsPath? outline;
    private Size outlineSize;
    private int outlineDpi;

    public ThemedGroupBox() => DoubleBuffered = true;

    void IThemePaletteAware.ApplyPalette(ThemePalette value) { palette = value; Invalidate(); }

    protected override void OnPaintBackground(PaintEventArgs e) => e.Graphics.Clear(Parent?.BackColor ?? palette.Window);

    protected override void OnPaint(PaintEventArgs e)
    {
        if (palette.HighContrast) { base.OnPaint(e); return; }
        if (Width < 2 || Height < 2) return;
        if (outline is null || outlineSize != Size || outlineDpi != DeviceDpi)
        {
            outline?.Dispose();
            outline = new GraphicsPath();
            outlineSize = Size; outlineDpi = DeviceDpi;
            var diameter = Math.Min(8f * DeviceDpi / 96f, Math.Min(Width - 1, Height - 1));
            outline.AddArc(0.5f, 0.5f, diameter, diameter, 180, 90);
            outline.AddArc(Width - diameter - 0.5f, 0.5f, diameter, diameter, 270, 90);
            outline.AddArc(Width - diameter - 0.5f, Height - diameter - 0.5f, diameter, diameter, 0, 90);
            outline.AddArc(0.5f, Height - diameter - 0.5f, diameter, diameter, 90, 90);
            outline.CloseFigure();
        }
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var background = new SolidBrush(BackColor);
        using var border = new Pen(palette.Border, Math.Max(1f, DeviceDpi / 96f));
        e.Graphics.FillPath(background, outline);
        e.Graphics.DrawPath(border, outline);
        var inset = (int)(9 * DeviceDpi / 96f);
        TextRenderer.DrawText(e.Graphics, Text, Font, new Rectangle(inset, 2, Math.Max(0, Width - inset * 2), Font.Height + 3),
            ForeColor, TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) outline?.Dispose();
        base.Dispose(disposing);
    }
}

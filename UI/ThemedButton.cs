using System.Drawing.Drawing2D;
using DBConnectionTester.Models;

namespace DBConnectionTester.UI;

/// <summary>A native Button with one small paint pass; no wrapper, region, animation or shadow.</summary>
internal class ThemedButton : Button, IThemePaletteAware
{
    private ThemePalette palette = ThemeManager.PaletteFor(ApplicationTheme.Light);
    private GraphicsPath? outline;
    private Size outlineSize;
    private int outlineDpi;
    private bool hovered;
    private bool pressed;

    public ThemedButton()
    {
        FlatStyle = FlatStyle.Flat;
        UseVisualStyleBackColor = false;
        Font = UiTypography.Body;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
    }

    void IThemePaletteAware.ApplyPalette(ThemePalette value) { palette = value; Invalidate(); }

    protected override void OnMouseEnter(EventArgs e) { hovered = true; base.OnMouseEnter(e); Invalidate(); }
    protected override void OnMouseLeave(EventArgs e) { hovered = pressed = false; base.OnMouseLeave(e); Invalidate(); }
    protected override void OnMouseDown(MouseEventArgs e) { if (e.Button == MouseButtons.Left) pressed = true; base.OnMouseDown(e); Invalidate(); }
    protected override void OnMouseUp(MouseEventArgs e) { pressed = false; base.OnMouseUp(e); Invalidate(); }
    protected override void OnKeyDown(KeyEventArgs e) { if (e.KeyCode == Keys.Space) pressed = true; base.OnKeyDown(e); Invalidate(); }
    protected override void OnKeyUp(KeyEventArgs e) { pressed = false; base.OnKeyUp(e); Invalidate(); }
    protected override void OnLostFocus(EventArgs e) { pressed = false; base.OnLostFocus(e); Invalidate(); }

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
        var background = Enabled && pressed ? FlatAppearance.MouseDownBackColor
            : Enabled && hovered ? FlatAppearance.MouseOverBackColor : BackColor;
        if (background.IsEmpty) background = BackColor;
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var brush = new SolidBrush(background);
        e.Graphics.FillPath(brush, outline);
        if (FlatAppearance.BorderSize > 0)
        {
            using var border = new Pen(FlatAppearance.BorderColor, Math.Max(1f, DeviceDpi / 96f));
            e.Graphics.DrawPath(border, outline);
        }
        var textBounds = Rectangle.FromLTRB(Padding.Left + 3, Padding.Top + 2, Width - Padding.Right - 3, Height - Padding.Bottom - 2);
        var alignment = TextAlign is ContentAlignment.MiddleLeft or ContentAlignment.TopLeft or ContentAlignment.BottomLeft
            ? TextFormatFlags.Left : TextAlign is ContentAlignment.MiddleRight or ContentAlignment.TopRight or ContentAlignment.BottomRight
                ? TextFormatFlags.Right : TextFormatFlags.HorizontalCenter;
        var flags = alignment | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis;
        if (!ShowKeyboardCues) flags |= TextFormatFlags.HidePrefix;
        TextRenderer.DrawText(e.Graphics, Text, Font, textBounds, ForeColor, flags);
        if (Focused && ShowFocusCues)
            ControlPaint.DrawFocusRectangle(e.Graphics, Rectangle.Inflate(ClientRectangle, -5, -5), ForeColor, background);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) outline?.Dispose();
        base.Dispose(disposing);
    }
}

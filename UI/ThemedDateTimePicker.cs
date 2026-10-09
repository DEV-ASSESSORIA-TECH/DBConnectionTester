using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using DBConnectionTester.Models;

namespace DBConnectionTester.UI;

/// <summary>Recolors a cached native date paint in dark mode without replacing editing or calendar behavior.</summary>
internal sealed class ThemedDateTimePicker : DateTimePicker, IThemePaletteAware
{
    private const int PaintMessage = 0x000F;
    private const int EraseBackgroundMessage = 0x0014;
    private const int PrintClientMessage = 0x0318;
    private ThemePalette palette = ThemeManager.PaletteFor(ApplicationTheme.Light);
    private Bitmap? nativeImage;
    private ImageAttributes? colors;

    void IThemePaletteAware.ApplyPalette(ThemePalette value)
    {
        if (!ReferenceEquals(palette, value))
        {
            palette = value;
            colors?.Dispose();
            colors = null;
        }
        Invalidate();
    }

    protected override void WndProc(ref Message message)
    {
        if (!palette.HighContrast && palette.Window.GetBrightness() < 0.5f && message.Msg == EraseBackgroundMessage)
        {
            message.Result = (IntPtr)1;
            return;
        }
        // Native editing, field selection, checkbox, keyboard and calendar messages stay untouched.
        if (palette.HighContrast || palette.Window.GetBrightness() >= 0.5f ||
            message.Msg is not (PaintMessage or PrintClientMessage) || ClientSize.Width < 1 || ClientSize.Height < 1)
        {
            base.WndProc(ref message);
            return;
        }
        if (message.Msg == PrintClientMessage)
        {
            using var graphics = Graphics.FromHdc(message.WParam);
            DrawNativeField(graphics);
            return;
        }
        var dc = BeginPaint(Handle, out var paint);
        try
        {
            using var graphics = Graphics.FromHdc(dc);
            DrawNativeField(graphics);
        }
        finally { EndPaint(Handle, ref paint); }
    }

    private void DrawNativeField(Graphics target)
    {
        if (nativeImage is null || nativeImage.Size != ClientSize)
        {
            nativeImage?.Dispose();
            nativeImage = new Bitmap(ClientSize.Width, ClientSize.Height);
        }
        if (colors is null)
        {
            // Continuous luminance mapping also handles native antialiased text, not just solid colors.
            var red = (palette.Field.R - palette.Text.R) / 255f;
            var green = (palette.Field.G - palette.Text.G) / 255f;
            var blue = (palette.Field.B - palette.Text.B) / 255f;
            colors = new ImageAttributes();
            colors.SetColorMatrix(new ColorMatrix(new[]
            {
                new[] { .2126f * red, .2126f * green, .2126f * blue, 0, 0 },
                new[] { .7152f * red, .7152f * green, .7152f * blue, 0, 0 },
                new[] { .0722f * red, .0722f * green, .0722f * blue, 0, 0 },
                new[] { 0f, 0, 0, 1, 0 },
                new[] { palette.Text.R / 255f, palette.Text.G / 255f, palette.Text.B / 255f, 0, 1 }
            }));
        }
        using (var buffer = Graphics.FromImage(nativeImage))
        {
            buffer.Clear(SystemColors.Window);
            var dc = buffer.GetHdc();
            try
            {
                var nativePaint = Message.Create(Handle, PrintClientMessage, dc, (IntPtr)12);
                base.WndProc(ref nativePaint);
            }
            finally { buffer.ReleaseHdc(dc); }
        }
        target.DrawImage(nativeImage, ClientRectangle, 0, 0, nativeImage.Width, nativeImage.Height, GraphicsUnit.Pixel, colors);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { nativeImage?.Dispose(); colors?.Dispose(); }
        base.Dispose(disposing);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PaintData
    {
        public IntPtr Dc;
        public int Erase, Left, Top, Right, Bottom, Restore, Update;
        public int Reserved1, Reserved2, Reserved3, Reserved4, Reserved5, Reserved6, Reserved7, Reserved8;
    }

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern IntPtr BeginPaint(IntPtr window, out PaintData paint);
    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern bool EndPaint(IntPtr window, ref PaintData paint);
}

using System.Drawing;
using System.Reflection;
using System.Windows.Forms;
using DBConnectionTester.Models;
using DBConnectionTester.UI;

namespace DBConnectionTester.Tests;

[Collection("WinForms UI")]
public sealed class ModernThemeTests
{
    [Theory]
    [InlineData(ApplicationTheme.Light)]
    [InlineData(ApplicationTheme.Dark)]
    public Task ThemeHasReadableSemanticAndMetricColors(ApplicationTheme theme) => UiStyleTests.OnUiThread(() =>
    {
        var palette = ThemeManager.PaletteFor(theme);
        using var button = UiStyle.WithRole(new ThemedButton(), UiRole.PrimaryAction);
        var primary = palette.ColorsFor(button);
        Assert.True(Contrast(primary.Foreground, primary.Background) >= 4.5);
        UiStyle.SetRole(button, UiRole.DestructiveAction);
        var danger = palette.ColorsFor(button);
        Assert.True(Contrast(danger.Foreground, danger.Background) >= 4.5);
        Assert.True(Contrast(palette.Text, palette.Surface) >= 4.5);
        Assert.True(Contrast(palette.MutedText, palette.Window) >= 4.5);
        Assert.True(Contrast(palette.Text, palette.Metrics.FailureBackground) >= 4.5);
        foreach (var state in new[] { UiState.Warning, UiState.Error, UiState.Success })
        {
            using var status = UiStyle.WithRole(new Label(), UiRole.Status);
            UiStyle.SetState(status, state);
            Assert.True(Contrast(palette.ColorsFor(status).Foreground, palette.Surface) >= 4.5);
        }
        Assert.Same(palette, ThemeManager.PaletteFor(theme));
    });

    [Theory]
    [InlineData(ApplicationTheme.Light)]
    [InlineData(ApplicationTheme.Dark)]
    public Task DisabledButtonPaintsReadableTextAndReusesRoundedOutline(ApplicationTheme theme) => UiStyleTests.OnUiThread(() =>
    {
        using var form = new Form();
        using var button = new ThemedButton { Text = "Exportar CSV", Size = new Size(150, 36) };
        form.Controls.Add(button);
        var palette = ThemeManager.PaletteFor(theme);
        ThemeManager.ApplyPalette(form, palette);
        button.Enabled = false;
        using var bitmap = new Bitmap(button.Width, button.Height);
        button.DrawToBitmap(bitmap, button.ClientRectangle);
        var readablePixels = 0;
        for (var y = 4; y < bitmap.Height - 4; y++)
            for (var x = 4; x < bitmap.Width - 4; x++)
                if (bitmap.GetPixel(x, y).ToArgb() == palette.DisabledText.ToArgb()) readablePixels++;
        Assert.True(readablePixels > 5, $"Disabled text was not rendered in its palette color: {readablePixels}");
        var cache = typeof(ThemedButton).GetField("outline", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var outline = cache.GetValue(button);
        for (var index = 0; index < 5; index++) button.DrawToBitmap(bitmap, button.ClientRectangle);
        Assert.Same(outline, cache.GetValue(button));
        var clicks = 0;
        button.Click += (_, _) => clicks++;
        button.PerformClick();
        Assert.Equal(0, clicks);
        button.Enabled = true;
        form.Show();
        button.PerformClick();
        Assert.Equal(1, clicks);
        Assert.Equal(AccessibleRole.PushButton, button.AccessibilityObject.Role);
        form.Close();
    });

    [Fact]
    public Task ReapplyingThemeKeepsCachedTrendToneWithoutLoadingData() => UiStyleTests.OnUiThread(() =>
    {
        using var form = new Form();
        using var dashboard = new StatisticsDashboardControl();
        form.Controls.Add(dashboard);
        form.Show();
        var label = (Label)typeof(StatisticsDashboardControl).GetField("trendLabel", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(dashboard)!;
        foreach (var theme in new[] { ApplicationTheme.Light, ApplicationTheme.Dark })
        {
            var palette = ThemeManager.PaletteFor(theme);
            ThemeManager.ApplyPalette(form, palette);
            ThemeManager.ApplyPalette(form, palette);
            Assert.Equal(palette.Metrics.Muted, label.ForeColor);
            Assert.Equal("Aguardando dados", label.Text);
        }
        form.Close();
    });

    [Fact]
    public Task CardsKeepNativeChildrenAndReuseOutlineAcrossPaints() => UiStyleTests.OnUiThread(() =>
    {
        using var form = new Form { ClientSize = new Size(300, 160) };
        using var card = new ThemedGroupBox { Text = "Conexão", Size = new Size(280, 130) };
        using var field = new TextBox { Bounds = new Rectangle(15, 35, 200, 23) };
        card.Controls.Add(field);
        form.Controls.Add(card);
        form.Show();
        var palette = ThemeManager.PaletteFor(ApplicationTheme.Dark);
        ThemeManager.ApplyPalette(form, palette);
        var bounds = field.Bounds;
        using var image = new Bitmap(card.Width, card.Height);
        card.DrawToBitmap(image, card.ClientRectangle);
        Assert.Equal(palette.Surface.ToArgb(), image.GetPixel(card.Width / 2, card.Height - 8).ToArgb());
        var cache = typeof(ThemedGroupBox).GetField("outline", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var outline = cache.GetValue(card);
        for (var index = 0; index < 5; index++) card.DrawToBitmap(image, card.ClientRectangle);
        Assert.Same(outline, cache.GetValue(card));
        Assert.Equal(bounds, field.Bounds);
        Assert.Single(card.Controls.Cast<Control>());
        form.Close();
    });

    [Fact]
    public Task TabHeadersFollowThemeAndKeepNativePageSelection() => UiStyleTests.OnUiThread(() =>
    {
        using var form = new Form { ClientSize = new Size(500, 240) };
        using var tabs = new ThemedTabControl { Dock = DockStyle.Fill };
        tabs.TabPages.Add(new TabPage("Resumo"));
        tabs.TabPages.Add(new TabPage("Ciclos"));
        form.Controls.Add(tabs);
        form.Show();
        var count = tabs.Controls.Count;
        foreach (var theme in new[] { ApplicationTheme.Light, ApplicationTheme.Dark, ApplicationTheme.Light })
        {
            var palette = ThemeManager.PaletteFor(theme);
            ThemeManager.ApplyPalette(form, palette);
            tabs.SelectTab(1);
            Assert.True(tabs.TabPages[1].Visible);
            Assert.False(tabs.TabPages[0].Visible);
            Assert.Equal(count, tabs.Controls.Count);
            using var image = new Bitmap(tabs.Width, tabs.Height);
            tabs.DrawToBitmap(image, tabs.ClientRectangle);
            var selected = tabs.GetTabRect(1);
            Assert.Equal(palette.Selection.ToArgb(), image.GetPixel(selected.Left + 2, selected.Top + 2).ToArgb());
        }
        form.Close();
    });

    [Theory]
    [InlineData(ApplicationTheme.Light)]
    [InlineData(ApplicationTheme.Dark)]
    public Task RoundedButtonsRepaintEveryPixelAcrossStatesAndResizes(ApplicationTheme theme) => UiStyleTests.OnUiThread(() =>
    {
        using var form = new Form { ClientSize = new Size(400, 200) };
        using var button = new PaintProbeButton { Text = "Abrir histórico" };
        form.Controls.Add(button);
        ThemeManager.ApplyPalette(form, ThemeManager.PaletteFor(theme));
        form.Show();
        foreach (var role in new[] { UiRole.NeutralAction, UiRole.Navigation, UiRole.PrimaryAction })
        {
            UiStyle.SetRole(button, role);
            foreach (var state in new[] { "normal", "hover", "pressed", "disabled" })
            foreach (var size in new[] { new Size(150, 36), new Size(117, 42), new Size(225, 54) })
            {
                button.Size = size;
                button.SetInteraction(state);
                using var first = PaintOver(Color.Magenta);
                using var second = PaintOver(Color.Lime);
                for (var y = 0; y < size.Height; y++)
                for (var x = 0; x < size.Width; x++)
                    Assert.True(first.GetPixel(x, y) == second.GetPixel(x, y),
                        $"Unpainted pixel at {x},{y}: {theme}, {role}, {state}, {size}");
            }
        }
        form.Close();

        Bitmap PaintOver(Color initialColor)
        {
            var image = new Bitmap(button.Width, button.Height);
            using (var graphics = Graphics.FromImage(image)) graphics.Clear(initialColor);
            button.DrawToBitmap(image, button.ClientRectangle);
            return image;
        }
    });

    private sealed class PaintProbeButton : ThemedButton
    {
        public void SetInteraction(string state)
        {
            OnMouseLeave(EventArgs.Empty);
            Enabled = true;
            if (state is "hover" or "pressed") OnMouseEnter(EventArgs.Empty);
            if (state == "pressed") OnMouseDown(new MouseEventArgs(MouseButtons.Left, 1, 10, 10, 0));
            if (state == "disabled") Enabled = false;
        }
    }

    private static double Contrast(Color a, Color b)
    {
        static double Channel(byte value) => value / 255d <= 0.04045 ? value / 255d / 12.92 : Math.Pow((value / 255d + 0.055) / 1.055, 2.4);
        static double Luminance(Color c) => 0.2126 * Channel(c.R) + 0.7152 * Channel(c.G) + 0.0722 * Channel(c.B);
        var first = Luminance(a); var second = Luminance(b);
        return (Math.Max(first, second) + 0.05) / (Math.Min(first, second) + 0.05);
    }
}

using System.Drawing;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using DBConnectionTester.Models;
using DBConnectionTester.UI;

namespace DBConnectionTester.Tests;

[Collection("WinForms UI")]
public sealed class NativeThemeTests
{
    [Theory]
    [InlineData(ApplicationTheme.Light)]
    [InlineData(ApplicationTheme.Dark)]
    public Task DisabledPackageCheckboxHasReadableTextWithoutChangingItsState(ApplicationTheme theme) => UiStyleTests.OnUiThread(() =>
    {
        using var form = new Form();
        using var checkbox = new ThemedCheckBox { Text = "Incluir aplicativo", Size = new Size(170, 28), Enabled = false };
        form.Controls.Add(checkbox);
        var palette = ThemeManager.PaletteFor(theme);
        ThemeManager.ApplyPalette(form, palette);
        form.Show();
        foreach (var selected in new[] { false, true })
        {
            checkbox.Checked = selected;
            using var image = new Bitmap(checkbox.Width, checkbox.Height);
            checkbox.DrawToBitmap(image, checkbox.ClientRectangle);
            Assert.True(CountColor(image, palette.DisabledText, 20) > 5);
            Assert.False(checkbox.Enabled);
            Assert.Equal(selected, checkbox.Checked);
        }
        Assert.Empty(checkbox.Controls.Cast<Control>());
        form.Close();
    });

    [Fact]
    public Task DateFieldFollowsThemeAndReusesItsNativePaintBuffer() => UiStyleTests.OnUiThread(() =>
    {
        using var form = new Form();
        using var date = new ThemedDateTimePicker { Format = DateTimePickerFormat.Short, ShowCheckBox = true, Checked = false, Width = 200 };
        form.Controls.Add(date);
        form.Show();
        var cache = typeof(ThemedDateTimePicker).GetField("nativeImage", BindingFlags.NonPublic | BindingFlags.Instance)!;
        foreach (var theme in new[] { ApplicationTheme.Dark, ApplicationTheme.Light, ApplicationTheme.Dark })
        {
            var palette = ThemeManager.PaletteFor(theme);
            ThemeManager.Apply(form, theme);
            using var image = new Bitmap(date.Width, date.Height);
            date.DrawToBitmap(image, date.ClientRectangle);
            Assert.True(CountColor(image, theme == ApplicationTheme.Dark ? palette.Field : SystemColors.Window, 0) > 100);
            var buffer = cache.GetValue(date);
            for (var index = 0; index < 5; index++) date.DrawToBitmap(image, date.ClientRectangle);
            Assert.Same(buffer, cache.GetValue(date));
            Assert.False(date.Checked);
            date.Width += 15;
        }
        Assert.Empty(date.Controls.Cast<Control>());
        form.Close();
    });

    [Fact]
    public Task DarkDateKeepsNativeKeyboardAndDateEvents() => UiStyleTests.OnUiThread(() =>
    {
        using var form = new Form();
        using var date = new ThemedDateTimePicker { Format = DateTimePickerFormat.Short, ShowCheckBox = true, Checked = true, Width = 200 };
        form.Controls.Add(date);
        ThemeManager.ApplyPalette(form, ThemeManager.PaletteFor(ApplicationTheme.Dark));
        form.Show();
        date.Value = new DateTime(2026, 1, 15);
        var changes = 0;
        date.ValueChanged += (_, _) => changes++;
        date.Focus();
        var original = date.Value;
        // The native optional-date checkbox receives focus first; move into the date fields.
        SendMessage(date.Handle, 0x0100, (IntPtr)Keys.Right, IntPtr.Zero);
        SendMessage(date.Handle, 0x0100, (IntPtr)Keys.Up, IntPtr.Zero);
        Assert.NotEqual(original, date.Value);
        Assert.True(changes > 0);
        Assert.True(date.Checked);
        form.Close();
    });

    private static int CountColor(Bitmap image, Color color, int startX)
    {
        var count = 0;
        for (var y = 0; y < image.Height; y++)
        for (var x = startX; x < image.Width; x++)
            if (image.GetPixel(x, y).ToArgb() == color.ToArgb()) count++;
        return count;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);
}

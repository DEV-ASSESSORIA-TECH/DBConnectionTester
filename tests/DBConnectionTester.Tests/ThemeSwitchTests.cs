using System.Drawing;
using System.Reflection;
using System.Windows.Forms;
using DBConnectionTester.Models;
using DBConnectionTester.Services.Storage;
using DBConnectionTester.UI;

namespace DBConnectionTester.Tests;

[Collection("WinForms UI")]
public sealed class ThemeSwitchTests
{
    [Theory]
    [InlineData(ApplicationTheme.Light)]
    [InlineData(ApplicationTheme.Dark)]
    public Task LiveThemeChangesPreserveHandlesValuesFocusAndScroll(ApplicationTheme initial) => UiStyleTests.OnUiThread(() =>
    {
        System.Windows.Forms.Application.EnableVisualStyles();
        ThemeManager.ConfigureNativeMode(initial);
        using var form = new Form { ClientSize = new Size(420, 300) };
        using var viewport = new Panel { Dock = DockStyle.Fill, AutoScroll = true, AutoScrollMinSize = new Size(700, 900) };
        using var combo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 220, Location = new Point(35, 75) };
        combo.Items.AddRange(new object[] { "Primeiro", "Segundo" });
        combo.SelectedIndex = 1;
        using var check = new CheckBox { Text = "Etapa", Size = new Size(120, 24), Location = new Point(35, 105) };
        using var text = new TextBox { Text = "Edição não salva", Width = 220, Location = new Point(35, 140) };
        using var amount = new NumericUpDown { Value = 37, Location = new Point(35, 175) };
        using var grid = new DataGridView { Location = new Point(35, 205), Size = new Size(300, 200), AllowUserToAddRows = false };
        grid.Columns.Add("Value", "Valor");
        grid.Rows.Add("Preservar resultado");
        viewport.Controls.AddRange(new Control[] { combo, check, text, amount, grid });
        form.Controls.Add(viewport);
        ThemeManager.Apply(form, initial);
        form.Show();
        text.Focus();
        text.Select(2, 5);
        viewport.AutoScrollPosition = new Point(15, 30);
        var scroll = viewport.AutoScrollPosition;
        var controls = new Control[] { form, viewport, combo, check, text, amount, grid };
        var handles = controls.Select(control => control.Handle).ToArray();
        var selected = 0;
        combo.SelectedIndexChanged += (_, _) => selected++;
        var row = grid.Rows[0];
        foreach (var theme in new[] { ApplicationTheme.Dark, ApplicationTheme.Light, ApplicationTheme.Dark, ApplicationTheme.Light })
        {
            ThemeManager.Apply(form, theme);
            using var image = new Bitmap(combo.Width, combo.Height);
            combo.DrawToBitmap(image, combo.ClientRectangle);
            var brightness = image.GetPixel(combo.Width - 40, combo.Height / 2).GetBrightness();
            Assert.True(theme == ApplicationTheme.Dark ? brightness < .5 : brightness > .8);
            using var glyph = new Bitmap(check.Width, check.Height);
            check.DrawToBitmap(glyph, check.ClientRectangle);
            var glyphBrightness = glyph.GetPixel(6, check.Height / 2).GetBrightness();
            Assert.True(theme == ApplicationTheme.Dark ? glyphBrightness < .5 : glyphBrightness > .8);
            Assert.Equal(handles, controls.Select(control => control.Handle).ToArray());
            Assert.Equal(1, combo.SelectedIndex);
            Assert.Equal(0, selected);
            Assert.Equal("Edição não salva", text.Text);
            Assert.Equal(2, text.SelectionStart);
            Assert.Equal(5, text.SelectionLength);
            Assert.True(text.Focused);
            Assert.Equal(scroll, viewport.AutoScrollPosition);
            Assert.Equal(37, amount.Value);
            Assert.Same(row, grid.Rows[0]);
            Assert.Equal("Preservar resultado", row.Cells[0].Value);
            Assert.False(check.Checked);
        }
        form.Close();
    });

    [Fact]
    public Task ReapplyingSameModeDoesNotRefreshNativeThemeAndHiddenControlsStayLazy() => UiStyleTests.OnUiThread(() =>
    {
        using var form = new Form();
        using var combo = new ThemeProbeCombo();
        using var hidden = new ComboBox { Visible = false };
        form.Controls.AddRange(new Control[] { combo, hidden });
        ThemeManager.Apply(form, ApplicationTheme.Dark);
        form.Show();
        Assert.False(hidden.IsHandleCreated);
        combo.ThemeMessages = 0;
        var handle = combo.Handle;
        for (var index = 0; index < 4; index++) ThemeManager.Apply(form, ApplicationTheme.Dark);
        Assert.Equal(0, combo.ThemeMessages);
        Assert.Equal(handle, combo.Handle);
        Assert.False(hidden.IsHandleCreated);
        hidden.Visible = true;
        using var image = new Bitmap(hidden.Width, hidden.Height);
        hidden.DrawToBitmap(image, hidden.ClientRectangle);
        Assert.True(image.GetPixel(hidden.Width - 40, hidden.Height / 2).GetBrightness() < .5);
        form.Close();
    });

    [Fact]
    public Task ThemeOnlySettingsKeepCoordinatorPagesAndCachedResults() => UiStyleTests.OnUiThread(() =>
    {
        var directory = Path.Combine(Path.GetTempPath(), "DBCT-theme-switch-test", Guid.NewGuid().ToString("N"));
        var store = SqliteApplicationStore.OpenOrCreateAsync(Path.Combine(directory, "data.db"), StorageScope.Custom).GetAwaiter().GetResult();
        using var form = new MainForm(store, ApplicationSettings.Default with { Theme = ApplicationTheme.Light });
        var coordinator = Field<object>(form, "runCoordinator");
        var history = Field<HistoryPage>(form, "historyPage");
        var profiles = Field<ProfilesPage>(form, "profilesPage");
        var results = Field<Control>(form, "resultsControl");
        var apply = typeof(MainForm).GetMethod("ApplyApplicationSettings", BindingFlags.Instance | BindingFlags.NonPublic)!;
        foreach (var theme in new[] { ApplicationTheme.Dark, ApplicationTheme.Light, ApplicationTheme.Dark })
        {
            apply.Invoke(form, new object[] { ApplicationSettings.Default with { Theme = theme } });
            Assert.Same(coordinator, Field<object>(form, "runCoordinator"));
            Assert.Same(history, Field<HistoryPage>(form, "historyPage"));
            Assert.Same(profiles, Field<ProfilesPage>(form, "profilesPage"));
            Assert.Same(results, Field<Control>(form, "resultsControl"));
            Assert.False(history.IsHandleCreated);
            Assert.False(profiles.IsHandleCreated);
        }
    });

    private static T Field<T>(object owner, string name) => (T)owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(owner)!;

    private sealed class ThemeProbeCombo : ComboBox
    {
        public int ThemeMessages;
        protected override void WndProc(ref Message message)
        {
            if (message.Msg == 0x031A) ThemeMessages++;
            base.WndProc(ref message);
        }
    }
}

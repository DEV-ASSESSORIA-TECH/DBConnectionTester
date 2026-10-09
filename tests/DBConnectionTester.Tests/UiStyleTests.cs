using System.Drawing;
using System.Windows.Forms;
using DBConnectionTester.UI;

namespace DBConnectionTester.Tests;

[Collection("WinForms UI")]
public sealed class UiStyleTests
{
    [Fact]
    public Task RolesAreMetadataAndPreserveLayoutAppearanceAndTag() => OnUiThread(() =>
    {
        using var button = new Button { Text = "Salvar", Tag = new object(), Bounds = new Rectangle(10, 20, 110, 30) };
        var tag = button.Tag;
        var bounds = button.Bounds;
        var font = button.Font;
        var background = button.BackColor;
        var foreground = button.ForeColor;
        var layouts = 0;
        button.Layout += (_, _) => layouts++;
        UiStyle.SetRole(button, UiRole.PrimaryAction);
        UiStyle.SetState(button, UiState.Busy);
        Assert.Equal(UiRole.PrimaryAction, UiStyle.GetRole(button));
        Assert.Equal(UiState.Busy, UiStyle.GetState(button));
        Assert.Same(tag, button.Tag);
        Assert.Same(font, button.Font);
        Assert.Equal(bounds, button.Bounds);
        Assert.Equal(background, button.BackColor);
        Assert.Equal(foreground, button.ForeColor);
        Assert.Empty(button.Controls.Cast<Control>());
        Assert.Equal(0, layouts);
    });

    [Fact]
    public Task DefaultRolesDoNotDependOnTextAndStatePreservesRole() => OnUiThread(() =>
    {
        using var button = new Button { Text = "Excluir" };
        using var group = new GroupBox();
        using var field = new TextBox();
        Assert.Equal(UiRole.NeutralAction, UiStyle.GetRole(button));
        Assert.Equal(UiRole.Card, UiStyle.GetRole(group));
        Assert.Equal(UiRole.Input, UiStyle.GetRole(field));
        UiStyle.SetState(button, UiState.Selected);
        Assert.Equal(UiRole.NeutralAction, UiStyle.GetRole(button));
        UiStyle.SetRole(button, UiRole.Navigation);
        Assert.Equal(UiState.Selected, UiStyle.GetState(button));
    });

    internal static Task OnUiThread(Action action)
    {
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try { action(); completed.SetResult(); }
            catch (Exception exception) { completed.SetException(exception); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completed.Task;
    }
}

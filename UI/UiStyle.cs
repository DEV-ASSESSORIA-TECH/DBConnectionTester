using System.Runtime.CompilerServices;

namespace DBConnectionTester.UI;

public enum UiRole
{
    Container, Card, Input, Text, Heading, SecondaryText,
    NeutralAction, PrimaryAction, DestructiveAction, Navigation,
    Status, Warning, Error, Success
}

public enum UiState { Normal, Selected, Busy, Warning, Error, Success }

/// <summary>Semantic metadata only: no controls, layout, event subscriptions or Tag usage.</summary>
public static class UiStyle
{
    private sealed class Metadata
    {
        public UiRole Role;
        public UiState State;
    }

    private static readonly ConditionalWeakTable<Control, Metadata> metadata = new();

    public static T WithRole<T>(T control, UiRole role) where T : Control
    {
        SetRole(control, role);
        return control;
    }

    public static void SetRole(Control control, UiRole role) => metadata.GetOrCreateValue(control).Role = role;
    public static void SetState(Control control, UiState state)
    {
        if (!metadata.TryGetValue(control, out var entry))
        {
            entry = metadata.GetOrCreateValue(control);
            entry.Role = DefaultRole(control);
        }
        entry.State = state;
    }
    public static UiRole GetRole(Control control) => metadata.TryGetValue(control, out var entry) ? entry.Role : DefaultRole(control);
    public static UiState GetState(Control control) => metadata.TryGetValue(control, out var entry) ? entry.State : UiState.Normal;

    private static UiRole DefaultRole(Control control) => control switch
    {
        Button => UiRole.NeutralAction,
        GroupBox => UiRole.Card,
        TextBoxBase or ComboBox or NumericUpDown or ListBox or DataGridView => UiRole.Input,
        Label => UiRole.Text,
        _ => UiRole.Container
    };
}

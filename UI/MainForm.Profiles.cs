using DBConnectionTester.Models;

namespace DBConnectionTester.UI;

public sealed partial class MainForm
{
    private readonly ComboBox executionProfile = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 250, AccessibleName = "Perfil da execução" };
    private readonly Button manageProfiles = new ThemedButton() { Text = "Gerenciar perfis", AutoSize = true };
    private readonly Label executionProfileState = new() { Text = "Configuração modificada", AutoSize = true, Visible = false, Margin = new Padding(8, 6, 3, 0) };
    private bool syncingExecutionProfile;
    private bool applyingExecutionProfile;
    private SavedConnectionProfile? appliedProfileVersion;
    private ConnectionProfileDraft? appliedProfileSnapshot;

    private Control BuildProfileArea()
    {
        var area = new BufferedPanel { Name = "ExecutionProfileArea", Dock = DockStyle.Top };
        var flow = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true, Margin = Padding.Empty };
        flow.Controls.Add(new Label { Text = "Perfil:", AutoSize = true, Margin = new Padding(3, 6, 6, 0) });
        flow.Controls.Add(executionProfile);
        flow.Controls.Add(manageProfiles);
        flow.Controls.Add(executionProfileState);
        area.Controls.Add(flow);
        executionProfile.Items.Add(new ExecutionProfileChoice(null));
        executionProfile.SelectedIndex = 0;
        executionProfile.SelectedIndexChanged += (_, _) =>
        {
            if (syncingExecutionProfile || !configurationEnabled) return;
            if (executionProfile.SelectedItem is ExecutionProfileChoice { Profile: { } profile }) ApplyProfile(profile);
            else
            {
                selectedProfileId = null;
                appliedProfileVersion = null;
                appliedProfileSnapshot = null;
                UpdateExecutionProfileState();
            }
        };
        manageProfiles.Click += (_, _) =>
        {
            if (!configurationEnabled) return;
            profilesPage.SelectProfile(selectedProfileId);
            ShowPage("Perfis", profilesPage);
        };
        void FitHeight()
        {
            var height = flow.GetPreferredSize(new Size(Math.Max(1, area.ClientSize.Width), 0)).Height;
            if (area.Height != height) area.Height = height;
        }
        area.SizeChanged += (_, _) => FitHeight();
        flow.FontChanged += (_, _) => FitHeight();
        manageProfiles.SizeChanged += (_, _) => FitHeight();
        executionProfileState.VisibleChanged += (_, _) => FitHeight();
        area.DpiChangedAfterParent += (_, _) => FitHeight();
        FitHeight();
        return area;
    }

    private void SetExecutionProfiles(IReadOnlyList<SavedConnectionProfile> items)
    {
        syncingExecutionProfile = true;
        executionProfile.BeginUpdate();
        try
        {
            executionProfile.Items.Clear();
            executionProfile.Items.Add(new ExecutionProfileChoice(null));
            foreach (var profile in items) executionProfile.Items.Add(new ExecutionProfileChoice(profile));
            SyncExecutionProfileSelection();
            if (executionProfile.SelectedIndex == 0)
            {
                selectedProfileId = null;
                appliedProfileVersion = null;
                appliedProfileSnapshot = null;
            }
        }
        finally { executionProfile.EndUpdate(); syncingExecutionProfile = false; }
        UpdateExecutionProfileState();
    }

    private void SyncExecutionProfileSelection(bool addAppliedIfMissing = false)
    {
        var previous = syncingExecutionProfile;
        syncingExecutionProfile = true;
        try
        {
            var choice = executionProfile.Items.Cast<ExecutionProfileChoice>().FirstOrDefault(item => item.Profile?.ProfileId == selectedProfileId);
            if (choice is null && addAppliedIfMissing && appliedProfileVersion is { } applied)
            {
                choice = new ExecutionProfileChoice(applied);
                executionProfile.Items.Add(choice);
            }
            executionProfile.SelectedItem = choice ?? executionProfile.Items[0];
        }
        finally { syncingExecutionProfile = previous; }
    }

    private ConnectionProfileDraft ExecutionProfileSnapshot() => new(selectedProfileId, "", SelectedProfile.Type, txtHost.Text,
        SelectedProfile.UsesNetwork ? (int)numPort.Value : null, txtUser.Text, txtDatabase.Text, txtSqliteFile.Text,
        SelectedSqlServerAuthentication, txtOdbcDriver.Text,
        new((long)numTests.Value, chkContinuous.Checked, (double)numInterval.Value, (int)numTimeout.Value,
            chkDns.Checked, chkPing.Checked, chkTcp.Checked, chkDatabase.Checked, chkBackground.Checked));

    private void UpdateExecutionProfileState()
    {
        var current = (executionProfile.SelectedItem as ExecutionProfileChoice)?.Profile;
        executionProfileState.Visible = selectedProfileId is not null && appliedProfileSnapshot is not null
            && (ExecutionProfileSnapshot() != appliedProfileSnapshot || current != appliedProfileVersion);
    }

    private void TrackExecutionProfileChanges()
    {
        foreach (var control in new Control[] { txtHost, txtUser, txtDatabase, txtSqliteFile, txtOdbcDriver, cmbDatabaseType, cmbSqlServerAuth,
            numPort, numTests, numInterval, numTimeout, chkContinuous, chkDns, chkPing, chkTcp, chkDatabase, chkBackground })
        {
            void Changed(object? sender, EventArgs args) { if (!applyingExecutionProfile) UpdateExecutionProfileState(); }
            if (control is TextBox text) text.TextChanged += Changed;
            else if (control is ComboBox combo) combo.SelectedIndexChanged += Changed;
            else if (control is NumericUpDown number) number.ValueChanged += Changed;
            else if (control is CheckBox check) check.CheckedChanged += Changed;
        }
    }

    private sealed record ExecutionProfileChoice(SavedConnectionProfile? Profile)
    {
        public override string ToString() => Profile?.Name ?? "Configuração manual";
    }
}

using DBConnectionTester.Models;
using DBConnectionTester.Services.Storage;

namespace DBConnectionTester.UI;

public sealed class HomePage : UserControl
{
    private readonly IRecentRunHistoryRepository history;
    private readonly LatestUiRequest recentRequests = new();
    private readonly Label statusValue = new() { AutoSize = true, Dock = DockStyle.Top, Text = "Nenhum teste em andamento." };
    private readonly Label statusDetail = new() { AutoSize = true, Dock = DockStyle.Top, Text = "O andamento da execução aparece aqui durante o teste." };
    private readonly Label recentFeedback = new() { AutoSize = true, Dock = DockStyle.Top, Text = "Carregando últimas execuções…" };
    private readonly DataGridView recentRuns = new();
    private readonly Button retry = new ThemedButton() { Text = "Tentar novamente", AutoSize = true, Visible = false };

    public HomePage(IRecentRunHistoryRepository history)
    {
        Font = UiTypography.Body;
        UiStyle.SetRole(statusValue, UiRole.Status);
        UiStyle.SetRole(statusDetail, UiRole.SecondaryText);
        UiStyle.SetRole(recentFeedback, UiRole.Status);

        this.history = history;
        Dock = DockStyle.Fill;
        AutoScroll = true;
        Padding = new Padding(24);
        var content = VerticalTable();
        content.Controls.Add(UiStyle.WithRole(new Label
        {
            Text = "Início", AutoSize = true, Dock = DockStyle.Top,
            Font = new Font(Font.FontFamily, 22, FontStyle.Bold), Margin = new Padding(3, 0, 3, 6)
        }, UiRole.Heading));
        content.Controls.Add(UiStyle.WithRole(new Label
        {
            Text = "Inicie um teste ou consulte suas últimas execuções.", AutoSize = true,
            Dock = DockStyle.Top, Margin = new Padding(3, 0, 3, 18)
        }, UiRole.SecondaryText));
        var start = ActionButton("Nova execução");
        UiStyle.SetRole(start, UiRole.PrimaryAction);
        start.Font = new Font(Font, FontStyle.Bold);
        start.Click += (_, _) => NewRunRequested?.Invoke(this, EventArgs.Empty);
        var profiles = ActionButton("Usar um perfil");
        profiles.Click += (_, _) => ProfilesRequested?.Invoke(this, EventArgs.Empty);
        var openHistory = ActionButton("Abrir histórico");
        openHistory.Click += (_, _) => HistoryRequested?.Invoke(this, EventArgs.Empty);
        var actions = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Top, WrapContents = true, Margin = new Padding(0, 0, 0, 16) };
        actions.Controls.AddRange(new Control[] { start, profiles, openHistory });
        content.Controls.Add(actions);

        var state = VerticalTable();
        statusValue.Font = new Font(Font, FontStyle.Bold);
        statusValue.Margin = new Padding(3, 6, 3, 6);
        state.Controls.Add(statusValue);
        state.Controls.Add(statusDetail);
        content.Controls.Add(Group("Execução atual", state));

        ConfigureRecentGrid();
        var recent = VerticalTable();
        recent.Controls.Add(recentFeedback);
        recent.Controls.Add(recentRuns);
        var allHistory = ActionButton("Ver histórico completo");
        allHistory.Click += (_, _) => HistoryRequested?.Invoke(this, EventArgs.Empty);
        retry.Click += async (_, _) => await RefreshAsync();
        var recentActions = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Top, FlowDirection = FlowDirection.RightToLeft, WrapContents = true };
        recentActions.Controls.AddRange(new Control[] { allHistory, retry });
        recent.Controls.Add(recentActions);
        content.Controls.Add(Group("Últimas execuções", recent));
        Controls.Add(content);
    }

    public event EventHandler? NewRunRequested;
    public event EventHandler? ProfilesRequested;
    public event EventHandler? HistoryRequested;

    public void UpdateRunStatus(string status) => statusValue.Text = status;
    public void UpdateRunProgress(string progress) => statusDetail.Text = progress;

    public async Task RefreshAsync(CancellationToken token = default)
    {
        if (IsDisposed) return;
        using var request = recentRequests.Start(token);
        UiStyle.SetState(recentFeedback, UiState.Busy);
        recentFeedback.Text = "Carregando últimas execuções…";
        recentFeedback.Visible = true;
        retry.Visible = false;
        try
        {
            var items = await Task.Run(() => history.GetRecentAsync(request.Token), request.Token);
            if (!request.IsCurrent || IsDisposed) return;
            recentRuns.Rows.Clear();
            foreach (var item in items.Take(3))
                recentRuns.Rows.Add(item.StartedAt.ToLocalTime().ToString("dd/MM/yyyy HH:mm"),
                    item.ProfileName ?? "Configuração manual", item.Target, HistoryPage.StatusText(item.Status));
            recentRuns.ClearSelection();
            recentRuns.CurrentCell = null;
            recentRuns.Visible = items.Count > 0;
            UiStyle.SetState(recentFeedback, UiState.Normal);
            recentFeedback.Text = "Nenhuma execução no histórico. Comece em Nova execução ou escolha um perfil.";
            recentFeedback.Visible = items.Count == 0;
        }
        catch (OperationCanceledException) when (request.Token.IsCancellationRequested) { }
        catch (Exception error) when (error is ApplicationStoreException or Microsoft.Data.Sqlite.SqliteException or IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            if (!request.IsCurrent || IsDisposed) return;
            UiStyle.SetState(recentFeedback, UiState.Error);
            recentFeedback.Text = recentRuns.Rows.Count > 0
                ? "Não foi possível atualizar as últimas execuções. Os registros exibidos podem estar desatualizados."
                : "Não foi possível carregar as últimas execuções. Tente novamente ou consulte o Histórico.";
            retry.Visible = true;
        }
    }

    private void ConfigureRecentGrid()
    {
        recentRuns.AccessibleName = "Últimas três execuções";
        recentRuns.Dock = DockStyle.Top;
        recentRuns.Visible = false;
        recentRuns.ReadOnly = true;
        recentRuns.AllowUserToAddRows = recentRuns.AllowUserToDeleteRows = false;
        recentRuns.AllowUserToResizeRows = false;
        recentRuns.MultiSelect = false;
        recentRuns.RowHeadersVisible = false;
        recentRuns.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        recentRuns.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        recentRuns.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
        foreach (var (name, caption, weight, minimum) in new[]
        { ("Started", "Início", 23f, 125), ("Profile", "Perfil", 25f, 110), ("Target", "Destino", 34f, 140), ("State", "Estado", 18f, 90) })
            recentRuns.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = name, HeaderText = caption, FillWeight = weight, MinimumWidth = minimum,
                SortMode = DataGridViewColumnSortMode.NotSortable
            });
        void FitGrid()
        {
            var rowHeight = recentRuns.Font.Height + 10 * recentRuns.DeviceDpi / 96;
            recentRuns.RowTemplate.Height = rowHeight;
            foreach (DataGridViewRow row in recentRuns.Rows) row.Height = rowHeight;
            recentRuns.ColumnHeadersHeight = rowHeight + 2;
            recentRuns.Height = recentRuns.ColumnHeadersHeight + 3 * rowHeight + 4 * recentRuns.DeviceDpi / 96;
        }
        recentRuns.FontChanged += (_, _) => FitGrid();
        recentRuns.DpiChangedAfterParent += (_, _) => FitGrid();
        FitGrid();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) recentRequests.Dispose();
        base.Dispose(disposing);
    }

    private static Button ActionButton(string text) => new ThemedButton() { Text = text, AutoSize = true, Padding = new Padding(8, 4, 8, 4) };
    private static TableLayoutPanel VerticalTable()
    {
        var table = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 1 };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        return table;
    }
    private static GroupBox Group(string title, Control content)
    {
        var group = new ThemedGroupBox
        {
            Text = title, Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(10, 8, 10, 14), Margin = new Padding(3, 0, 3, 16)
        };
        group.Controls.Add(content);
        return group;
    }
}

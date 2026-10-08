using DBConnectionTester.Models;
using DBConnectionTester.Services.Storage;

namespace DBConnectionTester.UI;

public sealed partial class MainForm
{
    private void BuildUi()
    {
        cmbDatabaseType.DataSource = DatabaseProfiles.All.ToList();
        cmbDatabaseType.DisplayMember = nameof(DatabaseProfile.DisplayName);
        cmbSqlServerAuth.DataSource = Enum.GetValues<SqlServerAuthentication>();
        cmbSqlServerAuth.FormattingEnabled = true;
        cmbSqlServerAuth.Format += (_, e) =>
            e.Value = (SqlServerAuthentication?)e.ListItem == SqlServerAuthentication.Windows
                ? "Autenticação do Windows"
                : "Usuário e senha do SQL Server";

        homePage = new HomePage(new RunHistoryRepository(applicationStore));
        executionPage = BuildExecutionPage();
        var repository = new PersistentSettingsRepository(applicationStore);
        historyPage = new HistoryPage(new RunHistoryRepository(applicationStore), repository);
        profilesPage = new ProfilesPage(repository);
        settingsPage = new SettingsPage(applicationStore, applicationSettings, repository, new RegistryStoragePreferenceStore());
        homePage.NewRunRequested += (_, _) => ShowPage("Nova execução", executionPage);
        homePage.HistoryRequested += (_, _) => ShowPage("Histórico", historyPage);
        homePage.ProfilesRequested += (_, _) => ShowPage("Perfis", profilesPage);
        profilesPage.UseRequested += ApplyProfile;
        profilesPage.ProfilesChanged += SetExecutionProfiles;
        settingsPage.SettingsSaved += ApplyApplicationSettings;
        settingsPage.BusyChanged += busy =>
        {
            var canStart = !busy && runUiState is not (RunUiState.Running or RunUiState.Stopping);
            btnStart.Enabled = btnTestOnce.Enabled = canStart;
        };
        settingsPage.StorageSelected += descriptor =>
        {
            var current = descriptor.StoreId == applicationStore.Descriptor.StoreId &&
                string.Equals(descriptor.DatabasePath, applicationStore.Descriptor.DatabasePath, StringComparison.OrdinalIgnoreCase);
            UiStyle.SetState(globalStatus, current ? UiState.Normal : UiState.Warning);
            globalStatus.Text = current ? $"Banco atual: {SettingsPage.ScopeName(applicationStore.Descriptor.Scope)}"
                : "Troca pendente — reinicie para aplicar.";
        };

        UiStyle.SetRole(navigation, UiRole.NavigationContainer);
        var shell = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2 };
        shell.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 176));
        shell.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        shell.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        shell.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        foreach (var (title, page) in new[]
                 {
                     ("Início", (UserControl)homePage), ("Nova execução", executionPage),
                     ("Histórico", historyPage), ("Perfis", profilesPage), ("Configurações", settingsPage)
                 })
            navigation.Controls.Add(CreateNavigationButton(title, page));
        foreach (var page in new UserControl[] { settingsPage, profilesPage, historyPage, executionPage, homePage })
        {
            page.Visible = false;
            pageHost.Controls.Add(page);
        }
        var statusPanel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(10, 7, 10, 0) };
        globalStatus.Text = $"Banco atual: {SettingsPage.ScopeName(applicationStore.Descriptor.Scope)}";
        statusPanel.Controls.Add(globalStatus);
        shell.Controls.Add(navigation, 0, 0);
        shell.SetRowSpan(navigation, 2);
        shell.Controls.Add(pageHost, 1, 0);
        shell.Controls.Add(statusPanel, 1, 1);
        Controls.Add(shell);
        ShowPage("Início", homePage);
    }

    private UserControl BuildExecutionPage()
    {
        var page = new UserControl { Dock = DockStyle.Fill, Padding = new Padding(12) };
        var root = new BufferedPanel { Dock = DockStyle.Fill };
        var settingsArea = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 2, RowCount = 1 };
        var connection = BuildConnectionGroup();
        var execution = BuildExecutionGroup();
        settingsArea.Controls.Add(connection, 0, 0);
        settingsArea.Controls.Add(execution, 1, 0);
        var viewport = new BufferedPanel { Name = "ExecutionConfigurationViewport", Dock = DockStyle.Top, AutoScroll = true };
        var columns = 0;
        settingsArea.SizeChanged += (_, _) =>
        {
            // Base the transition on the viewport width so its vertical scrollbar
            // cannot change the threshold when resizing back to two columns.
            var count = viewport.Width * 96d / settingsArea.DeviceDpi >= 680 ? 2 : 1;
            if (columns == count) return;
            columns = count;
            settingsArea.SuspendLayout();
            settingsArea.ColumnCount = count;
            settingsArea.RowCount = count == 2 ? 1 : 2;
            settingsArea.ColumnStyles.Clear(); settingsArea.RowStyles.Clear();
            for (var i = 0; i < count; i++) settingsArea.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / count));
            for (var i = 0; i < settingsArea.RowCount; i++) settingsArea.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            settingsArea.SetCellPosition(connection, new TableLayoutPanelCellPosition(0, 0));
            settingsArea.SetCellPosition(execution, new TableLayoutPanelCellPosition(count == 2 ? 1 : 0, count == 2 ? 0 : 1));
            settingsArea.ResumeLayout(true);
        };
        viewport.Controls.Add(settingsArea);
        resultsControl.Dock = DockStyle.Fill;
        var note = new Label { Dock = DockStyle.Bottom, Height = Font.Height + 8, Padding = new Padding(3, 6, 3, 0),
            Text = "Credenciais ficam somente na memória. O histórico completo é salvo no banco local." };
        var actions = BuildControlArea();
        var progress = BuildProgressArea();
        var profileArea = BuildProfileArea();
        root.Controls.Add(resultsControl);
        root.Controls.Add(note);
        root.Controls.Add(progress);
        root.Controls.Add(viewport);
        root.Controls.Add(profileArea);
        root.Controls.Add(actions);
        var updatingConfigurationHeight = false;
        void UpdateConfigurationHeight()
        {
            if (updatingConfigurationHeight) return;
            updatingConfigurationHeight = true;
            try
            {
                // Explicit row heights follow the already laid-out group content,
                // avoiding stale preferred sizes when wrapped options change height.
                var connectionHeight = connection.Height + connection.Margin.Vertical;
                var executionHeight = execution.Height + execution.Margin.Vertical;
                if (settingsArea.RowStyles.Count > 0)
                {
                    settingsArea.RowStyles[0].SizeType = SizeType.Absolute;
                    settingsArea.RowStyles[0].Height = columns == 2 ? Math.Max(connectionHeight, executionHeight) : connectionHeight;
                    if (columns == 1 && settingsArea.RowStyles.Count > 1)
                    {
                        settingsArea.RowStyles[1].SizeType = SizeType.Absolute;
                        settingsArea.RowStyles[1].Height = executionHeight;
                    }
                }
                var requiredHeight = columns == 2 ? Math.Max(connectionHeight, executionHeight) : connectionHeight + executionHeight;
                var resultMinimum = 188 * root.DeviceDpi / 96;
                var available = Math.Max(100 * root.DeviceDpi / 96, root.ClientSize.Height - actions.Height - profileArea.Height - progress.Height - note.Height - resultMinimum);
                var height = Math.Min(requiredHeight, available);
                if (viewport.Height != height) viewport.Height = height;
                var needsScroll = requiredHeight > available;
                if (viewport.AutoScroll != needsScroll) viewport.AutoScroll = needsScroll;
                if (!needsScroll)
                {
                    viewport.AutoScrollPosition = Point.Empty;
                    viewport.VerticalScroll.Visible = viewport.HorizontalScroll.Visible = false;
                }
            }
            finally { updatingConfigurationHeight = false; }
        }
        var configurationLayoutPending = false;
        void ScheduleConfigurationHeight()
        {
            if (!root.IsHandleCreated) { UpdateConfigurationHeight(); return; }
            if (configurationLayoutPending || root.IsDisposed) return;
            configurationLayoutPending = true;
            root.BeginInvoke((Action)(() =>
            {
                configurationLayoutPending = false;
                if (!root.IsDisposed) UpdateConfigurationHeight();
            }));
        }
        settingsArea.SizeChanged += (_, _) => ScheduleConfigurationHeight();
        root.SizeChanged += (_, _) => ScheduleConfigurationHeight();
        actions.SizeChanged += (_, _) => ScheduleConfigurationHeight();
        profileArea.SizeChanged += (_, _) => ScheduleConfigurationHeight();
        progress.SizeChanged += (_, _) => ScheduleConfigurationHeight();
        root.DpiChangedAfterParent += (_, _) => ScheduleConfigurationHeight();
        page.VisibleChanged += (_, _) => { if (page.Visible) ScheduleConfigurationHeight(); };
        UpdateConfigurationHeight();
        page.Controls.Add(root);
        return page;
    }

    private Button CreateNavigationButton(string title, UserControl page)
    {
        var button = new NavigationButton
        {
            Text = title,
            Width = 150,
            Height = 42,
            FlatStyle = FlatStyle.Flat,
            TextAlign = ContentAlignment.MiddleLeft,
            Margin = new Padding(2)
        };
        button.FlatAppearance.BorderSize = 0;
        button.Click += (_, _) => ShowPage(title, page);
        navigationButtons.Add(title, button);
        return button;
    }

    private NavigationButton? activeNavigationButton;
    private UserControl? activePage;
    private bool profileNavigationPending;
    private async void ShowPage(string title, UserControl page)
    {
        if (ReferenceEquals(activePage, page) || profileNavigationPending) return;
        if (ReferenceEquals(activePage, profilesPage) || ReferenceEquals(activePage, settingsPage))
        {
            profileNavigationPending = true;
            try
            {
                var allowed = ReferenceEquals(activePage, profilesPage)
                    ? await profilesPage.TryLeaveAsync() : await settingsPage.TryLeaveAsync();
                if (!allowed || IsDisposed) return;
            }
            finally { profileNavigationPending = false; }
        }
        void ActivatePage()
        {
            pageHost.SuspendLayout();
            try
            {
                activeNavigationButton?.SetSelected(false);
                activeNavigationButton = (NavigationButton)navigationButtons[title];
                activeNavigationButton.SetSelected(true);
                if (activePage is not null) activePage.Visible = false;
                activePage = page;
                page.Visible = true;
                page.BringToFront();
            }
            finally { pageHost.ResumeLayout(true); }
        }
        if (ReferenceEquals(page, settingsPage) || ReferenceEquals(activePage, settingsPage))
            settingsPage.BatchLayout(ActivatePage);
        else ActivatePage();
    }

    private sealed class NavigationButton : ThemedButton
    {
        private readonly Font regularFont;
        private readonly Font selectedFont;

        public NavigationButton()
        {
            UiStyle.SetRole(this, UiRole.Navigation);
            regularFont = new Font(UiTypography.Body, FontStyle.Regular);
            selectedFont = new Font(UiTypography.Body, FontStyle.Bold);
            Font = regularFont;
        }

        public void SetSelected(bool selected)
        {
            UiStyle.SetRole(this, UiRole.Navigation);
            UiStyle.SetState(this, selected ? UiState.Selected : UiState.Normal);
            Font = selected ? selectedFont : regularFont;
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing) { regularFont.Dispose(); selectedFont.Dispose(); }
        }
    }

    internal void NavigateTo(string title)
    {
        if (!navigationButtons.TryGetValue(title, out var button))
            throw new ArgumentOutOfRangeException(nameof(title));
        button.PerformClick();
    }

    private static UserControl CreatePlaceholderPage(string title, string description)
    {
        var page = new UserControl { Dock = DockStyle.Fill, Padding = new Padding(28) };
        var heading = new Label { Text = title, AutoSize = true, Font = new Font(page.Font.FontFamily, 20, FontStyle.Bold) };
        var text = new Label { Text = description, AutoSize = true, Top = 50 };
        page.Controls.Add(text);
        page.Controls.Add(heading);
        return page;
    }

    private async Task RefreshProfilesAsync()
    {
        try
        {
            await profilesPage.RefreshAsync();
        }
        catch (ApplicationStoreException exception)
        {
            MessageBox.Show(this, exception.Message, "Perfis", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private async Task RefreshHistoryAsync()
    {
        try
        {
            await Task.WhenAll(historyPage.RefreshAsync(), homePage.RefreshAsync());
        }
        catch (ApplicationStoreException exception)
        {
            MessageBox.Show(this, exception.Message, "Histórico", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void ApplyProfile(SavedConnectionProfile profile)
    {
        if (!configurationEnabled) return;
        applyingExecutionProfile = true;
        executionPage.SuspendLayout();
        try
        {
            cmbDatabaseType.SelectedItem = DatabaseProfiles.All.First(item => item.Type == profile.DatabaseType);
            txtHost.Text = profile.Host;
            if (profile.Port is int value)
                numPort.Value = value;
            txtUser.Text = profile.UserName;
            txtPassword.Clear();
            txtDatabase.Text = profile.DatabaseName;
            txtSqliteFile.Text = profile.SqliteFile;
            cmbSqlServerAuth.SelectedItem = profile.SqlServerAuthentication;
            txtOdbcDriver.Text = profile.OdbcDriver;
            numTests.Value = profile.ExecutionDefaults.TestCount;
            chkContinuous.Checked = profile.ExecutionDefaults.Continuous;
            numInterval.Value = (decimal)profile.ExecutionDefaults.IntervalSeconds;
            numTimeout.Value = profile.ExecutionDefaults.TimeoutSeconds;
            chkDns.Checked = profile.ExecutionDefaults.Dns;
            chkPing.Checked = profile.ExecutionDefaults.Ping;
            chkTcp.Checked = profile.ExecutionDefaults.Tcp;
            chkDatabase.Checked = profile.ExecutionDefaults.DatabaseTest;
            chkBackground.Checked = profile.ExecutionDefaults.StartInBackground;
            selectedProfileId = profile.ProfileId;
            ApplyDatabaseType(resetPort: false);
            appliedProfileVersion = profile;
            appliedProfileSnapshot = ExecutionProfileSnapshot();
            SyncExecutionProfileSelection(addAppliedIfMissing: true);
        }
        finally { applyingExecutionProfile = false; executionPage.ResumeLayout(true); }
        UpdateExecutionProfileState();
        ShowPage("Nova execução", executionPage);
        if (txtPassword.Visible && txtPassword.Enabled) txtPassword.Focus();
    }

    private void ApplyApplicationSettings(ApplicationSettings settings)
    {
        applicationSettings = settings;
        runCoordinator.Dispose();
        runCoordinator = CreateRunCoordinator(settings);
        ThemeManager.Apply(this, settings.Theme);
        lblStatus.Text = "Configurações salvas.";
    }

    private GroupBox BuildConnectionGroup()
    {
        var table = CreateSettingsTable(labelWidth: 110);
        var row = 0;
        AddRow(table, row++, "Tipo de banco:", cmbDatabaseType);
        hostRow = AddRow(table, row++, "Servidor / host:", txtHost);
        portRow = AddRow(table, row++, "Porta:", numPort);
        sqlAuthRow = AddRow(table, row++, "Autenticação:", cmbSqlServerAuth);
        userRow = AddRow(table, row++, "Usuário:", txtUser);
        passwordRow = AddRow(table, row++, "Senha:", txtPassword);
        databaseRow = AddRow(table, row++, "Banco (opcional):", txtDatabase);
        odbcDriverRow = AddRow(table, row++, "Driver ODBC:", txtOdbcDriver);

        var sqlitePanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            AutoSize = true,
            Margin = Padding.Empty
        };
        sqlitePanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        sqlitePanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        txtSqliteFile.Dock = DockStyle.Fill;
        sqlitePanel.Controls.Add(txtSqliteFile, 0, 0);
        sqlitePanel.Controls.Add(btnBrowseSqlite, 1, 0);
        sqliteFileRow = AddRow(table, row, "Arquivo SQLite:", sqlitePanel);

        return CreateGroup("Conexão", table);
    }

    private GroupBox BuildExecutionGroup()
    {
        var table = CreateSettingsTable(labelWidth: 88);
        chkContinuous.Text = "Execução contínua";
        chkBackground.Text = "Minimizar ao iniciar";
        numTests.Width = 110;
        numInterval.Width = numTimeout.Width = 75;
        var hints = new ToolTip();
        Disposed += (_, _) => hints.Dispose();
        hints.SetToolTip(chkContinuous, "Executa até você clicar em Parar; a quantidade é ignorada.");
        hints.SetToolTip(chkBackground, "Minimiza o aplicativo para a bandeja ao iniciar a execução.");
        hints.SetToolTip(numInterval, "Tempo de espera entre os ciclos, em segundos.");
        hints.SetToolTip(numTimeout, "Tempo limite de cada etapa, em segundos.");
        FlowLayoutPanel Flow(params Control[] controls)
        {
            var panel = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = true, Margin = Padding.Empty };
            panel.Controls.AddRange(controls);
            return panel;
        }
        Control Timing(string caption, Control input)
        {
            var panel = Flow(new Label { Text = caption, AutoSize = true, Margin = new Padding(0, 6, 3, 0) },
                input, new Label { Text = "s", AutoSize = true, Margin = new Padding(3, 6, 6, 0) });
            panel.Dock = DockStyle.None;
            panel.WrapContents = false;
            panel.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            return panel;
        }
        AddRow(table, 0, "Quantidade:", Flow(numTests, chkContinuous));
        AddRow(table, 1, "Tempos:", Flow(Timing("Intervalo:", numInterval), Timing("Timeout:", numTimeout)));
        AddRow(table, 2, "Etapas:", Flow(chkDns, chkPing, chkTcp, chkDatabase));
        AddRow(table, 3, "Ao iniciar:", chkBackground);
        // Multi-line option rows keep their captions aligned with the first input.
        foreach (var label in table.Controls.OfType<Label>()) label.Anchor = AnchorStyles.Top | AnchorStyles.Left;
        return CreateGroup("Execução", table);
    }

    private Control BuildControlArea()
    {
        var area = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = false,
            ColumnCount = 1,
            RowCount = 1,
            Margin = new Padding(0, 6, 0, 6)
        };
        // The action bar has a bounded height. An AutoSize table can retain surplus
        // height in its last row even when that row has an absolute style.
        area.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            Margin = Padding.Empty
        };
        btnTestOnce.Font = UiTypography.Emphasis;
        btnStart.Font = UiTypography.Emphasis;
        buttons.Controls.AddRange(new Control[]
        {
            btnTestOnce, btnStart, btnStop, btnOpenCsv, btnOpenLog, btnOpenFolder
        });

        area.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        area.Controls.Add(buttons, 0, 0);
        (int Width, Font Font, int Dpi)? measuredActions = null;
        var actionsHeight = 0;
        void UpdateHeight()
        {
            var key = (Math.Max(1, area.ClientSize.Width), buttons.Font, area.DeviceDpi);
            if (measuredActions != key)
            {
                measuredActions = key;
                actionsHeight = buttons.GetPreferredSize(new Size(key.Item1, 0)).Height;
            }
            var height = actionsHeight;
            if (area.Height != height) area.Height = height;
        }
        // Invalidate the measurement when button content changes (e.g. DPI scaling).
        foreach (Control button in buttons.Controls)
        {
            button.TextChanged += (_, _) => { measuredActions = null; UpdateHeight(); };
            button.FontChanged += (_, _) => { measuredActions = null; UpdateHeight(); };
            button.PaddingChanged += (_, _) => { measuredActions = null; UpdateHeight(); };
        }
        area.SizeChanged += (_, _) => UpdateHeight();
        area.FontChanged += (_, _) => UpdateHeight();
        area.DpiChangedAfterParent += (_, _) => UpdateHeight();
        UpdateHeight();
        return area;
    }

    private Control BuildProgressArea()
    {
        var area = new TableLayoutPanel { Name = "ExecutionProgressArea", Dock = DockStyle.Top, AutoSize = false,
            ColumnCount = 2, RowCount = 2, Padding = new Padding(3, 2, 3, 5) };
        area.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40));
        area.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 60));
        for (var i = 0; i < 2; i++) area.RowStyles.Add(new RowStyle(SizeType.Absolute));
        lblStatus.AutoSize = lblRunProgress.AutoSize = false;
        lblStatus.Dock = lblRunProgress.Dock = progressBar.Dock = DockStyle.Fill;
        lblStatus.Margin = lblRunProgress.Margin = Padding.Empty;
        progressBar.Margin = new Padding(0, 2, 0, 3);
        area.Controls.Add(lblStatus, 0, 0);
        lblRunProgress.TextAlign = ContentAlignment.MiddleRight;
        area.Controls.Add(lblRunProgress, 1, 0);
        area.Controls.Add(progressBar, 0, 1);
        area.SetColumnSpan(progressBar, 2);
        void UpdateHeight()
        {
            var statusHeight = Math.Clamp(TextRenderer.MeasureText(lblStatus.Text, lblStatus.Font,
                new Size(Math.Max(1, (int)((area.ClientSize.Width - area.Padding.Horizontal) * (showRunProgress ? 0.4 : 1))), int.MaxValue), TextFormatFlags.WordBreak).Height + 4,
                lblStatus.Font.Height + 6, lblStatus.Font.Height * 2 + 8);
            var countHeight = showRunProgress ? lblRunProgress.Font.Height + 6 : 0;
            var barHeight = showProgressBar ? 20 * area.DeviceDpi / 96 : 0;
            area.SetColumnSpan(lblStatus, showRunProgress ? 1 : 2);
            area.RowStyles[0].Height = Math.Max(statusHeight, countHeight);
            area.RowStyles[1].Height = barHeight;
            var height = Math.Max(statusHeight, countHeight) + barHeight + area.Padding.Vertical;
            if (area.Height != height) area.Height = height;
        }
        area.SizeChanged += (_, _) => UpdateHeight();
        area.FontChanged += (_, _) => UpdateHeight();
        area.DpiChangedAfterParent += (_, _) => UpdateHeight();
        lblStatus.TextChanged += (_, _) => UpdateHeight();
        lblRunProgress.VisibleChanged += (_, _) => UpdateHeight();
        progressBar.VisibleChanged += (_, _) => UpdateHeight();
        elapsedTimer.Tick += (_, _) => UpdateRunProgressText();
        Disposed += (_, _) => elapsedTimer.Dispose();
        UpdateHeight();
        return area;
    }

    private static TableLayoutPanel CreateSettingsTable(int labelWidth)
    {
        var table = UiLayout.Fields(labelWidth);
        table.Padding = new Padding(8, 5, 8, 5);
        return table;
    }

    private static GroupBox CreateGroup(string title, Control content)
    {
        var group = new ThemedGroupBox
        {
            Text = title,
            Dock = DockStyle.Top,
            AutoSize = false,
            Margin = new Padding(4)
        };
        void FitContent()
        {
            var height = content.Bottom + group.Padding.Bottom;
            if (group.Height != height) group.Height = height;
        }
        content.SizeChanged += (_, _) => FitContent();
        group.Layout += (_, _) => FitContent();
        group.Controls.Add(content);
        FitContent();
        return group;
    }

    private static Control WithSuffix(Control control, string suffix)
    {
        var panel = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 2, Margin = Padding.Empty };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        control.Dock = DockStyle.Fill;
        panel.Controls.Add(control, 0, 0);
        panel.Controls.Add(new Label { Text = suffix, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(6, 4, 0, 0) }, 1, 0);
        return panel;
    }

    private static UiLayout.FieldRow AddRow(TableLayoutPanel table, int row, string text, Control control)
    {
        var field = UiLayout.AddField(table, row, text, control);
        field.Label.Margin = new Padding(3, 2, 6, 2);
        field.Input.Margin = new Padding(3, 1, 3, 1);
        return field;
    }

    private void ConfigureTray()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add(trayStatus);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(trayOpenPanel);
        menu.Items.Add(trayOpenCsv);
        menu.Items.Add(trayOpenLog);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(trayStop);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(trayExit);

        trayIcon.Icon = SystemIcons.Application;
        trayIcon.Text = "DB Connection Tester";
        trayIcon.ContextMenuStrip = menu;
        trayIcon.Visible = false;
        trayIcon.DoubleClick += (_, _) => ShowPanel();
    }

    private void WireEvents()
    {
        cmbDatabaseType.SelectedIndexChanged += (_, _) =>
        {
            ApplyDatabaseType(resetPort: true);
        };
        cmbSqlServerAuth.SelectedIndexChanged += (_, _) => ApplyCredentialVisibility();
        chkContinuous.CheckedChanged += (_, _) => numTests.Enabled = configurationEnabled && !chkContinuous.Checked;
        btnBrowseSqlite.Click += (_, _) => BrowseSqliteFile();
        btnOpenFolder.Click += (_, _) => OpenOutputFolder();
        btnOpenCsv.Click += (_, _) => OpenFile(currentCsvPath);
        btnOpenLog.Click += (_, _) => OpenFile(currentTxtPath);
        btnTestOnce.Click += async (_, _) => await StartAsync(singleRun: true);
        btnStart.Click += async (_, _) => await StartAsync(singleRun: false);
        btnStop.Click += (_, _) => RequestStop();
        trayOpenPanel.Click += (_, _) => ShowPanel();
        trayOpenCsv.Click += (_, _) => OpenFile(currentCsvPath);
        trayOpenLog.Click += (_, _) => OpenFile(currentTxtPath);
        trayStop.Click += (_, _) => RequestStop();
        trayExit.Click += (_, _) => RequestExit();
        TrackExecutionProfileChanges();
        FormClosing += OnFormClosing;
        FormClosed += (_, _) =>
        {
            runCoordinator.Dispose();
            trayIcon.Visible = false;
            trayIcon.Dispose();
        };
    }

    private void ApplyDatabaseType(bool resetPort)
    {
        var profile = SelectedProfile;
        var state = DatabaseUiState.Create(profile.Type, SelectedSqlServerAuthentication);
        if (resetPort && profile.DefaultPort.HasValue)
            numPort.Value = profile.DefaultPort.Value;

        hostRow.SetVisible(state.ShowHost);
        portRow.SetVisible(state.ShowPort);
        databaseRow.SetVisible(state.ShowDatabase);
        sqlAuthRow.SetVisible(state.ShowSqlServerAuthentication);
        odbcDriverRow.SetVisible(state.ShowOdbcDriver);
        sqliteFileRow.SetVisible(state.ShowSqliteFile);

        if (resetPort && state.RequireDatabaseTest)
        {
            chkDns.Checked = false;
            chkPing.Checked = false;
            chkTcp.Checked = false;
            chkDatabase.Checked = true;
        }
        else if (resetPort && state.RequireTcp)
        {
            chkDns.Checked = true;
            chkPing.Checked = true;
            chkTcp.Checked = true;
            chkDatabase.Checked = false;
        }
        else if (resetPort)
        {
            chkDns.Checked = true;
            chkPing.Checked = true;
            chkTcp.Checked = true;
            chkDatabase.Checked = true;
        }

        chkDns.Enabled = configurationEnabled && state.AllowDns;
        chkPing.Enabled = configurationEnabled && state.AllowPing;
        chkTcp.Enabled = configurationEnabled && state.AllowTcp;
        chkDatabase.Enabled = configurationEnabled && state.AllowDatabaseTest;
        ApplyCredentialVisibility();
    }

    private void ApplyCredentialVisibility()
    {
        var state = DatabaseUiState.Create(SelectedProfile.Type, SelectedSqlServerAuthentication);
        userRow.SetVisible(state.ShowCredentials);
        passwordRow.SetVisible(state.ShowCredentials);
    }

}

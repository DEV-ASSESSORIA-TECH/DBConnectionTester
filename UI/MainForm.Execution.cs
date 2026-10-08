using DBConnectionTester.Application;
using DBConnectionTester.Models;
using DBConnectionTester.Services.Storage;

namespace DBConnectionTester.UI;

public sealed partial class MainForm
{
    private async Task StartAsync(bool singleRun)
    {
        if (runCoordinator.IsRunning)
            return;

        var settings = TryBuildSettings();
        if (settings is null)
            return;
        if (singleRun)
            settings = settings with
            {
                TestCount = RunCount.Create(1),
                Continuous = false,
                Interval = TestInterval.Create(TimeSpan.Zero)
            };

        currentCsvPath = "";
        currentTxtPath = "";
        exitRequested = false;
        completedTests = 0;
        resultsControl.ResetResults();
        ApplyRunUiState(RunUiState.Running, settings);
        UpdateTrayStatus(settings, new TestProgress(0, 0, 0, 0, 0, 0));
        lblStatus.Text = singleRun ? "Executando teste único..." : "Preparando execução...";
        homePage.UpdateRunStatus(lblStatus.Text);

        if (!singleRun && chkBackground.Checked)
            HideToTray("Teste iniciado em segundo plano.");

        try
        {
            var uiProgress = new Progress<TestProgress>(value => UpdateProgress(settings, value, singleRun));
            var summary = await runCoordinator.StartAsync(settings, uiProgress);
            completedTests = summary.Completed;
            await CompleteRunAsync(settings, summary);
        }
        catch (Exception exception)
        {
            ApplyRunUiState(RunUiState.Failed);
            lblStatus.Text = $"Erro: {exception.Message}";
            ShowPanel();
            MessageBox.Show(this, exception.ToString(), "Erro", MessageBoxButtons.OK, MessageBoxIcon.Error);
            homePage.UpdateRunStatus("A última execução falhou. Consulte o histórico para obter detalhes.");
            await RefreshHistoryAsync();
        }
        finally
        {
            if (exitRequested)
            {
                trayIcon.Visible = false;
                Close();
            }
        }
    }

    private TestSettings? TryBuildSettings()
    {
        var input = new TestSettingsInput(
            SelectedProfile.Type,
            txtHost.Text,
            (int)numPort.Value,
            txtUser.Text,
            txtPassword.Text,
            txtDatabase.Text,
            txtSqliteFile.Text,
            SelectedSqlServerAuthentication,
            txtOdbcDriver.Text,
            (long)numTests.Value,
            chkContinuous.Checked,
            TimeSpan.FromSeconds((double)numInterval.Value),
            TimeSpan.FromSeconds((double)numTimeout.Value),
            chkDns.Checked,
            chkPing.Checked,
            chkTcp.Checked,
            chkDatabase.Checked);

        var result = settingsValidator.Validate(input);
        return result.IsValid ? result.Settings! with { ProfileId = selectedProfileId } : ValidationError(result.ErrorMessage);
    }

    private TestSettings? ValidationError(string message)
    {
        MessageBox.Show(this, message, "Validação", MessageBoxButtons.OK, MessageBoxIcon.Information);
        return null;
    }

    private void UpdateProgress(TestSettings settings, TestProgress value, bool singleRun)
    {
        completedTests = Math.Max(completedTests, value.Completed);
        if (value.LatestCycle is not null)
            resultsControl.AddCycle(value.LatestCycle);
        if (value.Statistics is not null)
            resultsControl.UpdateStatistics(value.Statistics);
        if (runUiState is not (RunUiState.Running or RunUiState.Stopping)) return;
        if (!settings.Continuous)
        {
            var percentage = (int)Math.Round(value.Completed * 100.0 / settings.TestCount.Value);
            progressBar.Value = Math.Clamp(percentage, 0, 100);
        }
        lblStatus.Text = runUiState == RunUiState.Stopping ? "Finalizando execução..."
            : settings.Continuous ? "Execução contínua em andamento." : "Executando testes...";
        UpdateRunProgressText();
        homePage.UpdateRunStatus(lblStatus.Text);
        UpdateTrayStatus(settings, value);
    }

    private async Task CompleteRunAsync(TestSettings settings, RunSummary summary)
    {
        currentCsvPath = summary.CsvPath ?? "";
        currentTxtPath = summary.TxtPath ?? "";
        ApplyRunUiState(RunUiState.Completed);
        IReadOnlyList<PersistedRunWarning> warnings = [];
        try
        {
            var persisted = await new RunHistoryRepository(applicationStore).GetDetailsAsync(summary.RunId);
            warnings = persisted?.Warnings ?? [];
        }
        catch (Exception exception) when (exception is ApplicationStoreException or Microsoft.Data.Sqlite.SqliteException)
        {
            // A execução já foi concluída; uma falha de atualização da UI não altera seu resultado persistido.
        }
        if (summary.Stopped)
        {
            lblStatus.Text = $"Teste interrompido. {summary.Completed:N0} verificações gravadas.";
            homePage.UpdateRunStatus(lblStatus.Text);
            trayStatus.Text = $"Interrompido - {summary.Completed:N0} testes";
            SafeTrayText($"DB Connection Tester - parado - {summary.Completed:N0}");
            await RefreshHistoryAsync();
            return;
        }

        lblStatus.Text = string.IsNullOrWhiteSpace(summary.CsvPath)
            ? $"Concluído. {summary.Completed:N0} verificações salvas no histórico."
            : $"Concluído. Histórico salvo | CSV: {Path.GetFileName(summary.CsvPath)} | TXT: {Path.GetFileName(summary.TxtPath)}";
        if (warnings.Count > 0)
            lblStatus.Text += $" | {warnings.Count} aviso(s) de saída";
        homePage.UpdateRunStatus(lblStatus.Text);
        progressBar.Value = 100;
        trayStatus.Text = $"Concluído - {summary.Completed:N0} testes";
        SafeTrayText($"DB Connection Tester - concluído - {summary.Completed:N0}");
        trayIcon.Visible = true;
        trayIcon.BalloonTipTitle = "DB Connection Tester";
        trayIcon.BalloonTipText = $"Teste concluído. {summary.Completed:N0} verificações executadas.";
        trayIcon.BalloonTipIcon = ToolTipIcon.Info;
        trayIcon.ShowBalloonTip(3000);
        await RefreshHistoryAsync();
        if (warnings.Count > 0)
        {
            MessageBox.Show(this,
                "A execução foi salva no SQLite, mas houve falha parcial:\n\n" +
                string.Join(Environment.NewLine, warnings.Select(item => $"[{item.Code}] {item.Message}")),
                "Execução concluída com avisos",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
    }

    private void RequestStop()
    {
        if (!runCoordinator.IsRunning || runUiState == RunUiState.Stopping)
            return;

        ApplyRunUiState(RunUiState.Stopping);
        lblStatus.Text = "Encerrando teste e finalizando o log...";
        homePage.UpdateRunStatus(lblStatus.Text);
        trayStatus.Text = "Encerrando teste...";
        runCoordinator.Stop();
    }

    private void RequestExit()
    {
        if (runCoordinator.IsRunning)
        {
            var result = MessageBox.Show(
                "Há um teste em andamento. Deseja encerrar o teste, finalizar o log e sair?",
                "Sair do DB Connection Tester",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);
            if (result != DialogResult.Yes)
                return;
            BeginExitAfterRun();
            return;
        }

        exitRequested = true;
        trayIcon.Visible = false;
        Close();
    }

    private void OnFormClosing(object? sender, FormClosingEventArgs e)
    {
        if (!runCoordinator.IsRunning)
            return;

        e.Cancel = true;
        if (exitRequested)
            return;

        if (e.CloseReason is CloseReason.WindowsShutDown or CloseReason.TaskManagerClosing)
        {
            BeginExitAfterRun();
            return;
        }

        HideToTray("Teste continua em execução.");
    }

    private void BeginExitAfterRun()
    {
        exitRequested = true;
        ApplyRunUiState(RunUiState.Stopping);
        lblStatus.Text = "Encerrando teste, finalizando os arquivos e saindo...";
        trayStatus.Text = "Finalizando arquivos...";
        runCoordinator.Stop();
    }

    private void ApplyRunUiState(RunUiState state, TestSettings? settings = null)
    {
        runUiState = state;
        var active = state is RunUiState.Running or RunUiState.Stopping;
        var stopping = state == RunUiState.Stopping;
        SetConfigurationEnabled(!active);
        btnTestOnce.Enabled = !active;
        btnStart.Enabled = !active;
        btnStop.Enabled = active && !stopping;
        trayStop.Enabled = active && !stopping;
        profilesPage.SetEditingEnabled(!active);
        settingsPage.SetOperationsEnabled(!active);

        var hasOutput = !string.IsNullOrWhiteSpace(currentCsvPath);
        btnOpenCsv.Enabled = hasOutput;
        btnOpenLog.Enabled = !string.IsNullOrWhiteSpace(currentTxtPath);
        trayOpenCsv.Enabled = hasOutput;
        trayOpenLog.Enabled = btnOpenLog.Enabled;

        if (state == RunUiState.Running && settings is not null)
        {
            progressSettings = settings;
            runElapsed.Restart();
            elapsedTimer.Start();
            progressBar.Value = 0;
        }
        else if (!active)
        {
            elapsedTimer.Stop();
            runElapsed.Stop();
        }
        showRunProgress = progressSettings is not null && state != RunUiState.Idle;
        showProgressBar = showRunProgress && progressSettings?.Continuous == false && state != RunUiState.Failed;
        // Continuous runs have no completion percentage.
        progressBar.Style = ProgressBarStyle.Blocks;
        progressBar.Visible = showProgressBar;
        lblRunProgress.Visible = showRunProgress;
        UpdateRunProgressText();
    }

    private void UpdateRunProgressText()
    {
        if (!showRunProgress || progressSettings is null) return;
        var elapsed = runElapsed.Elapsed;
        var duration = $"{(long)elapsed.TotalHours:00}:{elapsed.Minutes:00}:{elapsed.Seconds:00}";
        lblRunProgress.Text = progressSettings.Continuous
            ? $"{completedTests:N0} ciclos concluídos · {duration} decorridos"
            : $"{completedTests:N0} de {progressSettings.TestCount.Value:N0} ciclos · {duration} decorridos";
    }

    private void SetConfigurationEnabled(bool enabled)
    {
        configurationEnabled = enabled;
        foreach (var control in new Control[]
                 {
                     cmbDatabaseType, txtHost, numPort, cmbSqlServerAuth, txtUser, txtPassword, txtDatabase,
                     txtOdbcDriver, txtSqliteFile, btnBrowseSqlite, numInterval, numTimeout, chkContinuous,
                     chkDns, chkPing, chkTcp, chkDatabase, chkBackground
                 })
            control.Enabled = enabled;
        numTests.Enabled = enabled && !chkContinuous.Checked;
        ApplyDatabaseType(resetPort: false);
    }

    private void UpdateTrayStatus(TestSettings settings, TestProgress value)
    {
        trayIcon.Visible = true;
        trayStatus.Text = settings.Continuous
            ? $"Executando - {value.Completed:N0} testes"
            : $"Executando - {value.Completed:N0}/{settings.TestCount.Value:N0}";
        var tooltip = settings.Continuous
            ? $"DB Tester - {value.Completed:N0} - falhas D:{value.DnsFailures} P:{value.PingFailures} T:{value.TcpFailures} DB:{value.DatabaseFailures}"
            : $"DB Tester - {value.Completed:N0}/{settings.TestCount.Value:N0} - falhas D:{value.DnsFailures} P:{value.PingFailures} T:{value.TcpFailures} DB:{value.DatabaseFailures}";
        SafeTrayText(tooltip);
    }
}

' ─────────────────────────────────────────────────────────────────────────────
' Quallit NetSpeedTest  |  MainForm.vb
' Tela da bancada: cópia do "Teste de velocidade" do app principal (mesmo tema e
' disposição), medindo por tempo com 1, 4 e 8 conexões e com progresso visível
' durante a medição (velocidade ao vivo, gráfico por segundo, barras de progresso).
'
' © Quallit — Licenciado sob GPL-3.0-or-later.
' ─────────────────────────────────────────────────────────────────────────────

Imports System.Drawing
Imports System.Drawing.Drawing2D
Imports System.IO
Imports System.Text
Imports System.Threading
Imports System.Windows.Forms
Imports qdisk.Core.Models
Imports qdisk.Core.Services
Imports Quallit.NetSpeedTest.Theme

Public Class MainForm
    Inherits Form

    Private Shared ReadOnly ConnectionOptions As Integer() = {1, 4, 8}

    Private ReadOnly _catalogSource As New CatalogSource()
    Private ReadOnly _service As New NetSpeedTestService()
    Private ReadOnly _results As New List(Of SpeedMeasurement)()
    Private ReadOnly _btnStart As Button
    Private ReadOnly _btnCancel As Button
    Private ReadOnly _btnExport As Button
    Private ReadOnly _comboDuration As ComboBox
    Private ReadOnly _chkFinalUrl As CheckBox
    Private ReadOnly _chkSwapSlow As CheckBox
    Private ReadOnly _toolTip As New ToolTip()
    Private ReadOnly _connectionChecks As New List(Of CheckBox)()
    Private ReadOnly _cards As New List(Of OriginCard)()
    Private ReadOnly _statusLabel As Label
    Private ReadOnly _measureBar As ProgressBar
    Private ReadOnly _overallBar As ProgressBar
    Private ReadOnly _logBox As TextBox
    Private _cts As CancellationTokenSource

    Public Sub New()
        Text = $"Quallit NetSpeedTest {AppVersionFormat.Format(GetType(MainForm).Assembly.GetName().Version)}"
        Font = UiTheme.FontBase
        BackColor = Color.White
        StartPosition = FormStartPosition.CenterScreen
        ' Mesmo tamanho da janela do app principal (MainForm.Designer.vb: 800 x 650)
        Size = New Size(800, 650)
        MinimumSize = New Size(800, 650)
        Try
            Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath)
        Catch
        End Try

        ' ── Cabeçalho (igual ao app principal) ──────────────────────────────
        Dim header = New Panel() With {.Dock = DockStyle.Top, .Height = 56, .BackColor = UiTheme.Navy}
        Dim title = New Label() With {.Text = "Teste de velocidade — bancada", .Dock = DockStyle.Fill, .ForeColor = Color.White,
                                      .Font = UiTheme.FontHeaderTitle, .TextAlign = ContentAlignment.MiddleLeft, .Padding = New Padding(12, 0, 0, 0)}
        header.Controls.Add(title)
        Dim logoPath = Path.Combine(AppPaths.AssetsDir, "logo-quallit-512x512.png")
        If File.Exists(logoPath) Then
            Dim logoHost = New Panel() With {.Dock = DockStyle.Left, .Width = 16 + 28, .Padding = New Padding(16, 14, 0, 14), .BackColor = UiTheme.Navy}
            Dim logo = New PictureBox() With {.Dock = DockStyle.Fill, .SizeMode = PictureBoxSizeMode.Zoom, .BackColor = UiTheme.Navy}
            Using fs = New FileStream(logoPath, FileMode.Open, FileAccess.Read)
                logo.Image = New Bitmap(Image.FromStream(fs))
            End Using
            logoHost.Controls.Add(logo)
            header.Controls.Add(logoHost)
        End If

        ' ── Barra de comandos ───────────────────────────────────────────────
        ' Comandos à esquerda, Exportar CSV encostado à direita
        Dim topBar = New Panel() With {.Dock = DockStyle.Top, .Height = 56, .Padding = New Padding(16, 12, 16, 12), .BackColor = Color.White}
        Dim topPanel = New FlowLayoutPanel() With {.Dock = DockStyle.Fill, .Padding = New Padding(0), .Margin = New Padding(0), .BackColor = Color.White, .WrapContents = False}
        _btnStart = New Button() With {.Text = "Iniciar"}
        _btnCancel = New Button() With {.Text = "Cancelar", .Enabled = False}
        _btnExport = New Button() With {.Text = String.Empty, .Enabled = False}
        UiStatus.StyleAutoButton(_btnStart, ButtonKind.Primary, UiTheme.Icon16(Me, "play", "white"))
        UiStatus.StyleAutoButton(_btnCancel, ButtonKind.Secondary, UiTheme.Icon16(Me, "x", "text"))
        UiTheme.StyleButton(_btnExport, ButtonKind.Secondary, UiTheme.Icon16(Me, "file", "text"))
        _btnExport.ImageAlign = ContentAlignment.MiddleCenter
        _toolTip.SetToolTip(_btnExport, "Exportar CSV")
        _btnStart.Margin = New Padding(0, 0, UiTheme.ButtonGap, 0)
        _btnCancel.Margin = New Padding(0)
        AddHandler _btnStart.Click, AddressOf OnStartClick
        AddHandler _btnCancel.Click, AddressOf OnCancelClick
        AddHandler _btnExport.Click, AddressOf OnExportClick

        Dim durationLabel = CreateBarLabel("Duração:")
        _comboDuration = New ComboBox() With {.DropDownStyle = ComboBoxStyle.DropDownList, .Width = 64, .Margin = New Padding(0, 0, 0, 0)}
        For Each seconds In {10, 20, 30, 60}
            _comboDuration.Items.Add(New DurationOption(seconds))
        Next
        _comboDuration.SelectedIndex = 1
        RoundedComboPainter.Attach(_comboDuration)

        Dim connectionsLabel = CreateBarLabel("Conexões:")
        topPanel.Controls.AddRange(New Control() {_btnStart, _btnCancel, durationLabel, _comboDuration, connectionsLabel})
        For Each count In ConnectionOptions
            Dim check = New CheckBox() With {.Text = count.ToString(), .Tag = count, .Checked = True, .AutoSize = True, .Margin = New Padding(0, 7, 4, 0)}
            _connectionChecks.Add(check)
            topPanel.Controls.Add(check)
        Next
        ' Desligado: cada conexão pede o link da página e segue os redirecionamentos (como um download novo).
        ' Ligado: todas pedem o link final já resolvido, como o app fazia até a 3.03.10.26.
        _chkFinalUrl = New CheckBox() With {.Text = "Link final", .AutoSize = True, .Margin = New Padding(6, 7, 0, 0)}
        _toolTip.SetToolTip(_chkFinalUrl, "Marcado: todas as conexões pedem o link final já resolvido, como o app fazia até a 3.03.10.26." & Environment.NewLine &
                                          "Desmarcado: cada conexão pede o link da página e segue os redirecionamentos.")
        topPanel.Controls.Add(_chkFinalUrl)
        ' Protótipo: conexão lenta é fechada e reaberta do mesmo ponto (no SharePoint cada conexão sorteia um caminho).
        _chkSwapSlow = New CheckBox() With {.Text = "Trocar lenta", .AutoSize = True, .Margin = New Padding(6, 7, 0, 0)}
        _toolTip.SetToolTip(_chkSwapSlow, $"Mesma regra do download: abaixo de {SlowConnectionPolicy.ThresholdBytesPerSec \ 1048576} MiB/s numa janela de {SlowConnectionPolicy.InitialWindowMs \ 1000} s, " &
                                          $"a conexão é fechada e reaberta do mesmo ponto. A janela dobra a cada troca seguida (até {SlowConnectionPolicy.MaxWindowMs \ 1000} s) " &
                                          "e há no máximo uma troca por segundo.")
        topPanel.Controls.Add(_chkSwapSlow)
        Dim exportHost = New Panel() With {.Dock = DockStyle.Right, .Width = 34, .BackColor = Color.White}
        _btnExport.Dock = DockStyle.Fill
        _btnExport.Margin = New Padding(0)
        exportHost.Controls.Add(_btnExport)
        topBar.Controls.Add(topPanel)
        topBar.Controls.Add(exportHost)

        ' ── Cartões das origens ─────────────────────────────────────────────
        Dim contentPanel = New Panel() With {.Dock = DockStyle.Fill, .Padding = New Padding(16, 0, 16, 0), .BackColor = Color.White}
        Dim cardsPanel = New TableLayoutPanel() With {.Dock = DockStyle.Fill, .ColumnCount = 2, .RowCount = 1, .BackColor = Color.White, .Margin = New Padding(0)}
        For i = 0 To 1
            cardsPanel.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 50.0F))
        Next
        cardsPanel.RowStyles.Add(New RowStyle(SizeType.Percent, 100.0F))
        Dim origins = {DownloadOrigin.Dropbox, DownloadOrigin.SharePoint}
        For i = 0 To origins.Length - 1
            Dim card = New OriginCard(origins(i), ConnectionOptions)
            card.Panel.Margin = New Padding(If(i = 0, 0, 6), 0, If(i = origins.Length - 1, 0, 6), 0)
            _cards.Add(card)
            cardsPanel.Controls.Add(card.Panel, i, 0)
        Next
        contentPanel.Controls.Add(cardsPanel)

        ' ── Progresso: linha de status, medição atual e teste inteiro ──────
        Dim progressPanel = New TableLayoutPanel() With {.Dock = DockStyle.Bottom, .Height = 68, .Padding = New Padding(16, 6, 16, 0), .ColumnCount = 2, .RowCount = 3, .BackColor = Color.White}
        progressPanel.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, 104.0F))
        progressPanel.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100.0F))
        progressPanel.RowStyles.Add(New RowStyle(SizeType.Absolute, 24.0F))
        progressPanel.RowStyles.Add(New RowStyle(SizeType.Absolute, 18.0F))
        progressPanel.RowStyles.Add(New RowStyle(SizeType.Absolute, 18.0F))
        _statusLabel = New Label() With {.Text = "Pronto. Carregando catálogos...", .Dock = DockStyle.Fill, .Font = UiTheme.FontBold, .ForeColor = UiTheme.MainText, .TextAlign = ContentAlignment.MiddleLeft, .Margin = New Padding(0), .AutoEllipsis = True}
        progressPanel.Controls.Add(_statusLabel, 0, 0)
        progressPanel.SetColumnSpan(_statusLabel, 2)
        _measureBar = New ProgressBar() With {.Dock = DockStyle.Fill, .Maximum = 1000, .Style = ProgressBarStyle.Continuous, .Margin = New Padding(0, 3, 0, 3)}
        _overallBar = New ProgressBar() With {.Dock = DockStyle.Fill, .Maximum = 1000, .Style = ProgressBarStyle.Continuous, .Margin = New Padding(0, 3, 0, 3)}
        progressPanel.Controls.Add(CreateProgressCaption("Medição atual"), 0, 1)
        progressPanel.Controls.Add(_measureBar, 1, 1)
        progressPanel.Controls.Add(CreateProgressCaption("Teste completo"), 0, 2)
        progressPanel.Controls.Add(_overallBar, 1, 2)

        ' ── Log ─────────────────────────────────────────────────────────────
        Dim logPanel = New Panel() With {.Dock = DockStyle.Bottom, .Height = 96, .Padding = New Padding(16, 6, 16, 12), .BackColor = Color.White}
        _logBox = New TextBox() With {.Dock = DockStyle.Fill, .Multiline = True, .ReadOnly = True, .ScrollBars = ScrollBars.Vertical, .BorderStyle = BorderStyle.None, .Font = New Font("Consolas", 8.5F), .BackColor = UiTheme.LogBack}
        Dim logInner As Panel = Nothing
        Dim logHost = UiTheme.CreateBorderedPanel(UiTheme.BorderLight, UiTheme.LogBack, logInner)
        logHost.Dock = DockStyle.Fill
        logInner.Padding = New Padding(8, 6, 4, 4)
        logInner.Controls.Add(_logBox)
        logPanel.Controls.Add(logHost)

        Controls.Add(contentPanel)
        Controls.Add(progressPanel)
        Controls.Add(logPanel)
        Controls.Add(topBar)
        Controls.Add(header)
        AddHandler Shown, AddressOf OnFormShown
    End Sub

    Private Shared Function CreateBarLabel(text As String) As Label
        Return New Label() With {.Text = text, .AutoSize = True, .Margin = New Padding(16, 8, 6, 0)}
    End Function

    Private Shared Function CreateProgressCaption(text As String) As Label
        Return New Label() With {.Text = text, .Dock = DockStyle.Fill, .ForeColor = UiTheme.MetricText, .TextAlign = ContentAlignment.MiddleLeft, .Margin = New Padding(0)}
    End Function

    Private Async Sub OnFormShown(sender As Object, e As EventArgs)
        SetBusy(True, allowCancel:=False)
        Try
            AppendLog("Carregando catálogos publicados...")
            Dim items = Await _catalogSource.LoadAsync(AddressOf AppendLog, CancellationToken.None)
            For Each card In _cards
                card.SetItems(NetSpeedTestService.ItemsForOrigin(items, card.Origin))
            Next
            SetStatus($"Pronto. {items.Count} item(ns) nos catálogos. Escolha as origens e as conexões e clique em Iniciar.")
        Catch ex As Exception
            AppendLog($"ERRO ao carregar catálogos: {ex.Message}")
            SetStatus("Catálogos não carregados. Verifique a conexão e reabra.")
        Finally
            SetBusy(False)
        End Try
    End Sub

    Private Async Sub OnStartClick(sender As Object, e As EventArgs)
        If _cts IsNot Nothing Then Return
        Dim selectedCards = _cards.Where(Function(c) c.IsSelected AndAlso c.SelectedItem IsNot Nothing).ToList()
        Dim connections = _connectionChecks.Where(Function(c) c.Checked).Select(Function(c) CInt(c.Tag)).ToList()
        If selectedCards.Count = 0 OrElse connections.Count = 0 Then
            MessageBox.Show("Escolha pelo menos uma origem e uma quantidade de conexões.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information)
            Return
        End If

        Dim duration = TimeSpan.FromSeconds(CType(_comboDuration.SelectedItem, DurationOption).Seconds)
        Dim totalSteps = selectedCards.Count * connections.Count
        _cts = New CancellationTokenSource()
        _results.Clear()
        _logBox.Clear()
        _measureBar.Value = 0
        _overallBar.Value = 0
        For Each card In _cards
            card.ResetResults()
        Next
        SetBusy(True)
        AppendLog($"Início: {selectedCards.Count} origem(ns) × {String.Join("/", connections)} conexões, {duration.TotalSeconds:N0} s cada (≈ {totalSteps * duration.TotalSeconds / 60:N1} min). Nada é gravado em disco.{If(_chkSwapSlow.Checked, " Troca de conexão lenta ligada.", "")} User-Agent: {HttpIdentity.AppToken}")

        Dim stepIndex = 0
        Try
            For Each card In selectedCards
                Dim item = card.SelectedItem
                card.SetMeasuring(Nothing)
                SetStatus($"{card.Title}: resolvendo o link de {item.Name}...")
                Dim link As (SourceUrl As String, FinalUrl As String)
                Try
                    link = Await _service.ResolveLinkAsync(item.FileUrl, _cts.Token)
                Catch ex As OperationCanceledException When _cts.IsCancellationRequested
                    Throw
                Catch ex As Exception
                    AppendLog($"{card.Title}: link não resolvido ({ex.Message}).")
                    card.SetLinkFailure(ex.Message)
                    stepIndex += connections.Count
                    UpdateOverall(stepIndex, 0, totalSteps)
                    Continue For
                End Try
                Dim useFinalUrl = _chkFinalUrl.Checked
                Dim fileUrl = If(useFinalUrl, link.FinalUrl, link.SourceUrl)
                card.SetHost(New Uri(link.FinalUrl).Host)
                AppendLog($"{card.Title}: {item.Name} -> {New Uri(link.FinalUrl).Host} (pedindo {If(useFinalUrl, "o link final", "o link de origem")}: {New Uri(fileUrl).Host})")

                For Each count In connections
                    card.SetMeasuring(count)
                    Dim currentStep = stepIndex
                    Dim label = $"{card.Title} · {count} {If(count = 1, "conexão", "conexões")}"
                    SetStatus($"{label} · iniciando...")
                    _measureBar.Value = 0
                    ' A última leitura ao vivo pode chegar depois do resultado: ignorada.
                    Dim isLive = True
                    Dim live = New Progress(Of LiveReading)(
                        Sub(r)
                            If Not isLive Then Return
                            card.ShowLive(r, count)
                            Dim fraction = Math.Min(1.0R, r.Elapsed.TotalMilliseconds / Math.Max(1.0R, r.Duration.TotalMilliseconds))
                            _measureBar.Value = CInt(fraction * 1000)
                            UpdateOverall(currentStep, fraction, totalSteps)
                            SetStatus($"{label} · {r.Elapsed.TotalSeconds:N0}/{r.Duration.TotalSeconds:N0} s · agora {r.CurrentMiBps:N1} MiB/s · média {r.AverageMiBps:N1} MiB/s · {r.ActiveConnections}/{count} ativas")
                        End Sub)
                    Dim measurement = Await _service.MeasureAsync(item, fileUrl, count, duration, live, _cts.Token, _chkSwapSlow.Checked)
                    isLive = False
                    measurement.LinkMode = If(useFinalUrl, "final", "origem")
                    _results.Add(measurement)
                    card.ApplyMeasurement(measurement)
                    AppendLog(DescribeMeasurement(card.Title, measurement))
                    stepIndex += 1
                    UpdateOverall(stepIndex, 0, totalSteps)
                Next
                card.FinishOrigin()
            Next
            MarkBestOrigin()
            _measureBar.Value = _measureBar.Maximum
            _overallBar.Value = _overallBar.Maximum
        Catch ex As OperationCanceledException
            AppendLog("Teste cancelado pelo usuário.")
            SetStatus("Teste cancelado.")
            For Each card In _cards
                card.StopMeasuring()
            Next
        Catch ex As Exception
            AppendLog($"ERRO: {ex.Message}")
            SetStatus($"Erro: {ex.Message}")
        Finally
            _cts.Dispose()
            _cts = Nothing
            SetBusy(False)
        End Try
    End Sub

    Private Sub OnCancelClick(sender As Object, e As EventArgs)
        If _cts Is Nothing Then Return
        AppendLog("Cancelamento solicitado.")
        _btnCancel.Enabled = False
        _cts.Cancel()
    End Sub

    Private Sub UpdateOverall(completedSteps As Integer, currentFraction As Double, totalSteps As Integer)
        If totalSteps <= 0 Then Return
        _overallBar.Value = CInt(Math.Min(1.0R, (completedSteps + currentFraction) / totalSteps) * 1000)
    End Sub

    Private Sub MarkBestOrigin()
        Dim ranking = _cards.Select(Function(c) (Card:=c, Best:=c.BestMeasurement)).Where(Function(x) x.Best IsNot Nothing).
                             OrderByDescending(Function(x) x.Best.MiBps).ToList()
        If ranking.Count = 0 Then
            SetStatus("Concluído. Nenhuma origem completou a medição.")
            Return
        End If
        ranking(0).Card.MarkBest()
        Dim winner = ranking(0)
        Dim summary = $"Concluído. Mais rápida: {winner.Card.Title} com {winner.Best.Connections} {If(winner.Best.Connections = 1, "conexão", "conexões")} ({winner.Best.MiBps:N1} MiB/s)."
        SetStatus(summary)
        AppendLog(summary & " Resultado informativo.")
    End Sub

    Private Shared Function DescribeMeasurement(title As String, m As SpeedMeasurement) As String
        If Not m.IsOk Then Return $"{title} · {m.Connections} conexão(ões): {m.Status}"
        Dim text = $"{title} · {m.Connections} conexão(ões): {m.MiBps:N2} MiB/s (mín {m.MinMiBps:N1}, máx {m.MaxMiBps:N1}), 1º byte {m.FirstByteMs} ms, {m.ActiveConnections}/{m.Connections} ativas, {m.TotalBytes / 1048576.0R:N0} MiB lidos."
        If Not String.IsNullOrWhiteSpace(m.Notes) Then text &= " " & m.Notes
        Return text
    End Function

    Private Sub OnExportClick(sender As Object, e As EventArgs)
        If _results.Count = 0 Then Return
        Using dialog As New SaveFileDialog() With {.Filter = "CSV (*.csv)|*.csv", .FileName = $"netspeedtest_{DateTime.Now:yyyyMMdd_HHmmss}.csv"}
            If dialog.ShowDialog(Me) <> DialogResult.OK Then Return
            Dim sb As New StringBuilder()
            sb.AppendLine("data;origem;item;arquivo;host;conexoes;status;mibps_media;mibps_min;mibps_max;primeiro_byte_ms;conexoes_ativas;bytes;segundos;mibps_por_segundo;link;troca_lenta;trocas;observacoes")
            For Each m In _results
                sb.AppendLine(String.Join(";", {
                    m.TestedAt.ToString("yyyy-MM-dd HH:mm:ss"), m.Origin.ToString(), m.ItemName, m.FileName, m.Host, m.Connections.ToString(), m.Status,
                    m.MiBps.ToString("N2"), m.MinMiBps.ToString("N2"), m.MaxMiBps.ToString("N2"), m.FirstByteMs.ToString(), m.ActiveConnections.ToString(),
                    m.TotalBytes.ToString(), m.Seconds.ToString("N1"), String.Join(" ", m.PerSecondMiBps.Select(Function(v) v.ToString("N1"))), m.LinkMode, If(m.SwapSlowEnabled, "sim", "nao"), m.SlowSwaps.ToString(), m.Notes
                }.Select(Function(v) If(v, String.Empty).Replace(";", ","))))
            Next
            File.WriteAllText(dialog.FileName, sb.ToString(), New UTF8Encoding(True))
            AppendLog($"CSV exportado: {dialog.FileName}")
        End Using
    End Sub

    Private Sub SetBusy(isBusy As Boolean, Optional allowCancel As Boolean = True)
        _btnStart.Enabled = Not isBusy
        _btnCancel.Enabled = isBusy AndAlso allowCancel
        _btnExport.Enabled = Not isBusy AndAlso _results.Count > 0
        _comboDuration.Enabled = Not isBusy
        For Each check In _connectionChecks
            check.Enabled = Not isBusy
        Next
        _chkFinalUrl.Enabled = Not isBusy
        _chkSwapSlow.Enabled = Not isBusy
        For Each card In _cards
            card.SetEditable(Not isBusy)
        Next
    End Sub

    Private Sub SetStatus(text As String)
        _statusLabel.Text = text
    End Sub

    Private Sub AppendLog(message As String)
        If String.IsNullOrWhiteSpace(message) Then Return
        _logBox.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}")
    End Sub

    Private Class DurationOption
        Public Sub New(seconds As Integer)
            Me.Seconds = seconds
        End Sub
        Public ReadOnly Property Seconds As Integer
        Public Overrides Function ToString() As String
            Return $"{Seconds} s"
        End Function
    End Class
End Class

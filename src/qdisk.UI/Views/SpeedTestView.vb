' ─────────────────────────────────────────────────────────────────────────────
' qdisk  |  UI / Views  |  SpeedTestView.vb
' Aba informativa "Teste de velocidade": mede o Dropbox e o SharePoint por tempo fixo
' (10/20/30/60 s) com 1, 4 e 8 conexões, sem gravar em disco, com progresso ao vivo.
' Medição: Core/Services/NetSpeedTestService.vb. Cartão e gráfico: Theme/UiSpeedTest.vb.
' Bancada para evoluir o teste: tools/Quallit.NetSpeedTest (usa os mesmos arquivos).
'
' © Quallit — Licenciado sob GPL-3.0-or-later.
' ─────────────────────────────────────────────────────────────────────────────

Imports System.Drawing
Imports System.IO
Imports System.Text
Imports System.Threading
Imports System.Threading.Tasks
Imports System.Windows.Forms
Imports qdisk.Core
Imports qdisk.Core.Models
Imports qdisk.Core.Services
Imports qdisk.UI.Theme

Namespace Views
    Public Class SpeedTestView
        Inherits UserControl

        Private Shared ReadOnly ConnectionOptions As Integer() = {1, 4, 8}

        Private ReadOnly _catalogService As CatalogService
        Private ReadOnly _service As NetSpeedTestService
        Private ReadOnly _logService As LogService
        Private ReadOnly _results As New List(Of SpeedMeasurement)()
        Private ReadOnly _btnStart As Button
        Private ReadOnly _btnCancel As Button
        Private ReadOnly _btnExport As Button
        Private ReadOnly _comboDuration As ComboBox
        Private ReadOnly _connectionChecks As New List(Of CheckBox)()
        Private ReadOnly _cards As New List(Of OriginCard)()
        Private ReadOnly _statusLabel As Label
        Private ReadOnly _measureBar As ProgressBar
        Private ReadOnly _overallBar As ProgressBar
        Private ReadOnly _logBox As TextBox
        Private ReadOnly _toolTip As New ToolTip()
        Private _cts As CancellationTokenSource

        Public Sub New()
            _catalogService = New CatalogService()
            _service = New NetSpeedTestService()
            _logService = New LogService()
            Dock = DockStyle.Fill
            BackColor = Color.White

            ' ── Barra de comandos: comandos à esquerda, Exportar CSV (só ícone) à direita ──
            Dim topBar = New Panel() With {.Dock = DockStyle.Top, .Height = 56, .Padding = New Padding(16, 12, 16, 12), .BackColor = Color.White}
            Dim topPanel = New FlowLayoutPanel() With {.Dock = DockStyle.Fill, .Padding = New Padding(0), .Margin = New Padding(0), .BackColor = Color.White, .WrapContents = False}
            _btnStart = New Button() With {.Text = "Iniciar"}
            _btnCancel = New Button() With {.Text = "Cancelar", .Enabled = False}
            UiStatus.StyleAutoButton(_btnStart, ButtonKind.Primary, UiTheme.Icon16(Me, "play", "white"))
            UiStatus.StyleAutoButton(_btnCancel, ButtonKind.Secondary, UiTheme.Icon16(Me, "x", "text"))
            _btnStart.Margin = New Padding(0, 0, UiTheme.ButtonGap, 0)
            _btnCancel.Margin = New Padding(0)
            AddHandler _btnStart.Click, AddressOf OnStartClick
            AddHandler _btnCancel.Click, AddressOf OnCancelClick

            Dim durationLabel = CreateBarLabel("Duração:")
            _comboDuration = New ComboBox() With {.DropDownStyle = ComboBoxStyle.DropDownList, .Width = 64, .Margin = New Padding(0)}
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

            _btnExport = New Button() With {.Text = String.Empty, .Enabled = False, .Dock = DockStyle.Fill, .Margin = New Padding(0)}
            UiTheme.StyleButton(_btnExport, ButtonKind.Secondary, UiTheme.Icon16(Me, "file", "text"))
            _btnExport.ImageAlign = ContentAlignment.MiddleCenter
            _toolTip.SetToolTip(_btnExport, "Exportar CSV")
            AddHandler _btnExport.Click, AddressOf OnExportClick
            Dim exportHost = New Panel() With {.Dock = DockStyle.Right, .Width = 34, .BackColor = Color.White}
            exportHost.Controls.Add(_btnExport)
            topBar.Controls.Add(topPanel)
            topBar.Controls.Add(exportHost)

            ' ── Cartões das origens ─────────────────────────────────────────
            Dim contentPanel = New Panel() With {.Dock = DockStyle.Fill, .Padding = New Padding(16, 0, 16, 0), .BackColor = Color.White}
            Dim cardsPanel = New TableLayoutPanel() With {.Dock = DockStyle.Fill, .ColumnCount = 2, .RowCount = 1, .BackColor = Color.White, .Margin = New Padding(0)}
            cardsPanel.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 50.0F))
            cardsPanel.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 50.0F))
            cardsPanel.RowStyles.Add(New RowStyle(SizeType.Percent, 100.0F))
            Dim origins = {DownloadOrigin.Dropbox, DownloadOrigin.SharePoint}
            For i = 0 To origins.Length - 1
                Dim card = New OriginCard(origins(i), ConnectionOptions)
                card.Panel.Margin = If(i = 0, New Padding(0, 0, 6, 0), New Padding(6, 0, 0, 0))
                _cards.Add(card)
                cardsPanel.Controls.Add(card.Panel, i, 0)
            Next
            contentPanel.Controls.Add(cardsPanel)

            ' ── Progresso: status, medição atual e teste completo ──────────
            Dim progressPanel = New TableLayoutPanel() With {.Dock = DockStyle.Bottom, .Height = 68, .Padding = New Padding(16, 6, 16, 0), .ColumnCount = 2, .RowCount = 3, .BackColor = Color.White}
            progressPanel.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, 104.0F))
            progressPanel.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100.0F))
            progressPanel.RowStyles.Add(New RowStyle(SizeType.Absolute, 24.0F))
            progressPanel.RowStyles.Add(New RowStyle(SizeType.Absolute, 18.0F))
            progressPanel.RowStyles.Add(New RowStyle(SizeType.Absolute, 18.0F))
            _statusLabel = New Label() With {.Text = "Pronto.", .Dock = DockStyle.Fill, .Font = UiTheme.FontBold, .ForeColor = UiTheme.MainText, .TextAlign = ContentAlignment.MiddleLeft, .Margin = New Padding(0), .AutoEllipsis = True}
            progressPanel.Controls.Add(_statusLabel, 0, 0)
            progressPanel.SetColumnSpan(_statusLabel, 2)
            _measureBar = New ProgressBar() With {.Dock = DockStyle.Fill, .Maximum = 1000, .Style = ProgressBarStyle.Continuous, .Margin = New Padding(0, 3, 0, 3)}
            _overallBar = New ProgressBar() With {.Dock = DockStyle.Fill, .Maximum = 1000, .Style = ProgressBarStyle.Continuous, .Margin = New Padding(0, 3, 0, 3)}
            progressPanel.Controls.Add(CreateProgressCaption("Medição atual"), 0, 1)
            progressPanel.Controls.Add(_measureBar, 1, 1)
            progressPanel.Controls.Add(CreateProgressCaption("Teste completo"), 0, 2)
            progressPanel.Controls.Add(_overallBar, 1, 2)

            ' ── Log ─────────────────────────────────────────────────────────
            Dim logPanel = New Panel() With {.Dock = DockStyle.Bottom, .Height = 72, .Padding = New Padding(16, 6, 16, 12), .BackColor = Color.White}
            _logBox = New TextBox() With {.Dock = DockStyle.Fill, .Multiline = True, .ReadOnly = True, .ScrollBars = ScrollBars.Vertical, .BorderStyle = BorderStyle.None, .Font = New Font("Consolas", 8.5F), .BackColor = UiTheme.LogBack}
            Dim logInner As Panel = Nothing
            Dim logHost = UiTheme.CreateBorderedPanel(UiTheme.BorderLight, UiTheme.LogBack, logInner)
            logHost.Dock = DockStyle.Fill
            logInner.Padding = New Padding(8, 4, 4, 2)
            logInner.Controls.Add(_logBox)
            logPanel.Controls.Add(logHost)

            Controls.Add(contentPanel)
            Controls.Add(progressPanel)
            Controls.Add(logPanel)
            Controls.Add(topBar)
            AddHandler Load, AddressOf OnSpeedTestViewLoad
        End Sub

        Private Shared Function CreateBarLabel(text As String) As Label
            Return New Label() With {.Text = text, .AutoSize = True, .Margin = New Padding(10, 8, 6, 0)}
        End Function

        Private Shared Function CreateProgressCaption(text As String) As Label
            Return New Label() With {.Text = text, .Dock = DockStyle.Fill, .ForeColor = UiTheme.MetricText, .TextAlign = ContentAlignment.MiddleLeft, .Margin = New Padding(0)}
        End Function

        Private Sub OnSpeedTestViewLoad(sender As Object, e As EventArgs)
            RemoveHandler Load, AddressOf OnSpeedTestViewLoad
            Dim items = LoadLocalCatalogItems()
            FillCards(items)
            SetStatus($"Pronto. {items.Count} item(ns) nos catálogos. Escolha as origens e as conexões e clique em Iniciar.")
        End Sub

        Private Sub FillCards(items As IReadOnlyList(Of CatalogItem))
            For Each card In _cards
                card.SetItems(NetSpeedTestService.ItemsForOrigin(items, card.Origin))
            Next
        End Sub

        Private Async Sub OnStartClick(sender As Object, e As EventArgs)
            If _cts IsNot Nothing Then Return
            Dim connections = _connectionChecks.Where(Function(c) c.Checked).Select(Function(c) CInt(c.Tag)).ToList()
            If Not _cards.Any(Function(c) c.IsSelected) OrElse connections.Count = 0 Then
                MessageBox.Show("Escolha pelo menos uma origem e uma quantidade de conexões.", "Teste de velocidade", MessageBoxButtons.OK, MessageBoxIcon.Information)
                Return
            End If

            _cts = New CancellationTokenSource()
            _results.Clear()
            _logBox.Clear()
            _measureBar.Value = 0
            _overallBar.Value = 0
            SetBusy(True)
            Dim duration = TimeSpan.FromSeconds(CType(_comboDuration.SelectedItem, DurationOption).Seconds)
            Try
                ' Catálogos atualizados antes do teste; mantém o item escolhido em cada cartão.
                SetStatus("Atualizando catálogos...")
                Dim chosen = _cards.ToDictionary(Function(c) c.Origin, Function(c) c.SelectedItem?.Name)
                Dim items = Await LoadCatalogItemsAsync(_cts.Token)
                FillCards(items)
                For Each card In _cards
                    card.SelectItemByName(chosen(card.Origin))
                    card.ResetResults()
                Next
                Dim selectedCards = _cards.Where(Function(c) c.IsSelected AndAlso c.SelectedItem IsNot Nothing).ToList()
                If selectedCards.Count = 0 Then
                    SetStatus("Nenhum item de Dropbox ou SharePoint nos catálogos deste perfil.")
                    Return
                End If

                Dim totalSteps = selectedCards.Count * connections.Count
                AppendLog($"Início: {selectedCards.Count} origem(ns) × {String.Join("/", connections)} conexões, {duration.TotalSeconds:N0} s cada (≈ {totalSteps * duration.TotalSeconds / 60:N1} min). Nada é gravado em disco.")
                Await _logService.WriteAsync($"SPEED_TEST | INICIO | Modelo=tempo | Duracao={duration.TotalSeconds:N0}s | Conexoes={String.Join("/", connections)} | Origens={String.Join("/", selectedCards.Select(Function(c) c.Title))}")

                Dim stepIndex = 0
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
                        _logService.WriteAsync($"SPEED_TEST | LINK_ERRO | {card.Title} | {item.Name} | {ex.Message}").GetAwaiter().GetResult()
                        card.SetLinkFailure(ex.Message)
                        stepIndex += connections.Count
                        UpdateOverall(stepIndex, 0, totalSteps)
                        Continue For
                    End Try
                    ' Cada conexão pede o link da página e segue os redirecionamentos (melhor resultado na bancada).
                    card.SetHost(New Uri(link.FinalUrl).Host)
                    AppendLog($"{card.Title}: {item.Name} -> {New Uri(link.FinalUrl).Host}")

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
                                SetStatus($"{label} · {r.Elapsed.TotalSeconds:N0}/{r.Duration.TotalSeconds:N0} s · agora {r.CurrentMiBps:N1} MiB/s · {r.ActiveConnections}/{count} ativas")
                            End Sub)
                        Dim measurement = Await _service.MeasureAsync(item, link.SourceUrl, count, duration, live, _cts.Token,
                                                                     swapSlowConnections:=SlowConnectionPolicy.AppliesTo(New Uri(link.FinalUrl)))
                        isLive = False
                        measurement.LinkMode = "origem"
                        _results.Add(measurement)
                        card.ApplyMeasurement(measurement)
                        AppendLog(DescribeMeasurement(card.Title, measurement))
                        Await _logService.WriteAsync($"SPEED_TEST | MEDICAO | {card.Title} | {item.Name} | Host={measurement.Host} | Conexoes={count} | Status={measurement.Status} | MiBps={measurement.MiBps:N2} | Min={measurement.MinMiBps:N2} | Max={measurement.MaxMiBps:N2} | Ativas={measurement.ActiveConnections} | PrimeiroByteMs={measurement.FirstByteMs} | Bytes={measurement.TotalBytes} | {measurement.Notes}")
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
                _logService.WriteAsync("SPEED_TEST | CANCELADO").GetAwaiter().GetResult()
            Catch ex As Exception
                AppendLog($"ERRO: {ex.Message}")
                SetStatus($"Erro: {ex.Message}")
                _logService.WriteAsync($"SPEED_TEST | ERRO | {ex.Message}").GetAwaiter().GetResult()
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
            Dim winner = ranking(0)
            winner.Card.MarkBest()
            Dim summary = $"Concluído. Mais rápida: {winner.Card.Title} com {winner.Best.Connections} {If(winner.Best.Connections = 1, "conexão", "conexões")} ({winner.Best.MiBps:N1} MiB/s)."
            SetStatus(summary)
            AppendLog(summary & " Resultado informativo.")
            _logService.WriteAsync($"SPEED_TEST | OK | Melhor={winner.Card.Title} | Conexoes={winner.Best.Connections} | MiBps={winner.Best.MiBps:N2}").GetAwaiter().GetResult()
        End Sub

        Private Shared Function DescribeMeasurement(title As String, m As SpeedMeasurement) As String
            If Not m.IsOk Then Return $"{title} · {m.Connections} conexão(ões): {m.Status}"
            Dim text = $"{title} · {m.Connections} conexão(ões): {m.MiBps:N2} MiB/s (mín {m.MinMiBps:N1}, máx {m.MaxMiBps:N1}), 1º byte {m.FirstByteMs} ms, {m.ActiveConnections}/{m.Connections} ativas."
            If Not String.IsNullOrWhiteSpace(m.Notes) Then text &= " " & m.Notes
            Return text
        End Function

        Private Sub OnExportClick(sender As Object, e As EventArgs)
            If _results.Count = 0 Then Return
            Using dialog As New SaveFileDialog() With {.Filter = "CSV (*.csv)|*.csv", .FileName = $"teste_velocidade_{DateTime.Now:yyyyMMdd_HHmmss}.csv"}
                If dialog.ShowDialog(Me) <> DialogResult.OK Then Return
                Dim sb As New StringBuilder()
                sb.AppendLine("data;origem;item;arquivo;host;conexoes;status;mibps_media;mibps_min;mibps_max;primeiro_byte_ms;conexoes_ativas;bytes;segundos;mibps_por_segundo;link;observacoes")
                For Each m In _results
                    sb.AppendLine(String.Join(";", {
                        m.TestedAt.ToString("yyyy-MM-dd HH:mm:ss"), m.Origin.ToString(), m.ItemName, m.FileName, m.Host, m.Connections.ToString(), m.Status,
                        m.MiBps.ToString("N2"), m.MinMiBps.ToString("N2"), m.MaxMiBps.ToString("N2"), m.FirstByteMs.ToString(), m.ActiveConnections.ToString(),
                        m.TotalBytes.ToString(), m.Seconds.ToString("N1"), String.Join(" ", m.PerSecondMiBps.Select(Function(v) v.ToString("N1"))), m.LinkMode, m.Notes
                    }.Select(Function(v) If(v, String.Empty).Replace(";", ","))))
                Next
                File.WriteAllText(dialog.FileName, sb.ToString(), New UTF8Encoding(True))
                AppendLog($"CSV exportado: {dialog.FileName}")
            End Using
        End Sub

        Private Sub SetBusy(isBusy As Boolean)
            _btnStart.Enabled = Not isBusy
            _btnCancel.Enabled = isBusy
            _btnExport.Enabled = Not isBusy AndAlso _results.Count > 0
            _comboDuration.Enabled = Not isBusy
            For Each check In _connectionChecks
                check.Enabled = Not isBusy
            Next
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

        ' ── Catálogos do perfil + Ferramentas (igual à versão anterior da aba) ──
        Private Async Function LoadCatalogItemsAsync(cancellationToken As CancellationToken) As Task(Of IReadOnlyList(Of CatalogItem))
            Dim toolsPath As String = Nothing
            Dim profilePath As String = Nothing
            Dim profileUrl As String = Nothing
            Dim profileFallbackUrl As String = Nothing
            ResolveCatalogPaths(toolsPath, profilePath, profileUrl, profileFallbackUrl)

            Try
                Dim results = Await _catalogService.RefreshCatalogsFromRemoteDetailedAsync(
                    cancellationToken,
                    New CatalogService.CatalogSource(profileUrl, profilePath, profileFallbackUrl),
                    New CatalogService.CatalogSource(CatalogService.RemoteToolsCatalogUrl, toolsPath, CatalogService.FallbackToolsCatalogUrl))
                For Each result In results
                    If result.Success Then
                        If result.Changed Then
                            Await _logService.WriteAsync($"SPEEDTEST | CATALOG | OK | Url={result.RemoteUrl} | Local={result.LocalPath} | Itens={result.ItemCount}")
                        Else
                            Await _logService.WriteAsync($"SPEEDTEST | CATALOG | SEM_ALTERACAO | Url={result.RemoteUrl} | Local={result.LocalPath} | Itens={result.ItemCount}")
                        End If
                    Else
                        Await _logService.WriteAsync($"SPEEDTEST | CATALOG | ERRO | Url={result.RemoteUrl} | Local={result.LocalPath} | {result.ErrorMessage}")
                    End If
                Next
            Catch ex As OperationCanceledException
                Throw
            Catch ex As Exception
                _logService.WriteAsync($"SPEEDTEST | CATALOG | FALLBACK_LOCAL | {ex.Message}").GetAwaiter().GetResult()
            End Try

            Return _catalogService.LoadAndMerge(profilePath, toolsPath)
        End Function

        Private Function LoadLocalCatalogItems() As IReadOnlyList(Of CatalogItem)
            Dim toolsPath As String = Nothing
            Dim profilePath As String = Nothing
            Dim profileUrl As String = Nothing
            Dim profileFallbackUrl As String = Nothing
            ResolveCatalogPaths(toolsPath, profilePath, profileUrl, profileFallbackUrl)
            Return _catalogService.LoadAndMerge(profilePath, toolsPath)
        End Function

        Private Shared Sub ResolveCatalogPaths(ByRef toolsPath As String, ByRef profilePath As String, ByRef profileUrl As String, ByRef profileFallbackUrl As String)
            Dim dataDir = AppPaths.DataDir
            toolsPath = Path.Combine(dataDir, CatalogService.ToolsCatalogFileName)
            Dim profile = TokenManager.GetUserProfile()
            profilePath = Path.Combine(dataDir, CatalogService.GetProfileCatalogFileName(profile))
            profileUrl = CatalogService.GetProfileRemoteCatalogUrl(profile)
            profileFallbackUrl = CatalogService.GetProfileFallbackCatalogUrl(profile)
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
End Namespace

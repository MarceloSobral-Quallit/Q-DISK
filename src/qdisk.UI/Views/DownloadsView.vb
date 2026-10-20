' ─────────────────────────────────────────────────────────────────────────────
' qdisk  |  UI / Views  |  DownloadsView.vb
' View de downloads do catálogo de imagens com progresso em tempo real.
'
' © Quallit — Licenciado sob GPL-3.0-or-later.
' ─────────────────────────────────────────────────────────────────────────────

Imports System.Collections.Generic
Imports System.Drawing
Imports System.Diagnostics
Imports System.IO
Imports System.Net.Http
Imports System.Text.RegularExpressions
Imports System.Text.Json
Imports System.Threading
Imports System.Windows.Forms
Imports qdisk.UI.Theme
Imports qdisk.Core
Imports qdisk.Core.Interfaces
Imports qdisk.Core.Models
Imports qdisk.Core.Services

Namespace Views
    Partial Public Class DownloadsView
        Inherits UserControl

        Private Const SelectionColumnName As String = "Selecionar"
        ' HttpClient compartilhado — evita esgotamento de sockets por instâncias descartadas.
        Private Shared ReadOnly _md5Client As HttpClient = CreateMd5Client()
        Private Shared Function CreateMd5Client() As HttpClient
            Dim c As New HttpClient() With {.Timeout = TimeSpan.FromSeconds(20)}
            HttpIdentity.Apply(c.DefaultRequestHeaders, "QuallitDownloader/1.0")
            Return c
        End Function
        Private ReadOnly _catalogService As CatalogService
        Private ReadOnly _hashService As HashService
        Private ReadOnly _settingsService As SettingsService
        Private _settings As AppSettings
        Private ReadOnly _md5StatusByName As Dictionary(Of String, Boolean?)
        Private ReadOnly _md5CacheByName As Dictionary(Of String, String)
        Private ReadOnly _downloadStatusByName As Dictionary(Of String, Boolean)
        Private ReadOnly _statusOkImage As Image
        Private ReadOnly _statusErrorImage As Image
        Private ReadOnly _statusUnknownImage As Image
        Private ReadOnly _downloadPathByName As Dictionary(Of String, String)
        Private ReadOnly _statusStoragePath As String
        Private _currentItems As List(Of CatalogItem)
        Private _downloadCts As CancellationTokenSource
        Private ReadOnly _logService As LogService
        Private _remoteCatalogRefreshStarted As Boolean

        Public Sub New()
            InitializeComponent()
            _catalogService = New CatalogService()
            _hashService = New HashService()
            _settingsService = New SettingsService()
            _settings = _settingsService.Load()
            _logService = New LogService()
            _md5StatusByName = New Dictionary(Of String, Boolean?)(StringComparer.OrdinalIgnoreCase)
            _md5CacheByName = New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase)
            _downloadStatusByName = New Dictionary(Of String, Boolean)(StringComparer.OrdinalIgnoreCase)
            _statusOkImage = CreateCheckImage(Color.FromArgb(40, 167, 69))
            _statusErrorImage = CreateErrorImage(Color.FromArgb(220, 53, 69))
            _statusUnknownImage = CreateUnknownImage(Color.FromArgb(108, 117, 125))
            _downloadPathByName = New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase)
            _statusStoragePath = GetStatusStoragePath()
            _currentItems = New List(Of CatalogItem)()
            LoadStatusFromDisk()
            Dim gifPath = Path.Combine(AppPaths.AssetsDir, "run.gif")
            If File.Exists(gifPath) Then
                _busyGif.Image = Image.FromFile(gifPath)
            End If
            ConfigureGridColumns()
            ApplyRedesign()

            LoadCatalog(refreshDownloadStatus:=False)
            UpdateActionState()
        End Sub

        Protected Overrides Async Sub OnLoad(e As EventArgs)
            MyBase.OnLoad(e)
            If _remoteCatalogRefreshStarted Then
                Return
            End If

            _remoteCatalogRefreshStarted = True
            Try
                Await RefreshRemoteCatalogsAndReloadAsync(showStatus:=False, refreshDownloadStatus:=False)
            Catch ex As Exception
                _logService.WriteAsync($"CATALOG | REMOTE | STARTUP_FALLBACK_LOCAL | {ex.Message}").GetAwaiter().GetResult()
            End Try
        End Sub

        ''' <summary>Redesenho da tela (HANDOFF 4.1). So aparencia e posicao; a logica nao muda.</summary>
        Private Sub ApplyRedesign()
            ' Barra de acoes: Baixar (principal), Validar integridade, Atualizar ... Abrir pasta
            UiStatus.StyleAutoButton(_btnDownload, ButtonKind.Primary, UiTheme.Icon16(Me, "download", "white"))
            UiStatus.StyleAutoButton(_btnValidate, ButtonKind.Secondary, UiTheme.Icon16(Me, "shield", "text"))
            UiStatus.StyleAutoButton(_btnRefresh, ButtonKind.Secondary, UiTheme.Icon16(Me, "refresh", "text"))
            UiStatus.StyleAutoButton(_btnOpenFolder, ButtonKind.Secondary, UiTheme.Icon16(Me, "folder", "text"))
            UiStatus.StyleAutoButton(_btnCancelDownload, ButtonKind.Secondary, UiTheme.Icon16(Me, "x", "text"), 104)

            Dim actions = New TableLayoutPanel() With {.Dock = DockStyle.Fill, .ColumnCount = 5, .RowCount = 1, .Margin = New Padding(0), .Padding = New Padding(0)}
            For i = 0 To 2
                actions.ColumnStyles.Add(New ColumnStyle(SizeType.AutoSize))
            Next
            actions.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100.0F))
            actions.ColumnStyles.Add(New ColumnStyle(SizeType.AutoSize))
            actions.RowStyles.Add(New RowStyle(SizeType.Percent, 100.0F))
            ActionsPanel.Controls.Clear()
            For Each b In New Button() {_btnDownload, _btnValidate, _btnRefresh, _btnOpenFolder}
                b.Margin = New Padding(0, 0, If(b Is _btnOpenFolder, 0, UiTheme.ButtonGap), 0)
                b.Anchor = AnchorStyles.Left
            Next
            actions.Controls.Add(_btnDownload, 0, 0)
            actions.Controls.Add(_btnValidate, 1, 0)
            actions.Controls.Add(_btnRefresh, 2, 0)
            actions.Controls.Add(_btnOpenFolder, 4, 0)
            UiStatus.ReplaceInParent(ActionsPanel, actions)

            LayoutRoot.Padding = New Padding(16, 16, 16, 8)
            For Each c As Control In New Control() {LowerPanel, StatusPanel, ProgressPanel}
                c.Margin = New Padding(0)
            Next
            LayoutRoot.RowStyles(0).Height = 44.0F

            ' Grade: estilo do tema dentro de painel com borda; "?" quando o status nao foi verificado
            UiTheme.StyleGrid(_grid)
            Dim gridInner As Panel = Nothing
            Dim gridBox = UiTheme.CreateBorderedPanel(UiTheme.BorderLight, Color.White, gridInner)
            gridBox.Dock = DockStyle.Fill
            UiStatus.ReplaceInParent(_grid, gridBox)
            gridInner.Controls.Add(_grid)
            For Each column As DataGridViewColumn In _grid.Columns
                Select Case column.HeaderText
                    Case "" : column.Width = 36
                    Case "Grupo" : column.Width = 112
                    Case "Download", "SHA-256"
                        column.Width = 84
                        column.HeaderCell.Style.Alignment = DataGridViewContentAlignment.MiddleCenter
                        column.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter
                        column.DefaultCellStyle.NullValue = _statusUnknownImage
                        Dim imageColumn = TryCast(column, DataGridViewImageColumn)
                        If imageColumn IsNot Nothing Then imageColumn.ImageLayout = DataGridViewImageCellLayout.Normal
                End Select
            Next
            AddHandler _grid.EnabledChanged, Sub() UiTheme.SetGridBusyLook(_grid, Not _grid.Enabled)
            ' Descricao do catalogo e "Como gravar" ao passar o mouse sobre a imagem.
            AddHandler _grid.CellToolTipTextNeeded,
                Sub(sender As Object, e As DataGridViewCellToolTipTextNeededEventArgs)
                    If e.RowIndex < 0 OrElse e.RowIndex >= _grid.Rows.Count Then Return
                    Dim catalogItem = TryCast(_grid.Rows(e.RowIndex).DataBoundItem, CatalogItem)
                    Dim text = ImageGuidance.Describe(catalogItem)
                    ' Imagem baixada com versao nova no catalogo: avisa na dica da linha.
                    If catalogItem IsNot Nothing AndAlso HasNewVersionInCatalog(catalogItem) Then
                        text = "Versão nova no catálogo: baixe de novo." & If(text.Length > 0, Environment.NewLine & text, String.Empty)
                    End If
                    If text.Length > 0 Then e.ToolTipText = text
                End Sub

            ' Legenda abaixo da grade
            Dim legend = New FlowLayoutPanel() With {.Dock = DockStyle.Fill, .WrapContents = False, .Margin = New Padding(0), .Padding = New Padding(0, 6, 0, 0)}
            For Each entry In {(_statusOkImage, "OK"), (_statusErrorImage, "Erro"), (_statusUnknownImage, "Não verificado")}
                legend.Controls.Add(New PictureBox() With {.Image = entry.Item1, .Size = New Size(16, 16), .SizeMode = PictureBoxSizeMode.CenterImage, .Margin = New Padding(0, 2, 4, 0)})
                legend.Controls.Add(New Label() With {.Text = entry.Item2, .AutoSize = True, .ForeColor = UiTheme.StatusText, .Margin = New Padding(0, 2, 16, 0)})
            Next
            LowerPanel.RowCount = 3
            LowerPanel.RowStyles.Clear()
            LowerPanel.RowStyles.Add(New RowStyle(SizeType.Percent, 100.0F))
            LowerPanel.RowStyles.Add(New RowStyle(SizeType.Absolute, 32.0F))
            LowerPanel.RowStyles.Add(New RowStyle(SizeType.Absolute, 96.0F))
            LowerPanel.Controls.Add(legend, 0, 1)
            LowerPanel.SetRow(StatusPanel, 2)

            ' Rodape: divisoria, "Selecionado:" + nome em negrito, linha de status, progresso + Cancelar
            StatusPanel.RowCount = 4
            StatusPanel.RowStyles.Clear()
            StatusPanel.RowStyles.Add(New RowStyle(SizeType.Absolute, 9.0F))
            StatusPanel.RowStyles.Add(New RowStyle(SizeType.Absolute, 24.0F))
            StatusPanel.RowStyles.Add(New RowStyle(SizeType.Absolute, 26.0F))
            StatusPanel.RowStyles.Add(New RowStyle(SizeType.Absolute, 36.0F))
            Dim divider = UiTheme.CreateDivider()
            divider.Dock = DockStyle.Top
            divider.Margin = New Padding(0, 0, 0, 8)
            StatusPanel.Controls.Add(divider, 0, 0)
            StatusPanel.SetRow(_selectionLabel, 1)
            StatusPanel.SetRow(_statusLabel, 2)
            StatusPanel.SetRow(ProgressPanel, 3)

            Dim selectionRow = New FlowLayoutPanel() With {.Dock = DockStyle.Fill, .WrapContents = False, .Margin = New Padding(0)}
            Dim selectionCaption = New Label() With {.Text = "Selecionado:", .AutoSize = True, .ForeColor = UiTheme.StatusText, .Margin = New Padding(0, 4, 2, 0)}
            Dim selectionName = New Label() With {.AutoSize = True, .ForeColor = UiTheme.MainText, .Font = UiTheme.FontBold, .Margin = New Padding(0, 4, 0, 0)}
            selectionRow.Controls.Add(selectionCaption)
            selectionRow.Controls.Add(selectionName)
            Dim syncSelection As Action = Sub()
                                              Dim t = _selectionLabel.Text
                                              Const prefix = "Selecionado: "
                                              selectionName.Text = If(t.StartsWith(prefix), t.Substring(prefix.Length), t)
                                          End Sub
            syncSelection()
            AddHandler _selectionLabel.TextChanged, Sub() syncSelection()
            UiStatus.ReplaceInParent(_selectionLabel, selectionRow)
            _selectionLabel.Visible = False
            Controls.Add(_selectionLabel)

            UiStatus.AttachStatusLine(_statusLabel, Me)
            Controls.Add(_statusLabel)

            ProgressPanel.ColumnCount = 3
            ProgressPanel.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, 112.0F))
            ProgressPanel.Padding = New Padding(0, 4, 0, 0)
            _progressBar.Dock = DockStyle.None
            _progressBar.Anchor = AnchorStyles.Left Or AnchorStyles.Right
            _progressBar.Height = 22
            _btnCancelDownload.Margin = New Padding(8, 0, 0, 0)
            _btnCancelDownload.Anchor = AnchorStyles.Right
            ProgressPanel.Controls.Add(_btnCancelDownload, 2, 0)
        End Sub

        Private Sub ConfigureGridColumns()
            If _grid Is Nothing Then
                Return
            End If

            _grid.RowHeadersVisible = False
            _grid.Columns.Clear()
            _grid.Columns.Add(New DataGridViewCheckBoxColumn() With {
                .HeaderText = "",
                .Name = SelectionColumnName,
                .Width = 20
            })
            _grid.Columns.Add(New DataGridViewTextBoxColumn() With {
                .HeaderText = "Nome",
                .DataPropertyName = "Name",
                .AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
                .MinimumWidth = 200
            })
            _grid.Columns.Add(New DataGridViewTextBoxColumn() With {
                .HeaderText = "Grupo",
                .DataPropertyName = "Group",
                .Width = 100
            })
            _grid.Columns.Add(New DataGridViewImageColumn() With {
                .HeaderText = "Download",
                .Name = "DownloadStatus",
                .Width = 70,
                .ImageLayout = DataGridViewImageCellLayout.Zoom
            })
            _grid.Columns.Add(New DataGridViewImageColumn() With {
                .HeaderText = "Integridade legado",
                .Name = "Md5Status",
                .Width = 60,
                .ImageLayout = DataGridViewImageCellLayout.Zoom,
                .Visible = False
            })
            _grid.Columns.Add(New DataGridViewImageColumn() With {
                .HeaderText = "SHA-256",
                .Name = "Sha256Status",
                .Width = 80,
                .ImageLayout = DataGridViewImageCellLayout.Zoom
            })
            _grid.Columns.Add(New DataGridViewTextBoxColumn() With {
                .HeaderText = "Arquivo",
                .DataPropertyName = "FileUrl",
                .AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
                .Visible = False
            })
            _grid.Columns.Add(New DataGridViewTextBoxColumn() With {
                .HeaderText = "Integridade legado",
                .DataPropertyName = "Md5Url",
                .AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
                .Visible = False
            })

            For Each column As DataGridViewColumn In _grid.Columns
                If Not String.Equals(column.Name, SelectionColumnName, StringComparison.OrdinalIgnoreCase) Then
                    column.ReadOnly = True
                End If
            Next
        End Sub

        Public Sub ReloadList()
            LoadCatalog(refreshDownloadStatus:=False)
        End Sub

        Private Async Sub OnRefreshClick(sender As Object, e As EventArgs)
            Try
                SetBusyState(True)
                _statusLabel.Text = "Atualizando lista..."
                Await _logService.WriteAsync("DOWNLOADS | ATUALIZAR_LISTA | INICIO")
                Await RefreshRemoteCatalogsAndReloadAsync(showStatus:=True, refreshDownloadStatus:=True)

                If _settings.AutoValidateMd5AfterCacheRefresh Then
                    Await ValidateAllCacheMd5FromStoredAsync()
                End If

                Await _logService.WriteAsync("DOWNLOADS | ATUALIZAR_LISTA | OK")
            Catch ex As Exception
                _statusLabel.Text = "Nao foi possivel atualizar online. Usando catalogo local."
                _logService.WriteAsync($"DOWNLOADS | ATUALIZAR_LISTA | FALLBACK_LOCAL | {ex.Message}").GetAwaiter().GetResult()
            Finally
                SetBusyState(False)
            End Try
        End Sub

        Private Sub OnOpenFolderClick(sender As Object, e As EventArgs)
            Directory.CreateDirectory(AppPaths.CacheDir)
            Process.Start(New ProcessStartInfo("explorer.exe", AppPaths.CacheDir) With {.UseShellExecute = True})
        End Sub

        Private Sub OnCancelDownloadClick(sender As Object, e As EventArgs)
            If _downloadCts Is Nothing Then Return
            _btnCancelDownload.Enabled = False
            _statusLabel.Text = "Cancelando download..."
            _downloadCts.Cancel()
        End Sub

        Private Async Sub OnDownloadClick(sender As Object, e As EventArgs)
            Dim item = GetSelectedItem()
            If item Is Nothing Then
                MessageBox.Show("Selecione um item na lista.", "Downloads", MessageBoxButtons.OK, MessageBoxIcon.Information)
                Return
            End If

            SetBusyState(True)
            _progressBar.Value = 0
            _statusLabel.Text = $"Baixando {item.Name}..."
            Await _logService.WriteAsync($"DOWNLOAD | INICIO | {item.Name}")

            Dim errorLog As String = Nothing
            _downloadCts = New CancellationTokenSource()

            Dim isMerging As Boolean = False

            Dim currentSettings = _settingsService.Load()
            Dim downloadService As IDownloadService =
                If(currentSettings.EnableMultiConnectionDownload,
                   New SegmentedDownloadService(currentSettings.MaxDownloadConnections,
                                                AddressOf ConfirmFallbackToSingleConnectionAsync,
                                                Sub()
                                                    isMerging = True
                                                    _progressBar.Value = 0
                                                    _statusLabel.Text = $"Todos os fragmentos baixados, unindo {item.Name}..."
                                                End Sub),
                   New DownloadService())

            Dim progress = New Progress(Of Integer)(
                Sub(value)
                    _progressBar.Value = value
                    If isMerging Then
                        _statusLabel.Text = $"Todos os fragmentos baixados, unindo {item.Name}... {value}%"
                        Return
                    End If
                    Dim segSvc = TryCast(downloadService, SegmentedDownloadService)
                    If segSvc IsNot Nothing AndAlso segSvc.LastRunUsedSegmentedMode AndAlso String.IsNullOrEmpty(segSvc.LastRunFallbackReason) Then
                        _statusLabel.Text = $"Baixando {item.Name}... ({segSvc.LastRunConnectionsUsed} conexões em uso)"
                    Else
                        _statusLabel.Text = $"Baixando {item.Name}..."
                    End If
                End Sub)

            Dim downloadTimer As Stopwatch = Nothing
            Dim partPath As String = Nothing
            Try
                ' Com fileName no catalogo, o arquivo final usa esse nome e o download vai para um .part.
                ' A imagem anterior so e substituida depois que o .part passa na verificacao.
                Dim hasCatalogFileName = Not String.IsNullOrWhiteSpace(item.FileName)
                Dim destination = If(hasCatalogFileName, GetCatalogCacheFilePath(item), GetCacheFilePath(item.FileUrl))
                Dim statusEntry = GetStoredStatusEntry(item, destination)
                Dim statusPath = If(statusEntry IsNot Nothing AndAlso Not String.IsNullOrWhiteSpace(statusEntry.Path), statusEntry.Path, destination)
                Dim statusExists = Not String.IsNullOrWhiteSpace(statusPath) AndAlso File.Exists(statusPath)
                Dim statusValid = (statusEntry IsNot Nothing AndAlso statusEntry.Downloaded AndAlso statusEntry.Md5Valid)
                ' Imagem ja na pasta: diz se e a mesma do catalogo, se o catalogo tem versao nova ou se nao ha verificacao.
                Dim overwriteCase = DownloadOverwriteCheck.Classify(statusExists, statusValid, statusEntry?.ImageSha256, item.Sha256)
                If overwriteCase <> DownloadOverwriteCase.NotDownloaded Then
                    Await _logService.WriteAsync($"DOWNLOAD | OVERWRITE_CHECK | {item.Name} | Caso={overwriteCase} | Status={(If(statusEntry IsNot Nothing, $"{statusEntry.Downloaded}/{statusEntry.Md5Valid}", "UNKNOWN"))}")
                    Dim question As String
                    Dim defaultButton = MessageBoxDefaultButton.Button1
                    Select Case overwriteCase
                        Case DownloadOverwriteCase.SameAsCatalog
                            question = $"A imagem {Path.GetFileName(statusPath)} já está baixada e confere com o catálogo (SHA-256 igual).{Environment.NewLine}{Environment.NewLine}Baixar de novo mesmo assim?"
                            defaultButton = MessageBoxDefaultButton.Button2
                        Case DownloadOverwriteCase.NewVersionInCatalog
                            question = $"Há versão nova desta imagem no catálogo (SHA-256 diferente do arquivo baixado).{Environment.NewLine}{Environment.NewLine}Baixar a versão nova? A imagem atual só é substituída depois que o download for conferido."
                        Case Else
                            question = $"Já existe {Path.GetFileName(statusPath)} na pasta, sem verificação registrada pelo app.{Environment.NewLine}{Environment.NewLine}Baixar e substituir? (Para só conferir o arquivo existente, use Validar integridade.)"
                    End Select
                    Dim overwrite = MessageBox.Show(question, "Downloads", MessageBoxButtons.YesNo, MessageBoxIcon.Question, defaultButton)
                    If overwrite <> DialogResult.Yes Then
                        _statusLabel.Text = "Download cancelado."
                        Await _logService.WriteAsync($"DOWNLOAD | CANCELADO | {item.Name} | Caso={overwriteCase}")
                        Return
                    End If
                End If
                downloadTimer = Stopwatch.StartNew()
                _btnCancelDownload.Enabled = True
                Dim downloadTarget = destination
                If hasCatalogFileName Then
                    partPath = destination & ".part"
                    If File.Exists(partPath) Then
                        File.Delete(partPath)
                    End If
                    downloadTarget = partPath
                End If
                Dim finalPath = Await downloadService.DownloadAsync(item.FileUrl, downloadTarget, progress, _downloadCts.Token)
                downloadTimer.Stop()
                _btnCancelDownload.Enabled = False
                Await _logService.WriteAsync($"DOWNLOAD | TEMPO | {item.Name} | {downloadTimer.ElapsedMilliseconds}ms")
                LogRequestRetries(item, downloadService)
                LogSlowSwaps(item, downloadService)

                Dim segmentedService = TryCast(downloadService, SegmentedDownloadService)
                If segmentedService IsNot Nothing Then
                    If segmentedService.LastRunUsedSegmentedMode AndAlso String.IsNullOrEmpty(segmentedService.LastRunFallbackReason) Then
                        Await _logService.WriteAsync($"DOWNLOAD | MODO | {item.Name} | Segmentado")
                        Await _logService.WriteAsync($"DOWNLOAD | SEGMENTADO | OK | {item.Name} | Conexoes={segmentedService.LastRunConnectionsUsed} | {downloadTimer.ElapsedMilliseconds}ms")
                    Else
                        Await _logService.WriteAsync($"DOWNLOAD | MODO | {item.Name} | Simples")
                        If Not String.IsNullOrEmpty(segmentedService.LastRunFallbackReason) Then
                            Await _logService.WriteAsync($"DOWNLOAD | SEGMENTADO | FALLBACK | {item.Name} | Motivo={segmentedService.LastRunFallbackReason}")
                        End If
                    End If
                End If
                If Not File.Exists(finalPath) OrElse New FileInfo(finalPath).Length = 0 Then
                    Throw New IOException("Download concluido, mas o arquivo esta vazio ou nao foi salvo.")
                End If

                Dim verifiedSha256 = String.Empty
                If hasCatalogFileName Then
                    ' Lanca excecao se o .part nao conferir; o Finally apaga o .part e a imagem anterior fica intacta.
                    verifiedSha256 = Await VerifyDownloadedFileAsync(item, finalPath, _downloadCts.Token)
                    File.Move(finalPath, destination, True)
                    finalPath = destination
                    partPath = Nothing
                End If

                _downloadPathByName(item.Name) = finalPath
                _downloadStatusByName(item.Name) = True
                UpdateRowStatus(item.Name)
                SaveStatusToDisk()
                RecordNewDownloadInStatus(item, finalPath, verifiedSha256)

                Await _logService.WriteAsync($"DOWNLOAD | OK | {item.Name} | {finalPath} | SHA256={(If(String.IsNullOrEmpty(verifiedSha256), "nao_verificado", verifiedSha256))}")

                LoadCatalog(refreshDownloadStatus:=True)
                SelectItemByName(item.Name)
                _statusLabel.Text = $"Download concluido: {Path.GetFileName(finalPath)}"

                ' Itens com SHA-256 no catalogo ja foram verificados acima. A opcao vale para os demais.
                If String.IsNullOrWhiteSpace(verifiedSha256) AndAlso currentSettings.AutoCalculateSha256AfterDownload Then
                    Await ValidateDownloadIntegrityAsync(item, showSuccessMessage:=False, showErrorMessage:=False)
                End If
            Catch ex As OperationCanceledException
                If downloadTimer IsNot Nothing AndAlso downloadTimer.IsRunning Then
                    downloadTimer.Stop()
                End If
                _statusLabel.Text = "Download cancelado."
                errorLog = $"DOWNLOAD | CANCELADO | {item.Name}"
            Catch ex As Exception
                If downloadTimer IsNot Nothing AndAlso downloadTimer.IsRunning Then
                    downloadTimer.Stop()
                End If
                If partPath IsNot Nothing Then
                    TryDeleteFile(partPath)
                    partPath = Nothing
                End If
                _statusLabel.Text = "Erro no download."
                errorLog = $"DOWNLOAD | ERRO | {item.Name} | {ex.Message}"
                MessageBox.Show(ex.Message, "Downloads", MessageBoxButtons.OK, MessageBoxIcon.Error)
            Finally
                _btnCancelDownload.Enabled = False
                If partPath IsNot Nothing Then
                    TryDeleteFile(partPath)
                End If
                If _downloadCts IsNot Nothing Then
                    _downloadCts.Dispose()
                    _downloadCts = Nothing
                End If
                SetBusyState(False)
            End Try

            If Not String.IsNullOrWhiteSpace(errorLog) Then
                Await _logService.WriteAsync(errorLog)
            End If

            ClearSelection()
        End Sub

        ' Chamado pelo SegmentedDownloadService quando o download multi-conexao falha de vez
        ' (fragmento esgotou tentativas ou a juncao final falhou), para o usuario decidir se
        ' continua com conexao unica ou cancela o download por completo.
        Private Function ConfirmFallbackToSingleConnectionAsync() As Task(Of Boolean)
            Dim result = MessageBox.Show(
                "O download com multiplas conexoes falhou. Deseja continuar baixando com conexao unica?",
                "Downloads", MessageBoxButtons.YesNo, MessageBoxIcon.Warning)
            Return Task.FromResult(result = DialogResult.Yes)
        End Function

        Private Async Sub OnValidateClick(sender As Object, e As EventArgs)
            If _grid.CurrentRow IsNot Nothing Then
                SetSingleChecked(_grid.CurrentRow)
            End If
            Dim item = GetSelectedItem()
            If item Is Nothing Then
                MessageBox.Show("Selecione um item na lista.", "Downloads", MessageBoxButtons.OK, MessageBoxIcon.Information)
                Return
            End If

            SetBusyState(True)
            _progressBar.Value = 0
            _statusLabel.Text = "Validando integridade..."

            Dim errorLog As String = Nothing
            Try
                Await _logService.WriteAsync($"INTEGRITY | INICIO | {item.Name}")
                Await ValidateDownloadIntegrityAsync(item, showSuccessMessage:=False, showErrorMessage:=True)
            Catch ex As Exception
                _statusLabel.Text = "Erro na validacao."
                errorLog = $"INTEGRITY | ERRO | {item.Name} | {ex.Message}"
                MessageBox.Show(ex.Message, "Downloads", MessageBoxButtons.OK, MessageBoxIcon.Error)
            Finally
                SetBusyState(False)
            End Try

            If Not String.IsNullOrWhiteSpace(errorLog) Then
                Await _logService.WriteAsync(errorLog)
            End If
        End Sub

        Private Async Function RefreshRemoteCatalogsAndReloadAsync(showStatus As Boolean, Optional refreshDownloadStatus As Boolean = False) As Task
            Dim dataDir = AppPaths.DataDir
            Dim toolsPath = Path.Combine(dataDir, CatalogService.ToolsCatalogFileName)
            Dim profile = TokenManager.GetUserProfile()
            Dim profileCatalogFileName = CatalogService.GetProfileCatalogFileName(profile)
            Dim profileUrl = CatalogService.GetProfileRemoteCatalogUrl(profile)
            Dim profileFallbackUrl = CatalogService.GetProfileFallbackCatalogUrl(profile)
            Dim profilePath = Path.Combine(dataDir, profileCatalogFileName)

            Try
                If showStatus Then
                    _statusLabel.Text = "Baixando catalogos remotos..."
                End If

                Dim results = Await _catalogService.RefreshCatalogsFromRemoteDetailedAsync(
                    CancellationToken.None,
                    forceRefresh:=showStatus,
                    New CatalogService.CatalogSource(profileUrl, profilePath, profileFallbackUrl),
                    New CatalogService.CatalogSource(CatalogService.RemoteToolsCatalogUrl, toolsPath, CatalogService.FallbackToolsCatalogUrl))

                Dim updated = 0
                For Each result In results
                    If result.Success Then
                        If result.Changed Then
                            updated += 1
                            Await _logService.WriteAsync($"CATALOG | REMOTE | OK | Url={result.RemoteUrl} | Local={result.LocalPath} | Itens={result.ItemCount}")
                        Else
                            Await _logService.WriteAsync($"CATALOG | REMOTE | SEM_ALTERACAO | Url={result.RemoteUrl} | Local={result.LocalPath} | Itens={result.ItemCount}")
                        End If
                    Else
                        Await _logService.WriteAsync($"CATALOG | REMOTE | ERRO | Url={result.RemoteUrl} | Local={result.LocalPath} | {result.ErrorMessage}")
                    End If
                Next

                Await _logService.WriteAsync($"CATALOG | REMOTE | RESUMO | Atualizados={updated} | Total={results.Count}")
            Catch ex As Exception
                _logService.WriteAsync($"CATALOG | REMOTE | FALLBACK_LOCAL | {ex.Message}").GetAwaiter().GetResult()
                If showStatus Then
                    _statusLabel.Text = "Falha ao baixar catalogo remoto. Usando catalogo local."
                End If
            End Try

            LoadCatalog(refreshDownloadStatus)
        End Function

        Private Sub LoadCatalog(Optional refreshDownloadStatus As Boolean = False)
            Try
                Dim dataDir = AppPaths.DataDir
                Dim toolsPath = Path.Combine(dataDir, CatalogService.ToolsCatalogFileName)
                Dim profile = TokenManager.GetUserProfile()
                Dim profilePath = Path.Combine(dataDir, CatalogService.GetProfileCatalogFileName(profile))

                Dim items = _catalogService.LoadAndMerge(profilePath, toolsPath)

                If items.Count = 0 Then
                    _grid.DataSource = New List(Of CatalogItem)()
                    _statusLabel.Text = $"Catálogo não encontrado em: {dataDir}"
                    UpdateActionState()
                    Return
                End If
                _currentItems = items.ToList()
                _settings = _settingsService.Load()
                LoadStatusFromDisk()
                _grid.DataSource = _currentItems
                If refreshDownloadStatus Then
                    UpdateDownloadStatusFromCache(items)
                End If
                InitializeSelectionCheckboxes()
                _statusLabel.Text = If(refreshDownloadStatus, $"Itens carregados: {_grid.Rows.Count}", $"Itens carregados: {_grid.Rows.Count}. Status nao verificado.")
                If refreshDownloadStatus Then
                    UpdateAllRowStatus()
                End If
                UpdateActionState()
            Catch ex As Exception
                _grid.DataSource = New List(Of CatalogItem)()
                _currentItems = New List(Of CatalogItem)()
                _statusLabel.Text = "Catalogo indisponivel. Tente atualizar novamente mais tarde."
                _logService.WriteAsync($"CATALOG | LOCAL | ERRO | {ex.Message}").GetAwaiter().GetResult()
                UpdateActionState()
            End Try
        End Sub

        Private Function GetSelectedItem() As CatalogItem
            Dim checked = GetCheckedItem()
            If checked IsNot Nothing Then
                Return checked
            End If
            Return Nothing
        End Function

        Private Sub OnSelectionChanged(sender As Object, e As EventArgs)
            ' Sincroniza o checkbox com a linha visualmente selecionada no grid,
            ' para que o botão "Baixar selecionado" habilite ao clicar em qualquer célula da linha.
            If _grid.CurrentRow IsNot Nothing Then
                SetSingleChecked(_grid.CurrentRow)
            End If
            UpdateActionState()
        End Sub

        Private Sub ClearSelection()
            If _grid Is Nothing Then
                Return
            End If
            If _grid.Columns.Contains(SelectionColumnName) Then
                For Each row As DataGridViewRow In _grid.Rows
                    row.Cells(SelectionColumnName).Value = False
                Next
            End If
            _grid.ClearSelection()
            _grid.CurrentCell = Nothing
            _selectionLabel.Text = "Selecionado: nenhum"
            UpdateActionState()
        End Sub

        Private Sub OnGridCurrentCellDirtyStateChanged(sender As Object, e As EventArgs)
            If _grid.IsCurrentCellDirty Then
                _grid.CommitEdit(DataGridViewDataErrorContexts.Commit)
            End If
        End Sub

        Private Sub OnGridCellContentClick(sender As Object, e As DataGridViewCellEventArgs)
            If e.RowIndex < 0 OrElse e.ColumnIndex < 0 Then
                Return
            End If

            Dim column = _grid.Columns(e.ColumnIndex)
            If column IsNot Nothing AndAlso String.Equals(column.Name, SelectionColumnName, StringComparison.OrdinalIgnoreCase) Then
                Dim row = _grid.Rows(e.RowIndex)
                SetSingleChecked(row)
                row.Selected = True
                UpdateActionState()
            End If
        End Sub

        Private Function GetCheckedItem() As CatalogItem
            For Each row As DataGridViewRow In _grid.Rows
                Dim cellValue = row.Cells(SelectionColumnName).Value
                If cellValue IsNot Nothing AndAlso TypeOf cellValue Is Boolean AndAlso DirectCast(cellValue, Boolean) Then
                    Return TryCast(row.DataBoundItem, CatalogItem)
                End If
            Next

            Return Nothing
        End Function

        Private Sub InitializeSelectionCheckboxes()
            ClearSelection()
        End Sub

        ''' <summary>Registra no log as novas tentativas da requisicao inicial (erro passageiro do servidor).</summary>
        Private Sub LogRequestRetries(item As CatalogItem, service As IDownloadService)
            Dim simple = TryCast(service, DownloadService)
            Dim segmented = TryCast(service, SegmentedDownloadService)
            Dim retries = If(simple?.LastRunRequestRetries, segmented?.FallbackRequestRetries)
            If retries IsNot Nothing AndAlso retries.Count > 0 Then
                _logService.WriteAsync($"DOWNLOAD | NOVA_TENTATIVA | {item.Name} | {retries.Count} | {String.Join(" ; ", retries)}").GetAwaiter().GetResult()
            End If
        End Sub

        ''' <summary>Registra no log as trocas de conexao lenta (SharePoint; ver SlowConnectionPolicy).</summary>
        Private Sub LogSlowSwaps(item As CatalogItem, service As IDownloadService)
            Dim simple = TryCast(service, DownloadService)
            Dim segmented = TryCast(service, SegmentedDownloadService)
            Dim swaps = If(simple?.LastRunSlowSwaps, 0) + If(segmented Is Nothing, 0, segmented.LastRunSlowSwaps + segmented.FallbackSlowSwaps)
            If swaps > 0 Then
                _logService.WriteAsync($"DOWNLOAD | TROCA_LENTA | {item.Name} | {swaps}").GetAwaiter().GetResult()
            End If
            If segmented IsNot Nothing AndAlso segmented.LastRunRebalances > 0 Then
                _logService.WriteAsync($"DOWNLOAD | REDIVISAO | {item.Name} | {segmented.LastRunRebalances}").GetAwaiter().GetResult()
            End If
        End Sub

        ''' <summary>Marca e mostra a linha do item (ex.: depois de recarregar a lista).</summary>
        Private Sub SelectItemByName(itemName As String)
            For Each row As DataGridViewRow In _grid.Rows
                Dim rowItem = TryCast(row.DataBoundItem, CatalogItem)
                If rowItem IsNot Nothing AndAlso String.Equals(rowItem.Name, itemName, StringComparison.OrdinalIgnoreCase) Then
                    SetSingleChecked(row)
                    _grid.CurrentCell = row.Cells(SelectionColumnName)
                    UpdateActionState()
                    Return
                End If
            Next
        End Sub

        Private Sub SetSingleChecked(targetRow As DataGridViewRow)
            If targetRow Is Nothing Then
                Return
            End If

            For Each row As DataGridViewRow In _grid.Rows
                row.Cells(SelectionColumnName).Value = (row Is targetRow)
            Next
        End Sub

        Private Sub UpdateActionState()
            Dim item = GetSelectedItem()
            Dim hasSelection = item IsNot Nothing
            If Not UseWaitCursor Then
                _btnDownload.Enabled = hasSelection
                _btnValidate.Enabled = hasSelection
            End If
            _selectionLabel.Text = If(hasSelection, $"Selecionado: {item.Name}", "Selecionado: nenhum")
        End Sub

        Private Sub SetBusyState(isBusy As Boolean)
            If Not isBusy Then
                _sha256ProgressGeneration += 1
                _progressBar.Style = ProgressBarStyle.Continuous
                _progressBar.Value = 0
            End If
            UseWaitCursor = isBusy
            _grid.Enabled = Not isBusy
            _btnRefresh.Enabled = Not isBusy
            _btnDownload.Enabled = Not isBusy
            _btnCancelDownload.Enabled = False
            _btnValidate.Enabled = Not isBusy
            _btnOpenFolder.Enabled = Not isBusy
            _busyGif.Visible = isBusy
            Application.DoEvents()
        End Sub

        Private Sub SetProgressMarquee(enabled As Boolean)
            If enabled Then
                _progressBar.Style = ProgressBarStyle.Marquee
                _progressBar.MarqueeAnimationSpeed = 30
                _busyGif.Visible = True
            Else
                _progressBar.MarqueeAnimationSpeed = 0
                _progressBar.Style = ProgressBarStyle.Continuous
                _progressBar.Value = 0
                If Not UseWaitCursor Then
                    _busyGif.Visible = False
                End If
            End If
        End Sub

        Private Sub UpdateAllRowStatus()
            For Each row As DataGridViewRow In _grid.Rows
                Dim item = TryCast(row.DataBoundItem, CatalogItem)
                If item IsNot Nothing Then
                    UpdateRowStatus(item.Name)
                End If
            Next
        End Sub

        Private Sub UpdateRowStatus(itemName As String)
            If String.IsNullOrWhiteSpace(itemName) Then
                Return
            End If

            For Each row As DataGridViewRow In _grid.Rows
                Dim item = TryCast(row.DataBoundItem, CatalogItem)
                If item Is Nothing OrElse Not String.Equals(item.Name, itemName, StringComparison.OrdinalIgnoreCase) Then
                    Continue For
                End If

                Dim downloadCell = row.Cells("DownloadStatus")
                Dim md5Cell = row.Cells("Md5Status")
                Dim sha256Cell = row.Cells("Sha256Status")
                Dim sha256Status = GetSha256Status(itemName)

                If Not String.IsNullOrWhiteSpace(item.Sha256) Then
                    Dim isDownloadedAndValid = sha256Status.HasValue AndAlso sha256Status.Value
                    downloadCell.Value = If(isDownloadedAndValid, _statusOkImage, _statusErrorImage)
                ElseIf _downloadStatusByName.ContainsKey(itemName) Then
                    Dim isDownloaded = _downloadStatusByName(itemName)
                    downloadCell.Value = If(isDownloaded, _statusOkImage, _statusErrorImage)
                Else
                    downloadCell.Value = Nothing
                End If

                If _md5StatusByName.ContainsKey(itemName) Then
                    Dim status = _md5StatusByName(itemName)
                    If status.HasValue Then
                        md5Cell.Value = If(status.Value, _statusOkImage, _statusErrorImage)
                    Else
                        md5Cell.Value = _statusUnknownImage
                    End If
                Else
                    md5Cell.Value = Nothing
                End If
                
                If sha256Status.HasValue Then
                    sha256Cell.Value = If(sha256Status.Value, _statusOkImage, _statusErrorImage)
                Else
                    sha256Cell.Value = _statusUnknownImage
                End If
            Next
        End Sub

        Private Function GetSha256Status(itemName As String) As Boolean?
            Try
                Dim catalogItem = _currentItems.FirstOrDefault(Function(item) String.Equals(item.Name, itemName, StringComparison.OrdinalIgnoreCase))
                If catalogItem Is Nothing OrElse String.IsNullOrWhiteSpace(catalogItem.Sha256) Then
                    Return Nothing
                End If

                ' Carregar status do JSON
                Dim statusMap = LoadDownloadStatusMap()
                Dim cacheKey = itemName
                
                Dim statusEntry As DownloadStatusEntry = Nothing
                If Not statusMap.TryGetValue(cacheKey, statusEntry) OrElse Not IsStoredSha256StatusUsable(catalogItem, statusEntry) Then
                    statusEntry = FindStoredStatusEntryByFileNameAndSha256(statusMap, catalogItem.FileName, catalogItem.Sha256)
                End If

                If Not IsStoredSha256StatusUsable(catalogItem, statusEntry) Then
                    Return False
                End If

                Dim filePath = If(Not String.IsNullOrWhiteSpace(statusEntry.Path), statusEntry.Path, If(_downloadPathByName.ContainsKey(itemName), _downloadPathByName(itemName), String.Empty))
                If String.IsNullOrWhiteSpace(filePath) OrElse Not File.Exists(filePath) Then
                    Return False
                End If
                
                ' Se nao tem SHA-256 calculado, exibir o mesmo X vermelho usado em falhas.
                If String.IsNullOrWhiteSpace(statusEntry.ImageSha256) Then
                    Return False
                End If
                
                Return String.Equals(catalogItem.Sha256.Trim(), statusEntry.ImageSha256.Trim(), StringComparison.OrdinalIgnoreCase)
                
            Catch ex As Exception
                _logService.WriteAsync($"SHA256 | STATUS | ERRO | {itemName} | {ex.Message}").GetAwaiter().GetResult()
                Return False
            End Try
        End Function

        Private Function CreateCheckImage(color As Color) As Image
            Dim bmp As New Bitmap(16, 16)
            Using g = Graphics.FromImage(bmp)
                g.SmoothingMode = Drawing2D.SmoothingMode.AntiAlias
                g.Clear(Color.Transparent)
                Using pen As New Pen(color, 2.2F)
                    g.DrawLines(pen, New Point() {
                        New Point(3, 9),
                        New Point(7, 13),
                        New Point(13, 4)
                    })
                End Using
            End Using
            Return bmp
        End Function

        Private Function CreateErrorImage(color As Color) As Image
            Dim bmp As New Bitmap(16, 16)
            Using g = Graphics.FromImage(bmp)
                g.SmoothingMode = Drawing2D.SmoothingMode.AntiAlias
                g.Clear(Color.Transparent)
                Using pen As New Pen(color, 2.2F)
                    g.DrawLine(pen, New Point(4, 4), New Point(12, 12))
                    g.DrawLine(pen, New Point(12, 4), New Point(4, 12))
                End Using
            End Using
            Return bmp
        End Function

        Private Function CreateUnknownImage(color As Color) As Image
            Dim bmp As New Bitmap(16, 16)
            Using g = Graphics.FromImage(bmp)
                g.SmoothingMode = Drawing2D.SmoothingMode.AntiAlias
                g.Clear(Color.Transparent)
                g.TextRenderingHint = Drawing.Text.TextRenderingHint.AntiAliasGridFit
                Using font As New Font("Segoe UI", 9.0!, FontStyle.Bold)
                    Using brush As New SolidBrush(color)
                        Dim textSize = g.MeasureString("?", font)
                        Dim x = (bmp.Width - textSize.Width) / 2.0F
                        Dim y = (bmp.Height - textSize.Height) / 2.0F - 1.0F
                        g.DrawString("?", font, brush, x, y)
                    End Using
                End Using
            End Using
            Return bmp
        End Function

        ''' <summary>
        ''' Confere o arquivo recem-baixado contra o catalogo antes de ele substituir a imagem atual.
        ''' Com SHA-256 no catalogo, a conferencia e obrigatoria. Sem ele, confere o tamanho, se houver.
        ''' Devolve o SHA-256 verificado, ou vazio quando o catalogo nao tem hash.
        ''' </summary>
        Private Async Function VerifyDownloadedFileAsync(item As CatalogItem, filePath As String, cancellationToken As CancellationToken) As Task(Of String)
            If Not String.IsNullOrWhiteSpace(item.Sha256) Then
                Dim expected = item.Sha256.Trim()
                Dim actual = Await ComputeImageSha256Async(filePath, cancellationToken, CreateSha256Progress(item.Name))
                If Not String.Equals(expected, actual, StringComparison.OrdinalIgnoreCase) Then
                    Await _logService.WriteAsync($"DOWNLOAD | SHA256_DIVERGENTE | {item.Name} | Esperado={expected} | Obtido={actual}")
                    Throw New IOException(
                        $"O arquivo baixado nao confere com o catalogo (SHA-256 divergente).{Environment.NewLine}" &
                        $"Esperado: {expected}{Environment.NewLine}Obtido: {actual}{Environment.NewLine}{Environment.NewLine}" &
                        "O download foi descartado. A imagem anterior, se existia, foi mantida.")
                End If
                Return actual
            End If

            If item.ImageSizeBytes > 0 Then
                Dim actualSize = New FileInfo(filePath).Length
                If actualSize <> item.ImageSizeBytes Then
                    Await _logService.WriteAsync($"DOWNLOAD | TAMANHO_DIVERGENTE | {item.Name} | Esperado={item.ImageSizeBytes} | Obtido={actualSize}")
                    Throw New IOException(
                        $"O arquivo baixado tem {actualSize:N0} bytes, mas o catalogo informa {item.ImageSizeBytes:N0}.{Environment.NewLine}" &
                        "O download foi descartado. A imagem anterior, se existia, foi mantida.")
                End If
            End If

            Return String.Empty
        End Function

        ''' <summary>
        ''' Registra no status o arquivo recem-baixado. O hash gravado passa a ser o do arquivo novo
        ''' (ou vazio, se nao foi verificado), nunca o do arquivo anterior.
        ''' </summary>
        Private Sub RecordNewDownloadInStatus(item As CatalogItem, filePath As String, verifiedSha256 As String)
            Dim statusMap = LoadDownloadStatusMap()
            Dim entry As DownloadStatusEntry = Nothing
            If Not statusMap.TryGetValue(item.Name, entry) OrElse entry Is Nothing Then
                entry = New DownloadStatusEntry()
                statusMap(item.Name) = entry
            End If

            Dim sameImage = Not String.IsNullOrEmpty(verifiedSha256) AndAlso
                            String.Equals(entry.ImageSha256, verifiedSha256, StringComparison.OrdinalIgnoreCase)
            If Not sameImage Then
                ' Imagem diferente da anterior: a verificacao de USB gravada antes nao vale mais.
                entry.UsbSha256 = String.Empty
                entry.UsbVerified = False
                entry.UsbVerifiedAt = String.Empty
            End If

            entry.Downloaded = True
            entry.Path = filePath
            entry.UpdatedAt = DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss")
            entry.ImageSha256 = verifiedSha256

            If String.IsNullOrEmpty(verifiedSha256) Then
                entry.Md5Valid = Nothing
                _md5StatusByName.Remove(item.Name)
            Else
                entry.Md5Valid = True
                _md5StatusByName(item.Name) = True
                LinkEquivalentCatalogItemsBySha256(item, filePath, verifiedSha256, statusMap)
            End If

            SaveDownloadStatusMap(statusMap)
            UpdateAllRowStatus()
        End Sub

        Private Shared Sub TryDeleteFile(filePath As String)
            Try
                If File.Exists(filePath) Then
                    File.Delete(filePath)
                End If
            Catch
                ' Melhor esforco: um .part que nao pode ser apagado agora e recriado no proximo download.
            End Try
        End Sub

        Private Function GetStatusStoragePath() As String
            Directory.CreateDirectory(AppPaths.ConfigDir)
            Return AppPaths.DownloadStatusPath
        End Function

        Private Sub LoadStatusFromDisk()
            If Not File.Exists(_statusStoragePath) Then
                Return
            End If

            _downloadStatusByName.Clear()
            _md5StatusByName.Clear()
            _md5CacheByName.Clear()
            _downloadPathByName.Clear()

            Dim json = File.ReadAllText(_statusStoragePath)
            Dim options = New JsonSerializerOptions() With {
                .PropertyNameCaseInsensitive = True
            }
            Dim data = JsonSerializer.Deserialize(Of Dictionary(Of String, DownloadStatusEntry))(json, options)
            If data Is Nothing Then
                Return
            End If

            For Each pair In data
                _downloadStatusByName(pair.Key) = pair.Value.Downloaded
                If pair.Value.Md5Valid.HasValue Then
                    _md5StatusByName(pair.Key) = pair.Value.Md5Valid
                End If
                If Not String.IsNullOrWhiteSpace(pair.Value.Md5Cache) Then
                    _md5CacheByName(pair.Key) = pair.Value.Md5Cache
                End If
                If Not String.IsNullOrWhiteSpace(pair.Value.Path) Then
                    _downloadPathByName(pair.Key) = pair.Value.Path
                End If
            Next
        End Sub

        Private Sub SaveStatusToDisk()
            ' Carregar status existente para preservar campos SHA-256
            Dim existingData = LoadDownloadStatusMap()

            ' O arquivo e compartilhado por todos os perfis: parte do conteudo existente e regrava
            ' apenas os itens do perfil atual, sem descartar as entradas dos outros perfis.
            Dim data As New Dictionary(Of String, DownloadStatusEntry)(existingData, StringComparer.OrdinalIgnoreCase)

            For Each item In _currentItems
                Dim itemName = item.Name
                
                ' Tentar usar entrada existente ou criar nova
                Dim existingEntry As DownloadStatusEntry = Nothing
                If existingData.ContainsKey(itemName) Then
                    existingEntry = existingData(itemName)
                End If
                
                Dim entry As New DownloadStatusEntry() With {
                    .Downloaded = If(_downloadStatusByName.ContainsKey(itemName), _downloadStatusByName(itemName), False),
                    .Md5Valid = If(_md5StatusByName.ContainsKey(itemName), _md5StatusByName(itemName), Nothing),
                    .Md5Cache = If(_md5CacheByName.ContainsKey(itemName), _md5CacheByName(itemName), String.Empty),
                    .Path = If(_downloadPathByName.ContainsKey(itemName), _downloadPathByName(itemName), String.Empty),
                    .UpdatedAt = DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                    .ImageSha256 = If(existingEntry?.ImageSha256, String.Empty),
                    .UsbSha256 = If(existingEntry?.UsbSha256, String.Empty),
                    .UsbVerified = If(existingEntry?.UsbVerified, False),
                    .UsbVerifiedAt = If(existingEntry?.UsbVerifiedAt, String.Empty)
                }
                data(itemName) = entry
            Next

            Dim options = New JsonSerializerOptions() With {.WriteIndented = True}
            File.WriteAllText(_statusStoragePath, JsonSerializer.Serialize(data, options))
        End Sub

        Private _statusMapCache As Dictionary(Of String, DownloadStatusEntry)
        Private _statusMapStamp As Date

        ''' <summary>Imagem baixada e verificada cujo SHA-256 registrado difere do catalogo atual (dica da linha).</summary>
        Private Function HasNewVersionInCatalog(item As CatalogItem) As Boolean
            Try
                Dim stamp = If(File.Exists(AppPaths.DownloadStatusPath), File.GetLastWriteTimeUtc(AppPaths.DownloadStatusPath), Date.MinValue)
                If _statusMapCache Is Nothing OrElse stamp <> _statusMapStamp Then
                    _statusMapCache = LoadDownloadStatusMap()
                    _statusMapStamp = stamp
                End If
                Dim entry As DownloadStatusEntry = Nothing
                If Not _statusMapCache.TryGetValue(item.Name, entry) OrElse entry Is Nothing Then Return False
                Dim exists = Not String.IsNullOrWhiteSpace(entry.Path) AndAlso File.Exists(entry.Path)
                Return DownloadOverwriteCheck.Classify(exists, entry.Downloaded AndAlso entry.Md5Valid.GetValueOrDefault(), entry.ImageSha256, item.Sha256) =
                       DownloadOverwriteCase.NewVersionInCatalog
            Catch
                Return False
            End Try
        End Function

        Private Function GetStoredStatusEntry(item As CatalogItem, destination As String) As DownloadStatusEntry
            If Not File.Exists(_statusStoragePath) Then
                Return Nothing
            End If

            Try
                Dim json = File.ReadAllText(_statusStoragePath)
                Dim options = New JsonSerializerOptions() With {
                    .PropertyNameCaseInsensitive = True
                }
                Dim data = JsonSerializer.Deserialize(Of Dictionary(Of String, DownloadStatusEntry))(json, options)
                If data Is Nothing Then
                    Return Nothing
                End If

                If item IsNot Nothing Then
                    Dim entry As DownloadStatusEntry = Nothing
                    If data.TryGetValue(item.Name, entry) Then
                        Return entry
                    End If
                End If

                If Not String.IsNullOrWhiteSpace(destination) Then
                    Dim targetName = Path.GetFileName(destination)
                    For Each pair In data
                        Dim candidate = pair.Value
                        If candidate Is Nothing Then
                            Continue For
                        End If
                        If String.Equals(candidate.Path, destination, StringComparison.OrdinalIgnoreCase) Then
                            Return candidate
                        End If
                        If Not String.IsNullOrWhiteSpace(candidate.Path) AndAlso String.Equals(Path.GetFileName(candidate.Path), targetName, StringComparison.OrdinalIgnoreCase) Then
                            Return candidate
                        End If
                    Next
                End If
            Catch ex As Exception
                _logService.WriteAsync($"STATUS | ERRO | Falha ao carregar status: {ex.Message}").GetAwaiter().GetResult()
            End Try

            Return Nothing
        End Function

        Private Sub UpdateDownloadStatusFromCache(items As IReadOnlyList(Of CatalogItem))
            For Each item In items
                Dim pathFromMap As String = Nothing
                If _downloadPathByName.ContainsKey(item.Name) Then
                    pathFromMap = _downloadPathByName(item.Name)
                End If

                Dim cachePath = GetCacheFilePath(item.FileUrl)
                Dim resolvedPath = ResolveExistingCachePath(item)
                Dim exists = Not String.IsNullOrWhiteSpace(resolvedPath) AndAlso File.Exists(resolvedPath)
                Dim isDownloaded = exists

                If Not String.IsNullOrWhiteSpace(item.Sha256) Then
                    Dim sha256Status = GetSha256Status(item.Name)
                    isDownloaded = sha256Status.HasValue AndAlso sha256Status.Value
                End If

                _downloadStatusByName(item.Name) = isDownloaded
                If Not isDownloaded Then
                    _md5StatusByName(item.Name) = False
                    _md5CacheByName(item.Name) = String.Empty
                End If
                If exists Then
                    _downloadPathByName(item.Name) = resolvedPath
                End If
            Next
        End Sub

        Private Async Function ValidateDownloadIntegrityAsync(item As CatalogItem, showSuccessMessage As Boolean, showErrorMessage As Boolean) As Task
            If item IsNot Nothing AndAlso Not String.IsNullOrWhiteSpace(item.Sha256) Then
                Await ValidateSha256Async(item, showSuccessMessage, showErrorMessage)
                Return
            End If

            Await _logService.WriteAsync($"INTEGRITY | FALLBACK_MD5 | {item?.Name}")
            Await ValidateMd5Async(item, showSuccessMessage, showErrorMessage)
        End Function

        Private Async Function ValidateSha256Async(item As CatalogItem, showSuccessMessage As Boolean, showErrorMessage As Boolean) As Task
            If item Is Nothing OrElse String.IsNullOrWhiteSpace(item.Sha256) Then
                _statusLabel.Text = "SHA-256 nao configurado para este item."
                If showErrorMessage Then
                    MessageBox.Show("SHA-256 nao configurado para este item.", "Downloads", MessageBoxButtons.OK, MessageBoxIcon.Warning)
                End If
                Return
            End If

            Dim destination = GetDownloadedPath(item)
            If String.IsNullOrWhiteSpace(destination) OrElse Not File.Exists(destination) Then
                destination = ResolveExistingCachePath(item, allowUnverifiedCacheCandidate:=False)
            End If

            If String.IsNullOrWhiteSpace(destination) OrElse Not File.Exists(destination) Then
                UpdateRowStatus(item.Name)
                SaveStatusToDisk()
                _statusLabel.Text = "Arquivo nao encontrado no cache. Faca o download primeiro."
                If showErrorMessage Then
                    MessageBox.Show("Arquivo nao encontrado no cache. Faca o download primeiro.", "Downloads", MessageBoxButtons.OK, MessageBoxIcon.Warning)
                End If
                Return
            End If

            Try
                SetProgressMarquee(False)
                _statusLabel.Text = "Calculando SHA-256 da imagem local... 0%"
                Dim sha256Timer = Stopwatch.StartNew()
                Dim sha256Progress = CreateSha256Progress(item.Name)
                Dim imageSha256 = Await ComputeImageSha256Async(destination, CancellationToken.None, sha256Progress)
                Dim match = String.Equals(item.Sha256.Trim(), imageSha256, StringComparison.OrdinalIgnoreCase)

                If Not match Then
                    Dim verifiedCandidate = Await FindMatchingCachePathBySha256Async(item, sha256Progress)
                    If verifiedCandidate IsNot Nothing Then
                        destination = verifiedCandidate.Item1
                        imageSha256 = verifiedCandidate.Item2
                        match = True
                    End If
                End If

                sha256Timer.Stop()
                Await _logService.WriteAsync($"SHA256 | TEMPO | {item.Name} | {sha256Timer.ElapsedMilliseconds}ms | {imageSha256}")

                Dim statusMap = LoadDownloadStatusMap()
                If Not statusMap.ContainsKey(item.Name) Then
                    statusMap(item.Name) = New DownloadStatusEntry()
                End If
                statusMap(item.Name).Downloaded = match
                statusMap(item.Name).Path = destination
                statusMap(item.Name).UpdatedAt = DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss")
                statusMap(item.Name).ImageSha256 = imageSha256

                _md5StatusByName(item.Name) = match
                _downloadStatusByName(item.Name) = match
                _downloadPathByName(item.Name) = destination
                If match Then
                    LinkEquivalentCatalogItemsBySha256(item, destination, imageSha256, statusMap)
                End If
                SaveDownloadStatusMap(statusMap)
                If match Then
                    UpdateAllRowStatus()
                Else
                    UpdateRowStatus(item.Name)
                End If
                SaveStatusToDisk()

                _statusLabel.Text = If(match, "SHA-256 valido.", "SHA-256 divergente.")
                Await _logService.WriteAsync($"SHA256 | {(If(match, "OK", "ERRO"))} | {item.Name}")

                If Not match AndAlso showErrorMessage Then
                    MessageBox.Show($"Esperado: {item.Sha256}{Environment.NewLine}Atual: {imageSha256}", "Validacao SHA-256", MessageBoxButtons.OK, MessageBoxIcon.Warning)
                ElseIf showSuccessMessage Then
                    MessageBox.Show("SHA-256 valido.", "Validacao SHA-256", MessageBoxButtons.OK, MessageBoxIcon.Information)
                End If
            Finally
                SetProgressMarquee(False)
            End Try
        End Function

        Private Async Function ValidateMd5Async(item As CatalogItem, showSuccessMessage As Boolean, showErrorMessage As Boolean) As Task
            Dim destination = GetDownloadedPath(item)
            If String.IsNullOrWhiteSpace(destination) OrElse Not File.Exists(destination) Then
                _md5StatusByName(item.Name) = False
                _md5CacheByName(item.Name) = String.Empty
                UpdateRowStatus(item.Name)
                SaveStatusToDisk()
                _statusLabel.Text = "Arquivo nao encontrado no cache. Faca o download primeiro."
                If showErrorMessage Then
                    MessageBox.Show("Arquivo nao encontrado no cache. Faca o download primeiro.", "Downloads", MessageBoxButtons.OK, MessageBoxIcon.Warning)
                End If
                Return
            End If
            If String.IsNullOrWhiteSpace(item.Md5) AndAlso String.IsNullOrWhiteSpace(item.Md5Url) Then
                _md5StatusByName(item.Name) = False
                _md5CacheByName(item.Name) = String.Empty
                UpdateRowStatus(item.Name)
                SaveStatusToDisk()
                _statusLabel.Text = "Integridade nao configurada para este item."
                If showErrorMessage Then
                    MessageBox.Show("Integridade nao configurada para este item.", "Downloads", MessageBoxButtons.OK, MessageBoxIcon.Warning)
                End If
                Return
            End If
            Try
                SetProgressMarquee(True)
                _statusLabel.Text = "Validando integridade..."

                Dim expectedMd5 = Await ResolveExpectedMd5Async(item)
                Dim md5Timer = Stopwatch.StartNew()
                Dim actualMd5 = Await _hashService.ComputeMd5Async(destination, CancellationToken.None)
                md5Timer.Stop()
                Await _logService.WriteAsync($"MD5 | TEMPO | {item.Name} | {md5Timer.ElapsedMilliseconds}ms")
                Dim match = String.Equals(expectedMd5, actualMd5, StringComparison.OrdinalIgnoreCase)
                _md5StatusByName(item.Name) = match
                _md5CacheByName(item.Name) = actualMd5
                
                ' Calcular SHA256 da imagem para verificação USB futura (se configurado)
                Dim settings = _settingsService.Load()
                If settings.AutoCalculateSha256AfterDownload Then
                    Try
                        ' Feedback visual para cálculo SHA-256
                        SetProgressMarquee(False)
                        _statusLabel.Text = $"Calculando SHA-256 de {item.Name}... 0%"
                        Application.DoEvents()
                        
                        Dim sha256Timer = Stopwatch.StartNew()
                        Dim sha256Progress = CreateSha256Progress(item.Name)
                        Dim imageSha256 = Await ComputeImageSha256Async(destination, CancellationToken.None, sha256Progress)
                        sha256Timer.Stop()
                        Await _logService.WriteAsync($"SHA256 | TEMPO | {item.Name} | {sha256Timer.ElapsedMilliseconds}ms | {imageSha256}")
                        
                        ' Atualizar status com SHA256
                        Dim statusMap = LoadDownloadStatusMap()
                        If statusMap.ContainsKey(item.Name) Then
                            statusMap(item.Name).ImageSha256 = imageSha256
                            SaveDownloadStatusMap(statusMap)
                        End If
                        
                        ' Atualizar UI para mostrar SHA-256 calculado
                        UpdateRowStatus(item.Name)
                        
                    Catch ex As Exception
                        _logService.WriteAsync($"SHA256 | ERRO | {item.Name} | {ex.Message}").GetAwaiter().GetResult()
                    Finally
                        SetProgressMarquee(False)
                        _statusLabel.Text = "Download concluído."
                    End Try
                Else
                    ' Se não calculou SHA-256, atualizar status aqui
                    UpdateRowStatus(item.Name)
                End If
                
                ' UpdateRowStatus já foi chamado dentro do bloco SHA-256 se aplicável
                ' Não chamar novamente para evitar duplicação
                SaveStatusToDisk()

                _statusLabel.Text = If(match, "Integridade valida.", "Integridade divergente.")
                Await _logService.WriteAsync($"MD5 | {(If(match, "OK", "ERRO"))} | {item.Name}")

                If Not match AndAlso showErrorMessage Then
                    MessageBox.Show($"Esperado: {expectedMd5}{Environment.NewLine}Atual: {actualMd5}", "Validacao de integridade", MessageBoxButtons.OK, MessageBoxIcon.Warning)
                ElseIf showSuccessMessage Then
                    MessageBox.Show("Integridade valida.", "Validacao de integridade", MessageBoxButtons.OK, MessageBoxIcon.Information)
                End If
            Finally
                SetProgressMarquee(False)
            End Try
        End Function

        Private Async Function ValidateAllCacheMd5Async() As Task
            SetProgressMarquee(True)
            _statusLabel.Text = "Validando integridade das imagens locais..."
            Await _logService.WriteAsync("MD5 | CACHE | INICIO")

            For Each item In _currentItems
                Dim destination = GetDownloadedPath(item)
                If String.IsNullOrWhiteSpace(destination) OrElse Not File.Exists(destination) Then
                    _md5StatusByName(item.Name) = False
                    UpdateRowStatus(item.Name)
                    Continue For
                End If

                Await ValidateDownloadIntegrityAsync(item, showSuccessMessage:=False, showErrorMessage:=False)
            Next

            SetProgressMarquee(False)
            _statusLabel.Text = "Integridade das imagens locais verificada."
            Await _logService.WriteAsync("INTEGRITY | CACHE | CONCLUIDO")
        End Function

        Private Async Function ValidateAllCacheMd5FromStoredAsync() As Task
            SetProgressMarquee(True)
            _statusLabel.Text = "Verificando integridade salva das imagens locais..."
            Await _logService.WriteAsync("INTEGRITY | CACHE | INICIO | STORED")

            Dim missingIntegrityCount = 0
            For Each item In _currentItems
                Dim hasStoredIntegrity = Await ValidateIntegrityFromStoredAsync(item)
                If Not hasStoredIntegrity Then
                    missingIntegrityCount += 1
                End If
            Next

            SetProgressMarquee(False)
            Dim mismatchCount = GetMd5MismatchCount()
            _statusLabel.Text = If(mismatchCount > 0, "Ha imagens locais divergentes; baixe novamente.", "Integridade salva das imagens locais verificada.")
            Await _logService.WriteAsync($"INTEGRITY | CACHE | STORED | MISMATCH_COUNT | {mismatchCount}")
            Await _logService.WriteAsync("INTEGRITY | CACHE | CONCLUIDO | STORED")

            If missingIntegrityCount > 0 Then
                Await _logService.WriteAsync($"INTEGRITY | CACHE | STORED | MISSING | {missingIntegrityCount}")
                MessageBox.Show($"Existem {missingIntegrityCount} imagens sem integridade salva.{Environment.NewLine}Use o botao ""Validar integridade"" para calcular.", "Validacao de integridade", MessageBoxButtons.OK, MessageBoxIcon.Information)
            End If
        End Function

        Private Async Function ValidateIntegrityFromStoredAsync(item As CatalogItem) As Task(Of Boolean)
            If item IsNot Nothing AndAlso Not String.IsNullOrWhiteSpace(item.Sha256) Then
                Return ValidateSha256FromStored(item)
            End If

            Return Await ValidateMd5FromStoredAsync(item)
        End Function

        Private Function ValidateSha256FromStored(item As CatalogItem) As Boolean
            Dim destination = ResolveExistingCachePath(item)
            If String.IsNullOrWhiteSpace(destination) OrElse Not File.Exists(destination) Then
                _downloadStatusByName(item.Name) = False
                UpdateRowStatus(item.Name)
                SaveStatusToDisk()
                Return True
            End If

            Dim statusMap = LoadDownloadStatusMap()
            Dim statusEntry As DownloadStatusEntry = Nothing
            If Not statusMap.TryGetValue(item.Name, statusEntry) OrElse Not IsStoredSha256StatusUsable(item, statusEntry) Then
                statusEntry = FindStoredStatusEntryByFileNameAndSha256(statusMap, item.FileName, item.Sha256)
                If statusEntry IsNot Nothing Then
                    LinkEquivalentCatalogItemsBySha256(item, destination, statusEntry.ImageSha256, statusMap)
                    SaveDownloadStatusMap(statusMap)
                End If
            End If

            If Not IsStoredSha256StatusUsable(item, statusEntry) Then
                UpdateRowStatus(item.Name)
                Return False
            End If

            _downloadPathByName(item.Name) = If(String.IsNullOrWhiteSpace(statusEntry.Path), destination, statusEntry.Path)
            _downloadStatusByName(item.Name) = True
            _md5StatusByName(item.Name) = True
            If Not (statusEntry.Md5Valid.HasValue AndAlso statusEntry.Md5Valid.Value) Then
                statusEntry.Md5Valid = True
                statusMap(item.Name) = statusEntry
                SaveDownloadStatusMap(statusMap)
            End If
            UpdateRowStatus(item.Name)
            Return True
        End Function

        Private Async Function ValidateMd5FromStoredAsync(item As CatalogItem) As Task(Of Boolean)
            Dim destination = GetDownloadedPath(item)
            If String.IsNullOrWhiteSpace(destination) OrElse Not File.Exists(destination) Then
                _md5StatusByName(item.Name) = False
                UpdateRowStatus(item.Name)
                SaveStatusToDisk()
                Return True
            End If

            If String.IsNullOrWhiteSpace(item.Md5) AndAlso String.IsNullOrWhiteSpace(item.Md5Url) Then
                _md5StatusByName(item.Name) = False
                UpdateRowStatus(item.Name)
                SaveStatusToDisk()
                Return True
            End If

            Dim storedMd5 As String = Nothing
            If _md5CacheByName.ContainsKey(item.Name) Then
                storedMd5 = _md5CacheByName(item.Name)
            End If
            If String.IsNullOrWhiteSpace(storedMd5) Then
                _md5StatusByName(item.Name) = Nothing
                UpdateRowStatus(item.Name)
                SaveStatusToDisk()
                Return False
            End If

            Dim expectedMd5 = Await ResolveExpectedMd5Async(item)
            Dim match = String.Equals(expectedMd5, storedMd5, StringComparison.OrdinalIgnoreCase)
            _md5StatusByName(item.Name) = match
            UpdateRowStatus(item.Name)
            SaveStatusToDisk()
            Return True
        End Function

        Private Async Function ResolveExpectedMd5Async(item As CatalogItem) As Task(Of String)
            If item IsNot Nothing AndAlso Not String.IsNullOrWhiteSpace(item.Md5) Then
                Return ParseMd5(item.Md5)
            End If

            Return Await FetchMd5Async(item.Md5Url)
        End Function

        ' Avisos de progresso chegam pela fila da UI e podem ser processados depois do fim da operacao;
        ' SetBusyState(False) troca a geracao e os avisos atrasados sao ignorados.
        Private _sha256ProgressGeneration As Integer

        Private Function CreateSha256Progress(itemName As String) As IProgress(Of Integer)
            Dim generation = _sha256ProgressGeneration
            Return New Progress(Of Integer)(Sub(value)
                                                If generation <> _sha256ProgressGeneration Then Return
                                                Dim percent = Math.Max(0, Math.Min(100, value))
                                                If _progressBar.Style <> ProgressBarStyle.Continuous Then
                                                    _progressBar.Style = ProgressBarStyle.Continuous
                                                End If
                                                _progressBar.Value = percent
                                                _statusLabel.Text = $"Calculando SHA-256 de {itemName}... {percent}%"
                                            End Sub)
        End Function

        Private Async Function ComputeImageSha256Async(filePath As String, cancellationToken As CancellationToken, Optional progress As IProgress(Of Integer) = Nothing) As Task(Of String)
            Try
                Dim totalBytes = New FileInfo(filePath).Length
                Dim totalRead As Long = 0
                Dim lastPercent = 0
                progress?.Report(0)

                Using sha256 = System.Security.Cryptography.IncrementalHash.CreateHash(System.Security.Cryptography.HashAlgorithmName.SHA256)
                    Using stream = New FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize:=81920, useAsync:=True)
                        Dim buffer(81919) As Byte

                        Do
                            Dim bytesRead = Await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken)
                            If bytesRead = 0 Then
                                Exit Do
                            End If

                            sha256.AppendData(buffer, 0, bytesRead)
                            totalRead += bytesRead

                            If totalBytes > 0 Then
                                Dim percent = CInt(Math.Min(100, (totalRead * 100L) \ totalBytes))
                                If percent <> lastPercent Then
                                    lastPercent = percent
                                    progress?.Report(percent)
                                End If
                            End If
                        Loop

                        progress?.Report(100)
                        Dim hash = sha256.GetHashAndReset()
                        Return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant()
                    End Using
                End Using
            Catch ex As Exception
                _logService.WriteAsync($"SHA256 | ERRO | {filePath} | {ex.Message}").GetAwaiter().GetResult()
                Throw
            End Try
        End Function

        Private Function LoadDownloadStatusMap() As Dictionary(Of String, DownloadStatusEntry)
            Dim statusPath = AppPaths.DownloadStatusPath
            If Not File.Exists(statusPath) Then
                Return New Dictionary(Of String, DownloadStatusEntry)(StringComparer.OrdinalIgnoreCase)
            End If

            Dim json = File.ReadAllText(statusPath)
            Dim options = New System.Text.Json.JsonSerializerOptions() With {
                .PropertyNameCaseInsensitive = True
            }
            Dim data = System.Text.Json.JsonSerializer.Deserialize(Of Dictionary(Of String, DownloadStatusEntry))(json, options)
            If data Is Nothing Then
                Return New Dictionary(Of String, DownloadStatusEntry)(StringComparer.OrdinalIgnoreCase)
            End If

            Return New Dictionary(Of String, DownloadStatusEntry)(data, StringComparer.OrdinalIgnoreCase)
        End Function

        Private Function ResolveExistingCachePath(item As CatalogItem, Optional allowUnverifiedCacheCandidate As Boolean = True) As String
            If item Is Nothing Then
                Return String.Empty
            End If

            Dim pathFromMap As String = Nothing
            If _downloadPathByName.TryGetValue(item.Name, pathFromMap) AndAlso Not String.IsNullOrWhiteSpace(pathFromMap) AndAlso File.Exists(pathFromMap) AndAlso MatchesCatalogFileName(item, pathFromMap) Then
                Return pathFromMap
            End If

            Dim destination = GetCatalogCacheFilePath(item)
            If Not String.IsNullOrWhiteSpace(destination) AndAlso File.Exists(destination) Then
                Return destination
            End If

            Dim statusMap = LoadDownloadStatusMap()
            Dim statusEntry As DownloadStatusEntry = Nothing
            If statusMap.TryGetValue(item.Name, statusEntry) AndAlso statusEntry IsNot Nothing AndAlso Not String.IsNullOrWhiteSpace(statusEntry.Path) AndAlso File.Exists(statusEntry.Path) AndAlso MatchesCatalogFileName(item, statusEntry.Path) Then
                Return statusEntry.Path
            End If

            If Not String.IsNullOrWhiteSpace(item.Sha256) Then
                statusEntry = FindStoredStatusEntryByFileNameAndSha256(statusMap, item.FileName, item.Sha256)
                If statusEntry IsNot Nothing AndAlso Not String.IsNullOrWhiteSpace(statusEntry.Path) AndAlso File.Exists(statusEntry.Path) Then
                    Return statusEntry.Path
                End If
            End If

            If Not allowUnverifiedCacheCandidate Then
                Return String.Empty
            End If

            If Not Directory.Exists(AppPaths.CacheDir) Then
                Return String.Empty
            End If

            Dim expectedSize = item.ImageSizeBytes
            For Each candidate In Directory.EnumerateFiles(AppPaths.CacheDir)
                If expectedSize > 0 Then
                    Try
                        If New FileInfo(candidate).Length <> expectedSize Then
                            Continue For
                        End If
                    Catch
                        Continue For
                    End Try
                End If

                If LooksLikeImageCacheFile(candidate) AndAlso MatchesCatalogFileName(item, candidate) Then
                    Return candidate
                End If
            Next

            Return String.Empty
        End Function

        Private Async Function FindMatchingCachePathBySha256Async(item As CatalogItem, progress As IProgress(Of Integer)) As Task(Of Tuple(Of String, String))
            If item Is Nothing OrElse String.IsNullOrWhiteSpace(item.Sha256) OrElse Not Directory.Exists(AppPaths.CacheDir) Then
                Return Nothing
            End If

            Dim expectedSha256 = item.Sha256.Trim()
            For Each candidate In GetCacheFileCandidates(item)
                Try
                    Dim candidateSha256 = Await ComputeImageSha256Async(candidate, CancellationToken.None, progress)
                    If String.Equals(expectedSha256, candidateSha256, StringComparison.OrdinalIgnoreCase) Then
                        Return Tuple.Create(candidate, candidateSha256)
                    End If
                Catch ex As Exception
                    _logService.WriteAsync($"SHA256 | CACHE_CANDIDATE | ERRO | {candidate} | {ex.Message}").GetAwaiter().GetResult()
                End Try
            Next

            Return Nothing
        End Function

        Private Function GetCacheFileCandidates(item As CatalogItem) As IEnumerable(Of String)
            Dim candidates As New List(Of String)()
            If item Is Nothing OrElse Not Directory.Exists(AppPaths.CacheDir) Then
                Return candidates
            End If

            Dim knownPath = GetDownloadedPath(item)
            If Not String.IsNullOrWhiteSpace(knownPath) AndAlso File.Exists(knownPath) AndAlso MatchesCatalogFileName(item, knownPath) Then
                candidates.Add(knownPath)
            End If

            Dim expectedSize = item.ImageSizeBytes
            For Each candidate In Directory.EnumerateFiles(AppPaths.CacheDir)
                If Not LooksLikeImageCacheFile(candidate) Then
                    Continue For
                End If
                If Not MatchesCatalogFileName(item, candidate) Then
                    Continue For
                End If

                If expectedSize > 0 Then
                    Try
                        If New FileInfo(candidate).Length <> expectedSize Then
                            Continue For
                        End If
                    Catch
                        Continue For
                    End Try
                End If

                If Not candidates.Any(Function(existing) String.Equals(existing, candidate, StringComparison.OrdinalIgnoreCase)) Then
                    candidates.Add(candidate)
                End If
            Next

            Return candidates
        End Function

        Private Function LooksLikeImageCacheFile(filePath As String) As Boolean
            Dim extension = Path.GetExtension(filePath)
            Return String.Equals(extension, ".tibx", StringComparison.OrdinalIgnoreCase) OrElse
                   String.Equals(extension, ".iso", StringComparison.OrdinalIgnoreCase) OrElse
                   String.Equals(extension, ".img", StringComparison.OrdinalIgnoreCase)
        End Function

        Private Function FindStoredStatusEntryByFileNameAndSha256(statusMap As Dictionary(Of String, DownloadStatusEntry), expectedFileName As String, expectedSha256 As String) As DownloadStatusEntry
            If statusMap Is Nothing OrElse String.IsNullOrWhiteSpace(expectedSha256) Then
                Return Nothing
            End If

            For Each pair In statusMap
                Dim entry = pair.Value
                If entry Is Nothing OrElse String.IsNullOrWhiteSpace(entry.ImageSha256) OrElse String.IsNullOrWhiteSpace(entry.Path) Then
                    Continue For
                End If
                If Not File.Exists(entry.Path) Then
                    Continue For
                End If
                If Not HasValidImageFileName(entry.Path) Then
                    Continue For
                End If
                If Not String.IsNullOrWhiteSpace(expectedFileName) AndAlso Not SameFileName(entry.Path, expectedFileName) Then
                    Continue For
                End If
                If String.Equals(expectedSha256.Trim(), entry.ImageSha256.Trim(), StringComparison.OrdinalIgnoreCase) Then
                    Return entry
                End If
            Next

            Return Nothing
        End Function

        Private Function HasValidImageFileName(filePath As String) As Boolean
            If String.IsNullOrWhiteSpace(filePath) Then
                Return False
            End If

            Dim fileName = Path.GetFileName(filePath)
            Return Not String.IsNullOrWhiteSpace(fileName) AndAlso LooksLikeImageCacheFile(filePath)
        End Function

        Private Sub LinkEquivalentCatalogItemsBySha256(sourceItem As CatalogItem, filePath As String, imageSha256 As String, statusMap As Dictionary(Of String, DownloadStatusEntry))
            If sourceItem Is Nothing OrElse String.IsNullOrWhiteSpace(sourceItem.Sha256) OrElse String.IsNullOrWhiteSpace(filePath) OrElse Not File.Exists(filePath) Then
                Return
            End If
            If String.IsNullOrWhiteSpace(imageSha256) OrElse Not HasValidImageFileName(filePath) Then
                Return
            End If

            Dim timestamp = DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss")
            For Each catalogItem In _currentItems
                If catalogItem Is Nothing OrElse String.IsNullOrWhiteSpace(catalogItem.Sha256) Then
                    Continue For
                End If
                If Not String.Equals(sourceItem.Sha256.Trim(), catalogItem.Sha256.Trim(), StringComparison.OrdinalIgnoreCase) Then
                    Continue For
                End If
                If Not String.Equals(catalogItem.Sha256.Trim(), imageSha256.Trim(), StringComparison.OrdinalIgnoreCase) Then
                    Continue For
                End If
                If Not MatchesCatalogFileName(catalogItem, filePath) Then
                    Continue For
                End If

                Dim entry As DownloadStatusEntry = Nothing
                If Not statusMap.TryGetValue(catalogItem.Name, entry) OrElse entry Is Nothing Then
                    entry = New DownloadStatusEntry()
                    statusMap(catalogItem.Name) = entry
                ElseIf Not String.IsNullOrWhiteSpace(entry.Path) AndAlso File.Exists(entry.Path) AndAlso Not SameFileName(entry.Path, filePath) Then
                    Continue For
                End If

                If Not String.Equals(entry.ImageSha256, imageSha256, StringComparison.OrdinalIgnoreCase) Then
                    ' Imagem diferente da anterior: a verificacao de USB gravada antes nao vale mais.
                    entry.UsbSha256 = String.Empty
                    entry.UsbVerified = False
                    entry.UsbVerifiedAt = String.Empty
                End If
                entry.Downloaded = True
                entry.Md5Valid = True
                entry.Path = filePath
                entry.UpdatedAt = timestamp
                entry.ImageSha256 = imageSha256
                _downloadStatusByName(catalogItem.Name) = True
                _downloadPathByName(catalogItem.Name) = filePath
                If String.Equals(catalogItem.Sha256.Trim(), imageSha256.Trim(), StringComparison.OrdinalIgnoreCase) Then
                    _md5StatusByName(catalogItem.Name) = True
                End If
            Next
        End Sub

        Private Function MatchesCatalogFileName(item As CatalogItem, filePath As String) As Boolean
            If item Is Nothing OrElse String.IsNullOrWhiteSpace(item.FileName) Then
                Return True
            End If

            Return SameFileName(filePath, item.FileName)
        End Function

        Private Function IsStoredSha256StatusUsable(item As CatalogItem, entry As DownloadStatusEntry) As Boolean
            If item Is Nothing OrElse entry Is Nothing Then
                Return False
            End If

            If String.IsNullOrWhiteSpace(item.Sha256) OrElse String.IsNullOrWhiteSpace(entry.ImageSha256) Then
                Return False
            End If

            If Not String.Equals(item.Sha256.Trim(), entry.ImageSha256.Trim(), StringComparison.OrdinalIgnoreCase) Then
                Return False
            End If

            If String.IsNullOrWhiteSpace(entry.Path) OrElse Not File.Exists(entry.Path) Then
                Return False
            End If

            Return MatchesCatalogFileName(item, entry.Path)
        End Function

        Private Function SameFileName(leftPath As String, rightPath As String) As Boolean
            Return String.Equals(Path.GetFileName(leftPath), Path.GetFileName(rightPath), StringComparison.OrdinalIgnoreCase)
        End Function

        Private Sub SaveDownloadStatusMap(data As Dictionary(Of String, DownloadStatusEntry))
            Dim statusPath = AppPaths.DownloadStatusPath
            Directory.CreateDirectory(AppPaths.ConfigDir)
            Dim options = New System.Text.Json.JsonSerializerOptions() With {
                .WriteIndented = True,
                .PropertyNameCaseInsensitive = True
            }
            File.WriteAllText(statusPath, System.Text.Json.JsonSerializer.Serialize(data, options))
        End Sub

        Private Class DownloadStatusEntry
            Public Property Downloaded As Boolean
            Public Property Md5Valid As Boolean?
            Public Property Md5Cache As String
            Public Property Path As String
            Public Property UpdatedAt As String
            Public Property ImageSha256 As String
            Public Property UsbSha256 As String
            Public Property UsbVerified As Boolean
            Public Property UsbVerifiedAt As String
            ' Campos que esta tela nao usa (por exemplo, o historico de gravacao do USB) voltam intactos ao gravar.
            <System.Text.Json.Serialization.JsonExtensionData>
            Public Property Extra As Dictionary(Of String, System.Text.Json.JsonElement)
        End Class

        Private Sub SuggestDownloadsIfMd5Mismatch()
            Dim mismatchCount = GetMd5MismatchCount()

            If mismatchCount <= 0 Then
                Return
            End If

            MessageBox.Show($"Foram encontradas {mismatchCount} imagens com MD5 divergente do servidor.{Environment.NewLine}Recomenda-se baixar novamente.", "Atualizacao de imagens", MessageBoxButtons.OK, MessageBoxIcon.Information)
        End Sub

        Private Function GetMd5MismatchCount() As Integer
            Dim mismatchCount = 0
            ' O status carregado inclui itens de outros perfis; conta apenas os do perfil atual.
            Dim currentNames As New HashSet(Of String)(_currentItems.Select(Function(item) item.Name), StringComparer.OrdinalIgnoreCase)
            For Each pair In _md5StatusByName
                Dim name = pair.Key
                If Not currentNames.Contains(name) Then
                    Continue For
                End If
                Dim valid = pair.Value
                If valid.HasValue AndAlso Not valid.Value AndAlso _downloadStatusByName.ContainsKey(name) AndAlso _downloadStatusByName(name) Then
                    mismatchCount += 1
                End If
            Next
            Return mismatchCount
        End Function

        Private Function GetCacheFilePath(fileUrl As String) As String
            Directory.CreateDirectory(AppPaths.CacheDir)
            Dim fileName = Path.GetFileName(New Uri(fileUrl).AbsolutePath)
            Return Path.Combine(AppPaths.CacheDir, fileName)
        End Function

        Private Function GetCatalogCacheFilePath(item As CatalogItem) As String
            Directory.CreateDirectory(AppPaths.CacheDir)
            If item IsNot Nothing AndAlso Not String.IsNullOrWhiteSpace(item.FileName) Then
                Return Path.Combine(AppPaths.CacheDir, Path.GetFileName(item.FileName.Trim()))
            End If

            Return GetCacheFilePath(item.FileUrl)
        End Function

        Private Function GetDownloadedPath(item As CatalogItem) As String
            If item Is Nothing Then
                Return String.Empty
            End If

            If _downloadPathByName.ContainsKey(item.Name) AndAlso MatchesCatalogFileName(item, _downloadPathByName(item.Name)) Then
                Return _downloadPathByName(item.Name)
            End If

            Return GetCatalogCacheFilePath(item)
        End Function

        Private Async Function FetchMd5Async(md5Url As String) As Task(Of String)
            Dim content = Await GetStringWithRedirectAsync(md5Url, 0)
            Return ParseMd5(content)
        End Function

        Private Function ParseMd5(content As String) As String
            If String.IsNullOrWhiteSpace(content) Then
                Return String.Empty
            End If

            Dim match = Regex.Match(content, "\b[a-fA-F0-9]{32}\b")
            If match.Success Then
                Return match.Value.Trim()
            End If

            Dim tokens = content.Trim().Split({" "c, ControlChars.Tab, ControlChars.Cr, ControlChars.Lf}, StringSplitOptions.RemoveEmptyEntries)
            If tokens.Length = 0 Then
                Return String.Empty
            End If

            Return tokens(0).Trim()
        End Function

        Private Async Function GetStringWithRedirectAsync(url As String, depth As Integer) As Task(Of String)
            If depth > 3 Then
                Throw New InvalidOperationException("Numero maximo de redirecionamentos excedido.")
            End If

            Dim response = Await _md5Client.GetAsync(url)
            response.EnsureSuccessStatusCode()

            Dim mediaType = response.Content.Headers.ContentType?.MediaType
            Dim content = Await response.Content.ReadAsStringAsync()
            If Not String.IsNullOrWhiteSpace(mediaType) AndAlso mediaType.StartsWith("text/html", StringComparison.OrdinalIgnoreCase) Then
                Dim redirectedUrl = TryExtractRedirectUrl(content)
                If Not String.IsNullOrWhiteSpace(redirectedUrl) Then
                    Return Await GetStringWithRedirectAsync(redirectedUrl, depth + 1)
                End If
            End If

            Return content
        End Function

        Private Function TryExtractRedirectUrl(html As String) As String
            If String.IsNullOrWhiteSpace(html) Then
                Return String.Empty
            End If

            Dim patterns = New String() {
                "window\.location\.replace\(['""](?<url>[^'""]+)['""]\)",
                "redirect\(['""](?<url>[^'""]+)['""]\)",
                "href=['""](?<url>https?://[^'""]+\.(?:iso|img|zip|exe|tibx|bin|md5|txt)[^'""]*)['""]"
            }

            For Each pattern In patterns
                Dim match = Regex.Match(html, pattern, RegexOptions.IgnoreCase)
                If match.Success Then
                    Dim value = match.Groups("url").Value
                    If Not String.IsNullOrWhiteSpace(value) Then
                        Dim normalizedUrl = NormalizeRedirectUrl(value)
                        If Not String.IsNullOrWhiteSpace(normalizedUrl) Then
                            Return normalizedUrl
                        End If
                    End If
                End If
            Next

            Return String.Empty
        End Function

        Private Function NormalizeRedirectUrl(value As String) As String
            If String.IsNullOrWhiteSpace(value) Then
                Return String.Empty
            End If

            Dim candidates As New List(Of String) From {value}
            Try
                Dim decoded = JsonSerializer.Deserialize(Of String)("""" & value & """")
                If Not String.IsNullOrWhiteSpace(decoded) Then
                    candidates.Insert(0, decoded)
                End If
            Catch
            End Try

            For Each candidate In candidates
                Dim normalized = candidate.Trim()
                Dim redirectUri As Uri = Nothing
                If Uri.TryCreate(normalized, UriKind.Absolute, redirectUri) AndAlso
                   redirectUri.Scheme = Uri.UriSchemeHttps Then
                    Return normalized
                End If
            Next

            Return String.Empty
        End Function
    End Class
End Namespace


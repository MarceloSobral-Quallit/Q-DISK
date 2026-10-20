' ─────────────────────────────────────────────────────────────────────────────
' qdisk  |  UI / Views  |  SettingsView.vb
' View de configurações persistíveis da aplicação.
'
' © Quallit — Licenciado sob GPL-3.0-or-later.
' ─────────────────────────────────────────────────────────────────────────────

Imports System.Drawing
Imports System.IO
Imports System.Threading
Imports System.Windows.Forms
Imports qdisk.Core.Models
Imports qdisk.Core.Services
Imports qdisk.UI.Services
Imports qdisk.UI.Theme

Namespace Views
    Partial Public Class SettingsView
        Inherits UserControl

        Private ReadOnly _settingsService As SettingsService
        Private ReadOnly _logService As LogService
        Private _loadingSettings As Boolean

        Public Sub New()
            Try
                Console.WriteLine("SettingsView: Iniciando InitializeComponent...")
                InitializeComponent()
                Console.WriteLine("SettingsView: InitializeComponent concluído")
                _settingsService = New SettingsService()
                _logService = New LogService()
                Console.WriteLine("SettingsView: Serviços criados")

                LoadSettings()
                ApplyRedesign()
                Console.WriteLine("SettingsView: LoadSettings concluído")
            Catch ex As Exception
                Console.WriteLine($"ERRO NO SETTINGSVIEW: {ex.Message}")
                Console.WriteLine($"STACK TRACE: {ex.StackTrace}")
                Console.WriteLine($"INNER: {If(ex.InnerException?.Message, "Nenhuma")}")
                Throw
            End Try
        End Sub

        ''' <summary>Redesenho (HANDOFF 5.5): secoes com titulo; nenhuma linha passa da largura do conteudo.</summary>
        Private Sub ApplyRedesign()
            ContentPanel.SuspendLayout()
            ContentPanel.Controls.Clear()
            ContentPanel.Padding = New Padding(0, 0, 8, 8)
            ' Sem barra horizontal: desliga antes de reativar o AutoScroll (truque conhecido do WinForms)
            ContentPanel.AutoScroll = False
            ContentPanel.HorizontalScroll.Enabled = False
            ContentPanel.HorizontalScroll.Visible = False
            ContentPanel.HorizontalScroll.Maximum = 0
            ContentPanel.AutoScroll = True
            LabelTitle.ForeColor = UiTheme.TitleText

            Dim fullWidth As New List(Of Control)

            Dim addSection = Sub(title As String)
                                 Dim host = New Panel() With {.Height = 30, .Margin = New Padding(0, 10, 0, 6)}
                                 Dim lbl = New Label() With {.Text = title, .Dock = DockStyle.Fill, .TextAlign = ContentAlignment.BottomLeft, .Padding = New Padding(0, 0, 0, 4)}
                                 UiTheme.StyleSectionLabel(lbl)
                                 Dim line = UiTheme.CreateDivider()
                                 line.Dock = DockStyle.Bottom
                                 host.Controls.Add(lbl)
                                 host.Controls.Add(line)
                                 ContentPanel.Controls.Add(host)
                                 fullWidth.Add(host)
                             End Sub
            Dim addItems = Sub(items As Control())
                               For Each c In items
                                   c.Margin = New Padding(0, 0, 0, 2)
                                   c.Padding = New Padding(0, 2, 0, 2)
                                   ContentPanel.Controls.Add(c)
                               Next
                           End Sub
            Dim styleButtons = Sub(kind As ButtonKind, iconName As String, iconVariant As String, buttons As Button())
                                   For Each b In buttons
                                       UiStatus.StyleAutoButton(b, kind, If(iconName Is Nothing, Nothing, UiTheme.Icon16(Me, iconName, iconVariant)))
                                       b.Height = UiTheme.ButtonHeight
                                       b.Margin = New Padding(0, 6, 8, 4)
                                   Next
                               End Sub

            ' 1. Pasta dos arquivos ISO/TIBX: caixa de texto elastica + Procurar + Padrao
            addSection(LabelImageCacheDir.Text)
            Dim pathRow = New TableLayoutPanel() With {.ColumnCount = 3, .RowCount = 1, .Height = 40, .Margin = New Padding(0, 0, 0, 2)}
            pathRow.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100.0F))
            pathRow.ColumnStyles.Add(New ColumnStyle(SizeType.AutoSize))
            pathRow.ColumnStyles.Add(New ColumnStyle(SizeType.AutoSize))
            pathRow.RowStyles.Add(New RowStyle(SizeType.Percent, 100.0F))
            CachePathPanel.Controls.Clear()
            TextImageCacheDir.Dock = DockStyle.None
            TextImageCacheDir.Anchor = AnchorStyles.Left Or AnchorStyles.Right
            TextImageCacheDir.Margin = New Padding(0, 0, 8, 0)
            styleButtons(ButtonKind.Secondary, "folder", "text", {ButtonBrowseImageCacheDir})
            styleButtons(ButtonKind.Secondary, Nothing, Nothing, {ButtonResetImageCacheDir})
            For Each b In New Button() {ButtonBrowseImageCacheDir, ButtonResetImageCacheDir}
                b.Anchor = AnchorStyles.Left
                b.Margin = New Padding(0, 0, If(b Is ButtonResetImageCacheDir, 0, 8), 0)
            Next
            pathRow.Controls.Add(TextImageCacheDir, 0, 0)
            pathRow.Controls.Add(ButtonBrowseImageCacheDir, 1, 0)
            pathRow.Controls.Add(ButtonResetImageCacheDir, 2, 0)
            RoundedInputHost.Wrap(TextImageCacheDir)
            ContentPanel.Controls.Add(pathRow)
            fullWidth.Add(pathRow)

            ' 2. Atualizacoes
            addSection("Atualizações")
            addItems({CheckAutoMd5OnStartup, CheckAutoUpdateApp})
            styleButtons(ButtonKind.Secondary, "refresh", "text", {ButtonCheckAppUpdate})
            ContentPanel.Controls.Add(ButtonCheckAppUpdate)

            ' 3. Validacao de hash
            addSection("Validação de hash")
            addItems({CheckAutoMd5AfterCacheRefresh, CheckAutoSha256, CheckAutoMd5BeforeIsoBurn, CheckAutoVerifySha256, CheckAutoMd5AfterCopy, CheckVerifyExistingTibxBeforeCopy})

            ' 4. Operacoes USB
            addSection("Operações USB")
            addItems({CheckAutoDisableAutoplay, CheckShowIdentifyUsbButton, CheckShowValidateBootButton})

            ' 5. Download
            addSection("Download")
            addItems({CheckEnableMultiConnection})
            MultiConnectionPanel.Margin = New Padding(21, 0, 0, 2)
            MultiConnectionPanel.Padding = New Padding(0)
            LabelMaxConnections.Padding = New Padding(0, 9, 6, 0)
            Dim nudHost = RoundedInputHost.Wrap(NumericMaxConnections, 76)
            nudHost.Margin = New Padding(0, 2, 0, 2)
            ContentPanel.Controls.Add(MultiConnectionPanel)

            ' 6. Manutencao
            addSection("Manutenção")
            Dim maintenance = New FlowLayoutPanel() With {.AutoSize = True, .WrapContents = True, .Margin = New Padding(0)}
            styleButtons(ButtonKind.Danger, "eraser", "red", {ButtonClearCache})
            styleButtons(ButtonKind.Secondary, Nothing, Nothing, {ButtonClearLog})
            styleButtons(ButtonKind.Secondary, "refresh", "text", {ButtonRestoreDefaults})
            maintenance.Controls.AddRange({ButtonClearCache, ButtonClearLog, ButtonRestoreDefaults})
            ContentPanel.Controls.Add(maintenance)

            Dim fit = Sub()
                          Dim w = ContentPanel.ClientSize.Width - ContentPanel.Padding.Horizontal - SystemInformation.VerticalScrollBarWidth
                          For Each c In fullWidth
                              c.Width = Math.Max(200, w)
                          Next
                          maintenance.MaximumSize = New Size(Math.Max(200, w), 0)
                      End Sub
            AddHandler ContentPanel.Resize, Sub() fit()
            fit()
            ContentPanel.ResumeLayout(True)
        End Sub

        Private Sub OnRestoreDefaultsClick(sender As Object, e As EventArgs)
            Dim confirm = MessageBox.Show("Deseja restaurar as configurações padrão?", "Configurações",
                                          MessageBoxButtons.YesNo, MessageBoxIcon.Warning)
            If confirm <> DialogResult.Yes Then
                Return
            End If

            Dim defaults = AppSettings.CreateDefault()
            _settingsService.Save(defaults)
            _logService.WriteAsync("SETTINGS | RESTAURADO_PADRAO").GetAwaiter().GetResult()
            LoadSettings()
            MessageBox.Show("Configurações restauradas.", "Configurações", MessageBoxButtons.OK, MessageBoxIcon.Information)
        End Sub

        Private Sub LoadSettings()
            _loadingSettings = True
            Try
                Dim settings = _settingsService.Load()
                CheckAutoSha256.Checked = settings.AutoCalculateSha256AfterDownload
                CheckAutoMd5.Checked = CheckAutoSha256.Checked
                CheckAutoVerifySha256.Checked = settings.AutoVerifySha256AfterUsbBurn
                CheckAutoMd5AfterCopy.Checked = settings.AutoValidateSha256AfterTibxCopy
                CheckVerifyExistingTibxBeforeCopy.Checked = settings.AutoVerifyExistingTibxBeforeCopy
                CheckAutoMd5AfterCacheRefresh.Checked = settings.AutoValidateMd5AfterCacheRefresh
                CheckAutoMd5OnStartup.Checked = settings.AutoValidateMd5OnStartup
                CheckAutoMd5BeforeIsoBurn.Checked = settings.AutoValidateSha256BeforeIsoBurn
                CheckAutoDisableAutoplay.Checked = settings.AutoDisableAutoplayDuringUsbOps
                CheckAutoUpdateApp.Checked = settings.AutoUpdateApp
                CheckEnableMultiConnection.Checked = settings.EnableMultiConnectionDownload
                NumericMaxConnections.Value = Math.Max(2, Math.Min(8, settings.MaxDownloadConnections))
                NumericMaxConnections.Enabled = CheckEnableMultiConnection.Checked
                TextImageCacheDir.Text = SettingsService.ResolveImageCacheDir(settings.ImageCacheDir)
                CheckShowIdentifyUsbButton.Checked = settings.ShowIdentifyUsbButton
                CheckShowValidateBootButton.Checked = settings.ShowValidateBootButton
            Finally
                _loadingSettings = False
            End Try
        End Sub

        Private Sub OnAutoMd5Changed(sender As Object, e As EventArgs)
            If _loadingSettings Then
                Return
            End If

            Dim currentSettings = _settingsService.Load()
            Dim settings = New AppSettings() With {
                .AutoValidateMd5AfterDownload = CheckAutoSha256.Checked,
                .AutoValidateSha256AfterTibxCopy = CheckAutoMd5AfterCopy.Checked,
                .AutoVerifyExistingTibxBeforeCopy = CheckVerifyExistingTibxBeforeCopy.Checked,
                .AutoValidateMd5AfterCacheRefresh = CheckAutoMd5AfterCacheRefresh.Checked,
                .AutoValidateMd5OnStartup = CheckAutoMd5OnStartup.Checked,
                .AutoValidateSha256BeforeIsoBurn = CheckAutoMd5BeforeIsoBurn.Checked,
                .AutoDisableAutoplayDuringUsbOps = CheckAutoDisableAutoplay.Checked,
                .AutoCalculateSha256AfterDownload = CheckAutoSha256.Checked,
                .AutoVerifySha256AfterUsbBurn = CheckAutoVerifySha256.Checked,
                .AutoUpdateApp = CheckAutoUpdateApp.Checked,
                .ImageCacheDir = currentSettings.ImageCacheDir,
                .EnableMultiConnectionDownload = CheckEnableMultiConnection.Checked,
                .MaxDownloadConnections = CInt(NumericMaxConnections.Value),
                .ShowIdentifyUsbButton = CheckShowIdentifyUsbButton.Checked,
                .ShowValidateBootButton = CheckShowValidateBootButton.Checked
            }
            _settingsService.Save(settings)
            _logService.WriteAsync($"SETTINGS | ATUALIZADO | Download={settings.AutoValidateMd5AfterDownload} | TIBX_SHA256={settings.AutoValidateSha256AfterTibxCopy} | TIBX_EXISTING_SHA256={settings.AutoVerifyExistingTibxBeforeCopy} | Cache={settings.AutoValidateMd5AfterCacheRefresh} | Startup={settings.AutoValidateMd5OnStartup} | ISO_SHA256={settings.AutoValidateSha256BeforeIsoBurn} | Autoplay={settings.AutoDisableAutoplayDuringUsbOps} | SHA256={settings.AutoCalculateSha256AfterDownload} | VerifyUSB={settings.AutoVerifySha256AfterUsbBurn} | AppUpdate={settings.AutoUpdateApp} | MultiConn={settings.EnableMultiConnectionDownload}/{settings.MaxDownloadConnections} | ShowIdentifyUsb={settings.ShowIdentifyUsbButton} | ShowValidateBoot={settings.ShowValidateBootButton}").GetAwaiter().GetResult()
        End Sub

        Private Sub OnEnableMultiConnectionChanged(sender As Object, e As EventArgs)
            NumericMaxConnections.Enabled = CheckEnableMultiConnection.Checked
        End Sub

        Private Sub OnBrowseImageCacheDirClick(sender As Object, e As EventArgs)
            Dim currentPath As String
            Try
                currentPath = SettingsService.ResolveImageCacheDir(TextImageCacheDir.Text)
                Directory.CreateDirectory(currentPath)
            Catch
                currentPath = SettingsService.ResolveImageCacheDir(_settingsService.Load().ImageCacheDir)
            End Try

            Using dialog As New FolderBrowserDialog()
                dialog.Description = "Selecione a pasta dos arquivos ISO/TIBX"
                dialog.SelectedPath = currentPath
                dialog.ShowNewFolderButton = True
                If dialog.ShowDialog(Me) = DialogResult.OK Then
                    TextImageCacheDir.Text = dialog.SelectedPath
                    SaveImageCacheDirFromText()
                End If
            End Using
        End Sub

        Private Sub OnResetImageCacheDirClick(sender As Object, e As EventArgs)
            TextImageCacheDir.Text = AppSettings.GetDefaultImageCacheDir()
            SaveImageCacheDirFromText()
        End Sub

        Private Sub OnImageCacheDirLeave(sender As Object, e As EventArgs)
            SaveImageCacheDirFromText()
        End Sub

        Private Sub OnImageCacheDirKeyDown(sender As Object, e As KeyEventArgs)
            If e.KeyCode <> Keys.Enter Then
                Return
            End If

            e.SuppressKeyPress = True
            SaveImageCacheDirFromText()
        End Sub

        Private Sub SaveImageCacheDirFromText()
            If _loadingSettings Then
                Return
            End If

            Try
                Dim resolvedPath = SettingsService.ResolveImageCacheDir(TextImageCacheDir.Text)
                Directory.CreateDirectory(resolvedPath)

                Dim settings = _settingsService.Load()
                If String.Equals(settings.ImageCacheDir, resolvedPath, StringComparison.OrdinalIgnoreCase) Then
                    TextImageCacheDir.Text = resolvedPath
                    Return
                End If

                settings.ImageCacheDir = resolvedPath
                _settingsService.Save(settings)
                TextImageCacheDir.Text = resolvedPath
                _logService.WriteAsync($"SETTINGS | CACHE_DIR | {resolvedPath}").GetAwaiter().GetResult()
            Catch ex As Exception
                MessageBox.Show($"Pasta de arquivos invalida ou inacessivel: {ex.Message}", "Configurações", MessageBoxButtons.OK, MessageBoxIcon.Error)
                LoadSettings()
            End Try
        End Sub

        Private Async Sub OnCheckAppUpdateClick(sender As Object, e As EventArgs)
            ButtonCheckAppUpdate.Enabled = False
            Dim previousText = ButtonCheckAppUpdate.Text
            ButtonCheckAppUpdate.Text = "Verificando..."
            Try
                Await AppUpdateHelper.CheckAndNotifyAsync(Me, True, CancellationToken.None)
            Catch ex As OperationCanceledException
            Catch ex As Exception
                _logService.WriteAsync($"APP_UPDATE | CHECK | ERRO_UI | {ex.Message}").GetAwaiter().GetResult()
                MessageBox.Show($"Falha ao verificar atualizacao: {ex.Message}", "Atualizacao do aplicativo", MessageBoxButtons.OK, MessageBoxIcon.Error)
            Finally
                ButtonCheckAppUpdate.Text = previousText
                ButtonCheckAppUpdate.Enabled = True
            End Try
        End Sub

        Private Sub OnClearCacheClick(sender As Object, e As EventArgs)
            Dim confirm = MessageBox.Show("Deseja apagar todos os arquivos do cache de imagens?", "Configurações",
                                          MessageBoxButtons.YesNo, MessageBoxIcon.Warning)
            If confirm <> DialogResult.Yes Then
                Return
            End If

            Dim cachePath = AppPaths.CacheDir
            Dim statusPath = AppPaths.DownloadStatusPath
            Try
                If Directory.Exists(cachePath) Then
                    For Each filePath In Directory.GetFiles(cachePath)
                        File.Delete(filePath)
                    Next
                    For Each dirPath In Directory.GetDirectories(cachePath)
                        Directory.Delete(dirPath, True)
                    Next
                End If
                If File.Exists(statusPath) Then
                    File.Delete(statusPath)
                End If
                _logService.WriteAsync($"SETTINGS | CACHE_LIMPO | {cachePath}").GetAwaiter().GetResult()
                MessageBox.Show("Cache limpo com sucesso.", "Configurações", MessageBoxButtons.OK, MessageBoxIcon.Information)
            Catch ex As Exception
                _logService.WriteAsync($"SETTINGS | CACHE_LIMPO | ERRO | {ex.Message}").GetAwaiter().GetResult()
                MessageBox.Show($"Falha ao limpar o cache: {ex.Message}", "Configurações", MessageBoxButtons.OK, MessageBoxIcon.Error)
            End Try
        End Sub

        Private Sub OnClearLogClick(sender As Object, e As EventArgs)
            Dim confirm = MessageBox.Show("Deseja apagar o arquivo de log?", "Configurações",
                                          MessageBoxButtons.YesNo, MessageBoxIcon.Warning)
            If confirm <> DialogResult.Yes Then
                Return
            End If

            Dim logPath = AppPaths.LogPath
            Try
                If File.Exists(logPath) Then
                    File.Delete(logPath)
                End If
                _logService.WriteAsync("SETTINGS | LOG_LIMPO").GetAwaiter().GetResult()
                MessageBox.Show("Log limpo com sucesso.", "Configurações", MessageBoxButtons.OK, MessageBoxIcon.Information)
            Catch ex As Exception
                _logService.WriteAsync($"SETTINGS | LOG_LIMPO | ERRO | {ex.Message}").GetAwaiter().GetResult()
                MessageBox.Show($"Falha ao limpar o log: {ex.Message}", "Configurações", MessageBoxButtons.OK, MessageBoxIcon.Error)
            End Try
        End Sub
    End Class
End Namespace


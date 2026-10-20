' ─────────────────────────────────────────────────────────────────────────────
' qdisk  |  UI / Views  |  MainForm.vb
' Formulário principal com navegação lateral e gerenciamento de views.
'
' © Quallit — Licenciado sob GPL-3.0-or-later.
' ─────────────────────────────────────────────────────────────────────────────

Imports System.Drawing
Imports System.IO
Imports System.Reflection
Imports System.Threading
Imports System.Windows.Forms
Imports System.Security.Principal
Imports qdisk.Core.Models
Imports qdisk.Core.Services
Imports qdisk.UI.Services
Imports qdisk.UI.Theme

Namespace Views
    Partial Public Class MainForm
        Inherits Form

        Private ReadOnly _views As Dictionary(Of String, UserControl)
        Private ReadOnly _navButtons As Dictionary(Of String, Button)
        Private ReadOnly _navItems As New Dictionary(Of String, NavItem)(StringComparer.OrdinalIgnoreCase)
        Private _activeViewName As String
        Private _appState As AppState
        Private _versionText As String
        Private ReadOnly _logService As LogService
        Public Property ProfileChangeRequested As Boolean

        Public Sub New()
            _logService = New LogService()
            _versionText = GetAppVersion()
            _navButtons = New Dictionary(Of String, Button)(StringComparer.OrdinalIgnoreCase)
            InitializeComponent()
            ApplyFrameStyle()
            BuildNavigation()
            _views = New Dictionary(Of String, UserControl)(StringComparer.OrdinalIgnoreCase)
            _appState = New AppState() With {
                .AuthenticatedUser = "local",
                .SelectedUsbDrive = Nothing,
                .CacheItemsCount = 0,
                .UpdateStatus = "desconhecida"
            }

            Text = $"Q-DISK {_versionText}"
            SetAppIcon()
            UpdateVersionInfo()
            _logService.WriteAsync("APP | START").GetAwaiter().GetResult()
            If IsRunningAsAdmin() Then
                _logService.WriteAsync("APP | START | ADMIN").GetAwaiter().GetResult()
            End If

            _logService.WriteAsync("APP | INIT_VIEWS | BEGIN").GetAwaiter().GetResult()
            InitializeViews()
            _logService.WriteAsync("APP | INIT_VIEWS | OK").GetAwaiter().GetResult()
            ApplyState()
            AddHandler Me.Shown, AddressOf OnMainFormShown
            _logService.WriteAsync("APP | READY").GetAwaiter().GetResult()
        End Sub

        Private Async Sub OnMainFormShown(sender As Object, e As EventArgs)
            RemoveHandler Me.Shown, AddressOf OnMainFormShown
            Try
                Dim settings = New SettingsService().Load()
                If settings.AutoUpdateApp Then
                    Await AppUpdateHelper.CheckAndNotifyAsync(Me, False, CancellationToken.None)
                End If
            Catch ex As OperationCanceledException
            Catch ex As Exception
                _logService.WriteAsync($"APP_UPDATE | STARTUP | ERRO | {ex.Message}").GetAwaiter().GetResult()
            End Try
            Await CheckImageUpdatesOnStartupAsync()
        End Sub

        ''' <summary>"Verificar atualizacoes de imagens na inicializacao": avisa as imagens baixadas com versao nova no catalogo.</summary>
        Private Async Function CheckImageUpdatesOnStartupAsync() As Task
            Try
                If Not New SettingsService().Load().AutoValidateMd5OnStartup OrElse IsDisposed Then Return
                Dim outdated = Await ImageUpdateNotifier.FindOutdatedImagesAsync(CancellationToken.None)
                If outdated.Count = 0 OrElse IsDisposed Then Return

                Dim names = String.Join(Environment.NewLine, outdated.Select(Function(image) $"- {image.FileName}"))
                Dim answer = MessageBox.Show(Me,
                    $"Há versão nova no catálogo para {outdated.Count} imagem(ns) já baixada(s):{Environment.NewLine}{names}" &
                    $"{Environment.NewLine}{Environment.NewLine}Abrir a tela Downloads para baixar novamente?",
                    "Atualização de imagens", MessageBoxButtons.YesNo, MessageBoxIcon.Information)
                _logService.WriteAsync($"IMAGES | STARTUP_CHECK | AVISO | Abrir={answer = DialogResult.Yes}").GetAwaiter().GetResult()
                If answer = DialogResult.Yes AndAlso _navButtons.ContainsKey("Downloads") AndAlso _navButtons("Downloads").Enabled Then
                    SetActiveView("Downloads")
                End If
            Catch ex As Exception
                _logService.WriteAsync($"IMAGES | STARTUP_CHECK | ERRO | {ex.Message}").GetAwaiter().GetResult()
            End Try
        End Function

        Private Sub BuildNavigation()
            Dim navFlow = New FlowLayoutPanel() With {
                .Dock = DockStyle.Fill,
                .FlowDirection = FlowDirection.TopDown,
                .WrapContents = False,
                .AutoScroll = True,
                .Padding = New Padding(8, 12, 8, 12),
                .BackColor = UiTheme.NavBack
            }

            Dim viewNames = New String() {
                "Inicio",
                "Downloads",
                "Copiar Backup TIBX",
                "Gravar ISO Bootável",
                "Teste de velocidade",
                "Logs",
                "Configurações",
                "Sobre",
                "Manual"
            }

            ' Icone de cada tela (icons/ICONES.md) e separadores depois destes itens.
            Dim navIcons = New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase) From {
                {"Inicio", "home"}, {"Downloads", "download"}, {"Copiar Backup TIBX", "copy"},
                {"Gravar ISO Bootável", "disc"}, {"Teste de velocidade", "gauge"}, {"Logs", "file"},
                {"Configurações", "sliders"}, {"Sobre", "info"}, {"Manual", "book"}
            }
            Dim separatorsAfter = New HashSet(Of String)(StringComparer.OrdinalIgnoreCase) From {
                "Inicio", "Teste de velocidade", "Configurações"
            }

            For Each viewName In viewNames
                Dim iconName As String = Nothing
                navIcons.TryGetValue(viewName, iconName)
                Dim item = New NavItem(viewName,
                                       If(iconName Is Nothing, Nothing, UiTheme.Icon16(Me, iconName, "dark")),
                                       If(iconName Is Nothing, Nothing, UiTheme.Icon16(Me, iconName, "teal")))
                AddHandler item.Button.Click, AddressOf OnNavClick
                navFlow.Controls.Add(item)
                _navButtons(viewName) = item.Button
                _navItems(viewName) = item
                If separatorsAfter.Contains(viewName) Then
                    navFlow.Controls.Add(NavItem.CreateSeparator())
                End If
            Next

            _navPanel.Controls.Add(navFlow)
            Dim navBorder = UiTheme.CreateDivider(vertical:=True)
            navBorder.Dock = DockStyle.Right
            _navPanel.Controls.Add(navBorder)
        End Sub

        ''' <summary>Redesenho: logo no cabeçalho, Alterar Perfil com margem e barra de status.</summary>
        Private Sub ApplyFrameStyle()
            _headerPanel.BackColor = UiTheme.Navy
            _navPanel.BackColor = UiTheme.NavBack

            Dim logoPath = Path.Combine(AppPaths.AssetsDir, "logo-quallit-512x512.png")
            If File.Exists(logoPath) Then
                Dim logoHost = New Panel() With {.Dock = DockStyle.Left, .Width = 16 + 28, .Padding = New Padding(16, 14, 0, 14), .BackColor = UiTheme.Navy}
                Dim logo = New PictureBox() With {.Dock = DockStyle.Fill, .SizeMode = PictureBoxSizeMode.Zoom, .BackColor = UiTheme.Navy}
                Using fs = New FileStream(logoPath, FileMode.Open, FileAccess.Read)
                    logo.Image = New Bitmap(Image.FromStream(fs))
                End Using
                logoHost.Controls.Add(logo)
                _headerPanel.Controls.Add(logoHost)
                logoHost.SendToBack()
            End If
            _titleLabel.Padding = New Padding(12, 0, 0, 0)

            _headerPanel.Controls.Remove(_btnChangeProfile)
            Dim profileHost = New Panel() With {.Dock = DockStyle.Right, .Padding = New Padding(0, 12, 16, 12), .BackColor = UiTheme.Navy}
            UiTheme.StyleButton(_btnChangeProfile, ButtonKind.Header, UiTheme.Icon16(Me, "swap", "white"))
            _btnChangeProfile.Dock = DockStyle.Fill
            _btnChangeProfile.Height = 32
            profileHost.Width = TextRenderer.MeasureText(_btnChangeProfile.Text, _btnChangeProfile.Font).Width + 16 + 6 + 30 + 16
            profileHost.Controls.Add(_btnChangeProfile)
            _headerPanel.Controls.Add(profileHost)
            _titleLabel.BringToFront()

            UiTheme.StyleStatusStrip(_statusStrip, _statusReady, UiTheme.Icon16(Me, "shield", "warn"))
        End Sub

        Private Function IsRunningAsAdmin() As Boolean
            Try
                Dim identity = WindowsIdentity.GetCurrent()
                Dim principal = New WindowsPrincipal(identity)
                Return principal.IsInRole(WindowsBuiltInRole.Administrator)
            Catch
                Return False
            End Try
        End Function

        Private Sub OnNavClick(sender As Object, e As EventArgs)
            Dim button = TryCast(sender, Button)
            If button Is Nothing Then
                Return
            End If

            Dim viewName = TryCast(button.Tag, String)
            If String.IsNullOrWhiteSpace(viewName) Then
                Return
            End If

            SetActiveView(viewName)
            ActiveControl = Nothing
        End Sub

        Private Sub SetActiveView(viewName As String)
            If String.Equals(_activeViewName, viewName, StringComparison.OrdinalIgnoreCase) Then
                Return
            End If

            If Not String.IsNullOrWhiteSpace(_activeViewName) AndAlso _views.ContainsKey(_activeViewName) Then
                _views(_activeViewName).Visible = False
            End If

            _activeViewName = viewName
            _titleLabel.Text = viewName

            If Not _views.ContainsKey(viewName) Then
                Dim createdView = CreateView(viewName)
                If createdView IsNot Nothing Then
                    createdView.Dock = DockStyle.Fill
                    createdView.Visible = False
                    _contentPanel.Controls.Add(createdView)
                    _views(viewName) = createdView
                End If
            End If

            If _views.ContainsKey(viewName) Then
                _views(viewName).Visible = True
                _views(viewName).BringToFront()
                _contentLabel.Visible = False
                If String.Equals(viewName, "Downloads", StringComparison.OrdinalIgnoreCase) Then
                    Dim downloadsView = TryCast(_views(viewName), DownloadsView)
                    downloadsView?.ReloadList()
                ElseIf String.Equals(viewName, "Copiar Backup TIBX", StringComparison.OrdinalIgnoreCase) OrElse
                       String.Equals(viewName, "Gravar ISO Bootável", StringComparison.OrdinalIgnoreCase) Then
                    Dim usbView = TryCast(_views(viewName), UsbBootView)
                    usbView?.OnViewShown()
                End If
            Else
                _contentLabel.Text = viewName
                _contentLabel.Visible = True
            End If

            UpdateNavStyles()
        End Sub

        Public Sub UpdateAppState(state As AppState)
            If state Is Nothing Then
                Return
            End If

            _appState = state
            ApplyState()
        End Sub

        Private Sub ApplyState()
            Dim isAuthenticated = Not String.IsNullOrWhiteSpace(_appState.AuthenticatedUser)
            Dim hasUsb = _appState.SelectedUsbDrive IsNot Nothing

            SetButtonEnabled("Downloads", isAuthenticated)
            SetButtonEnabled("Copiar Backup TIBX", isAuthenticated)
            SetButtonEnabled("Gravar ISO Bootável", isAuthenticated)
            SetButtonEnabled("Teste de velocidade", isAuthenticated)
            SetButtonEnabled("Logs", True)
            SetButtonEnabled("Configurações", True)
            SetButtonEnabled("Sobre", True)
            SetButtonEnabled("Manual", True)
            SetButtonEnabled("Inicio", True)

            UpdateStatusText()
        End Sub

        Private Sub SetButtonEnabled(viewName As String, enabled As Boolean)
            If _navButtons.ContainsKey(viewName) Then
                _navButtons(viewName).Enabled = enabled
            End If
        End Sub

        Private Sub UpdateStatusText()
            Dim isAdmin = IsRunningAsAdmin()
            _statusText.Text = String.Empty
            _statusReady.Text = If(isAdmin, "Modo Administrador", String.Empty)
            _statusReady.ForeColor = If(isAdmin, UiTheme.WarnText, SystemColors.ControlText)
            _statusReady.Visible = isAdmin
        End Sub

        Private Sub UpdateNavStyles()
            For Each item In _navItems
                item.Value.SetActive(String.Equals(item.Key, _activeViewName, StringComparison.OrdinalIgnoreCase))
            Next
        End Sub

        Private Sub OnChangeProfileClick(sender As Object, e As EventArgs)
            Dim confirm = MessageBox.Show(
                "Deseja trocar de perfil? A tela principal sera fechada e a tela de token sera exibida novamente.",
                "Alterar Perfil", MessageBoxButtons.YesNo, MessageBoxIcon.Question)
            If confirm <> DialogResult.Yes Then
                Return
            End If

            ProfileChangeRequested = True
            Me.Close()
        End Sub

        Protected Overrides Sub OnFormClosed(e As FormClosedEventArgs)
            _logService.WriteAsync(If(ProfileChangeRequested, "APP | ALTERAR_PERFIL | CLOSE", "APP | EXIT")).GetAwaiter().GetResult()
            MyBase.OnFormClosed(e)
        End Sub

        Private Sub InitializeViews()
            SetActiveView("Inicio")
        End Sub

        Private Function CreateView(viewName As String) As UserControl
            _logService.WriteAsync($"APP | CREATE_VIEW | BEGIN | {viewName}").GetAwaiter().GetResult()
            Dim view As UserControl = Nothing

            Select Case viewName
                Case "Inicio"
                    Dim dashboard = New DashboardView()
                    AddHandler dashboard.NavigateRequested, Sub(target) If _navButtons.ContainsKey(target) AndAlso _navButtons(target).Enabled Then SetActiveView(target)
                    view = dashboard
                Case "Downloads"
                    view = New DownloadsView()
                Case "Copiar Backup TIBX"
                    view = New UsbBootView(UsbBootView.UsbOperationViewMode.CopyTibx)
                Case "Gravar ISO Bootável"
                    view = New UsbBootView(UsbBootView.UsbOperationViewMode.BootIso)
                Case "Teste de velocidade"
                    view = New SpeedTestView()
                Case "Logs"
                    view = New LogsView()
                Case "Configurações"
                    view = New SettingsView()
                Case "Sobre"
                    view = New AboutView()
                Case "Manual"
                    view = New ManualView()
            End Select

            _logService.WriteAsync($"APP | CREATE_VIEW | OK | {viewName}").GetAwaiter().GetResult()
            Return view
        End Function

        Private Sub UpdateVersionInfo()
            Dim version = Assembly.GetExecutingAssembly().GetName().Version
            Dim versionText = If(AppVersionFormat.Format(version), "0.0.0.0")
            _logService.WriteAsync($"APP | VERSION_INFO | {versionText}").GetAwaiter().GetResult()
        End Sub

        Private Function GetAppVersion() As String
            Dim version = Assembly.GetExecutingAssembly().GetName().Version
            Return If(AppVersionFormat.Format(version), "0.0.0.0")
        End Function

        Private Sub SetAppIcon()
            ' Lê o ícone embutido no exe como recurso Win32 (<ApplicationIcon> no .vbproj)
            ' Funciona em publish single-file e em build normal, sem depender de arquivo externo
            Try
                Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath)
                Return
            Catch
            End Try
            ' Fallback: tenta carregar do sistema de arquivos
            Dim iconPath = Path.Combine(AppPaths.AssetsDir, "favicon.ico")
            If File.Exists(iconPath) Then
                Try
                    Icon = New Icon(iconPath)
                Catch
                End Try
            End If
        End Sub
    End Class
End Namespace


Imports System.Drawing
Imports System.Windows.Forms

Namespace Views
    <Global.Microsoft.VisualBasic.CompilerServices.DesignerGenerated()>
    Partial Public Class SettingsView
        Inherits UserControl

        Private components As System.ComponentModel.IContainer
        Friend WithEvents LayoutRoot As TableLayoutPanel
        Friend WithEvents LabelTitle As Label
        Friend WithEvents ContentPanel As FlowLayoutPanel
        Friend WithEvents CheckAutoSha256 As CheckBox
        Friend WithEvents CheckAutoVerifySha256 As CheckBox
        Friend WithEvents CheckAutoMd5AfterCopy As CheckBox
        Friend WithEvents CheckVerifyExistingTibxBeforeCopy As CheckBox
        Friend WithEvents CheckAutoMd5AfterCacheRefresh As CheckBox
        Friend WithEvents CheckAutoMd5OnStartup As CheckBox
        Friend WithEvents CheckAutoMd5BeforeIsoBurn As CheckBox
        Friend WithEvents CheckAutoDisableAutoplay As CheckBox
        Friend WithEvents CheckShowIdentifyUsbButton As CheckBox
        Friend WithEvents CheckShowValidateBootButton As CheckBox
        Friend WithEvents CheckAutoUpdateApp As CheckBox
        Friend WithEvents CheckEnableMultiConnection As CheckBox
        Friend WithEvents MultiConnectionPanel As FlowLayoutPanel
        Friend WithEvents LabelMaxConnections As Label
        Friend WithEvents NumericMaxConnections As NumericUpDown
        Friend WithEvents LabelImageCacheDir As Label
        Friend WithEvents CachePathPanel As FlowLayoutPanel
        Friend WithEvents TextImageCacheDir As TextBox
        Friend WithEvents ButtonBrowseImageCacheDir As Button
        Friend WithEvents ButtonResetImageCacheDir As Button
        Friend WithEvents ButtonCheckAppUpdate As Button
        Friend WithEvents ButtonClearCache As Button
        Friend WithEvents ButtonClearLog As Button
        Friend WithEvents ButtonRestoreDefaults As Button

        <System.Diagnostics.DebuggerNonUserCode()>
        Protected Overrides Sub Dispose(disposing As Boolean)
            Try
                If disposing AndAlso components IsNot Nothing Then
                    components.Dispose()
                End If
            Finally
                MyBase.Dispose(disposing)
            End Try
        End Sub

        <System.Diagnostics.DebuggerStepThrough()>
        Private Sub InitializeComponent()
            Me.LayoutRoot = New TableLayoutPanel()
            Me.LabelTitle = New Label()
            Me.ContentPanel = New FlowLayoutPanel()
            Me.CheckAutoSha256 = New CheckBox()
            Me.CheckAutoVerifySha256 = New CheckBox()
            Me.CheckAutoMd5AfterCopy = New CheckBox()
            Me.CheckVerifyExistingTibxBeforeCopy = New CheckBox()
            Me.CheckAutoMd5AfterCacheRefresh = New CheckBox()
            Me.CheckAutoMd5OnStartup = New CheckBox()
            Me.CheckAutoMd5BeforeIsoBurn = New CheckBox()
            Me.CheckAutoDisableAutoplay = New CheckBox()
            Me.CheckShowIdentifyUsbButton = New CheckBox()
            Me.CheckShowValidateBootButton = New CheckBox()
            Me.CheckAutoUpdateApp = New CheckBox()
            Me.CheckEnableMultiConnection = New CheckBox()
            Me.MultiConnectionPanel = New FlowLayoutPanel()
            Me.LabelMaxConnections = New Label()
            Me.NumericMaxConnections = New NumericUpDown()
            Me.LabelImageCacheDir = New Label()
            Me.CachePathPanel = New FlowLayoutPanel()
            Me.TextImageCacheDir = New TextBox()
            Me.ButtonBrowseImageCacheDir = New Button()
            Me.ButtonResetImageCacheDir = New Button()
            Me.ButtonCheckAppUpdate = New Button()
            Me.ButtonClearCache = New Button()
            Me.ButtonClearLog = New Button()
            Me.ButtonRestoreDefaults = New Button()
            Me.LayoutRoot.SuspendLayout()
            Me.ContentPanel.SuspendLayout()
            Me.MultiConnectionPanel.SuspendLayout()
            Me.CachePathPanel.SuspendLayout()
            Me.SuspendLayout()
            '
            'LayoutRoot
            '
            Me.LayoutRoot.ColumnCount = 1
            Me.LayoutRoot.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100.0!))
            Me.LayoutRoot.Dock = DockStyle.Fill
            Me.LayoutRoot.Padding = New Padding(16)
            Me.LayoutRoot.RowCount = 2
            Me.LayoutRoot.RowStyles.Add(New RowStyle(SizeType.Absolute, 32.0!))
            Me.LayoutRoot.RowStyles.Add(New RowStyle(SizeType.Percent, 100.0!))
            Me.LayoutRoot.Controls.Add(Me.LabelTitle, 0, 0)
            Me.LayoutRoot.Controls.Add(Me.ContentPanel, 0, 1)
            '
            'LabelTitle
            '
            Me.LabelTitle.Dock = DockStyle.Fill
            Me.LabelTitle.TextAlign = ContentAlignment.MiddleLeft
            Me.LabelTitle.Font = New Font("Segoe UI", 12.0!, FontStyle.Bold)
            Me.LabelTitle.ForeColor = Color.FromArgb(60, 70, 80)
            Me.LabelTitle.Text = "Configurações"
            '
            'ContentPanel
            '
            Me.ContentPanel.Dock = DockStyle.Fill
            Me.ContentPanel.FlowDirection = FlowDirection.TopDown
            Me.ContentPanel.WrapContents = False
            Me.ContentPanel.AutoScroll = True
            Me.ContentPanel.Controls.Add(Me.CheckAutoMd5OnStartup)
            Me.ContentPanel.Controls.Add(Me.CheckAutoMd5AfterCacheRefresh)
            Me.ContentPanel.Controls.Add(Me.CheckAutoSha256)
            Me.ContentPanel.Controls.Add(Me.CheckAutoMd5BeforeIsoBurn)
            Me.ContentPanel.Controls.Add(Me.CheckAutoVerifySha256)
            Me.ContentPanel.Controls.Add(Me.CheckAutoMd5AfterCopy)
            Me.ContentPanel.Controls.Add(Me.CheckVerifyExistingTibxBeforeCopy)
            Me.ContentPanel.Controls.Add(Me.CheckAutoDisableAutoplay)
            Me.ContentPanel.Controls.Add(Me.CheckAutoUpdateApp)
            Me.ContentPanel.Controls.Add(Me.CheckEnableMultiConnection)
            Me.ContentPanel.Controls.Add(Me.MultiConnectionPanel)
            Me.ContentPanel.Controls.Add(Me.LabelImageCacheDir)
            Me.ContentPanel.Controls.Add(Me.CachePathPanel)
            Me.ContentPanel.Controls.Add(Me.ButtonCheckAppUpdate)
            Me.ContentPanel.Controls.Add(Me.ButtonClearCache)
            Me.ContentPanel.Controls.Add(Me.ButtonClearLog)
            Me.ContentPanel.Controls.Add(Me.ButtonRestoreDefaults)
            Me.ContentPanel.Controls.Add(Me.CheckShowIdentifyUsbButton)
            Me.ContentPanel.Controls.Add(Me.CheckShowValidateBootButton)
            '
            'CheckAutoMd5OnStartup
            '
            Me.CheckAutoMd5OnStartup.Text = "Verificar atualizações de imagens na inicialização"
            Me.CheckAutoMd5OnStartup.AutoSize = True
            Me.CheckAutoMd5OnStartup.Padding = New Padding(4)
            AddHandler Me.CheckAutoMd5OnStartup.CheckedChanged, AddressOf Me.OnAutoMd5Changed
            '
            'CheckAutoMd5AfterCacheRefresh
            '
            Me.CheckAutoMd5AfterCacheRefresh.Text = "Validar hashes ao atualizar arquivos do cache"
            Me.CheckAutoMd5AfterCacheRefresh.AutoSize = True
            Me.CheckAutoMd5AfterCacheRefresh.Padding = New Padding(4)
            AddHandler Me.CheckAutoMd5AfterCacheRefresh.CheckedChanged, AddressOf Me.OnAutoMd5Changed
            '
            'CheckAutoSha256
            '
            Me.CheckAutoSha256.Text = "Validar hash após download"
            Me.CheckAutoSha256.AutoSize = True
            Me.CheckAutoSha256.Padding = New Padding(4)
            AddHandler Me.CheckAutoSha256.CheckedChanged, AddressOf Me.OnAutoMd5Changed
            '
            'CheckAutoMd5BeforeIsoBurn
            '
            Me.CheckAutoMd5BeforeIsoBurn.Text = "Validar hash antes da gravação ISO"
            Me.CheckAutoMd5BeforeIsoBurn.AutoSize = True
            Me.CheckAutoMd5BeforeIsoBurn.Padding = New Padding(4)
            AddHandler Me.CheckAutoMd5BeforeIsoBurn.CheckedChanged, AddressOf Me.OnAutoMd5Changed
            '
            'CheckAutoVerifySha256
            '
            Me.CheckAutoVerifySha256.Text = "Validar hash após gravação do USB bootável"
            Me.CheckAutoVerifySha256.AutoSize = True
            Me.CheckAutoVerifySha256.Padding = New Padding(4)
            AddHandler Me.CheckAutoVerifySha256.CheckedChanged, AddressOf Me.OnAutoMd5Changed
            '
            'CheckAutoMd5AfterCopy
            '
            Me.CheckAutoMd5AfterCopy.Text = "Validar hash após cópia TIBX"
            Me.CheckAutoMd5AfterCopy.AutoSize = True
            Me.CheckAutoMd5AfterCopy.Padding = New Padding(4)
            AddHandler Me.CheckAutoMd5AfterCopy.CheckedChanged, AddressOf Me.OnAutoMd5Changed
            '
            'CheckVerifyExistingTibxBeforeCopy
            '
            Me.CheckVerifyExistingTibxBeforeCopy.Text = "Validar hash quando TIBX ja existir no USB"
            Me.CheckVerifyExistingTibxBeforeCopy.AutoSize = True
            Me.CheckVerifyExistingTibxBeforeCopy.Padding = New Padding(4)
            AddHandler Me.CheckVerifyExistingTibxBeforeCopy.CheckedChanged, AddressOf Me.OnAutoMd5Changed
            '
            'CheckAutoDisableAutoplay
            '
            Me.CheckAutoDisableAutoplay.Text = "Desativar AutoPlay durante operações USB/ISO"
            Me.CheckAutoDisableAutoplay.AutoSize = True
            Me.CheckAutoDisableAutoplay.Padding = New Padding(4)
            AddHandler Me.CheckAutoDisableAutoplay.CheckedChanged, AddressOf Me.OnAutoMd5Changed
            '
            'CheckAutoUpdateApp
            '
            Me.CheckAutoUpdateApp.Text = "Verificar atualizações do aplicativo na inicialização"
            Me.CheckAutoUpdateApp.AutoSize = True
            Me.CheckAutoUpdateApp.Padding = New Padding(4)
            AddHandler Me.CheckAutoUpdateApp.CheckedChanged, AddressOf Me.OnAutoMd5Changed
            '
            'CheckEnableMultiConnection
            '
            Me.CheckEnableMultiConnection.Text = "Download multi-conexão (Dropbox e SharePoint)"
            Me.CheckEnableMultiConnection.AutoSize = True
            Me.CheckEnableMultiConnection.Padding = New Padding(4)
            AddHandler Me.CheckEnableMultiConnection.CheckedChanged, AddressOf Me.OnEnableMultiConnectionChanged
            AddHandler Me.CheckEnableMultiConnection.CheckedChanged, AddressOf Me.OnAutoMd5Changed
            '
            'MultiConnectionPanel
            '
            Me.MultiConnectionPanel.AutoSize = True
            Me.MultiConnectionPanel.FlowDirection = FlowDirection.LeftToRight
            Me.MultiConnectionPanel.WrapContents = False
            Me.MultiConnectionPanel.Padding = New Padding(4, 0, 4, 4)
            Me.MultiConnectionPanel.Controls.Add(Me.LabelMaxConnections)
            Me.MultiConnectionPanel.Controls.Add(Me.NumericMaxConnections)
            '
            'LabelMaxConnections
            '
            Me.LabelMaxConnections.Text = "Conexões:"
            Me.LabelMaxConnections.AutoSize = True
            Me.LabelMaxConnections.Padding = New Padding(0, 7, 4, 0)
            '
            'NumericMaxConnections
            '
            Me.NumericMaxConnections.Minimum = 2
            Me.NumericMaxConnections.Maximum = 8
            Me.NumericMaxConnections.Value = 8
            Me.NumericMaxConnections.Width = 60
            Me.NumericMaxConnections.Margin = New Padding(0, 4, 4, 4)
            AddHandler Me.NumericMaxConnections.ValueChanged, AddressOf Me.OnAutoMd5Changed
            '
            'LabelImageCacheDir
            '
            Me.LabelImageCacheDir.Text = "Pasta dos arquivos ISO/TIBX"
            Me.LabelImageCacheDir.AutoSize = True
            Me.LabelImageCacheDir.Padding = New Padding(4, 12, 4, 0)
            '
            'CachePathPanel
            '
            Me.CachePathPanel.AutoSize = True
            Me.CachePathPanel.FlowDirection = FlowDirection.LeftToRight
            Me.CachePathPanel.WrapContents = False
            Me.CachePathPanel.Padding = New Padding(4, 0, 4, 4)
            Me.CachePathPanel.Controls.Add(Me.TextImageCacheDir)
            Me.CachePathPanel.Controls.Add(Me.ButtonBrowseImageCacheDir)
            Me.CachePathPanel.Controls.Add(Me.ButtonResetImageCacheDir)
            '
            'TextImageCacheDir
            '
            Me.TextImageCacheDir.Width = 520
            Me.TextImageCacheDir.Margin = New Padding(0, 4, 4, 4)
            AddHandler Me.TextImageCacheDir.Leave, AddressOf Me.OnImageCacheDirLeave
            AddHandler Me.TextImageCacheDir.KeyDown, AddressOf Me.OnImageCacheDirKeyDown
            '
            'ButtonBrowseImageCacheDir
            '
            Me.ButtonBrowseImageCacheDir.Text = "Procurar"
            Me.ButtonBrowseImageCacheDir.Width = 90
            Me.ButtonBrowseImageCacheDir.Height = 26
            Me.ButtonBrowseImageCacheDir.Margin = New Padding(4, 3, 4, 4)
            AddHandler Me.ButtonBrowseImageCacheDir.Click, AddressOf Me.OnBrowseImageCacheDirClick
            '
            'ButtonResetImageCacheDir
            '
            Me.ButtonResetImageCacheDir.Text = "Padrão"
            Me.ButtonResetImageCacheDir.Width = 80
            Me.ButtonResetImageCacheDir.Height = 26
            Me.ButtonResetImageCacheDir.Margin = New Padding(4, 3, 4, 4)
            AddHandler Me.ButtonResetImageCacheDir.Click, AddressOf Me.OnResetImageCacheDirClick
            '
            'ButtonCheckAppUpdate
            '
            Me.ButtonCheckAppUpdate.Text = "Verificar atualização agora"
            Me.ButtonCheckAppUpdate.Width = 220
            Me.ButtonCheckAppUpdate.Height = 30
            Me.ButtonCheckAppUpdate.Margin = New Padding(4, 12, 4, 4)
            AddHandler Me.ButtonCheckAppUpdate.Click, AddressOf Me.OnCheckAppUpdateClick
            '
            'ButtonClearCache
            '
            Me.ButtonClearCache.Text = "Limpar cache de imagens"
            Me.ButtonClearCache.Width = 220
            Me.ButtonClearCache.Height = 30
            Me.ButtonClearCache.Margin = New Padding(4, 4, 4, 4)
            AddHandler Me.ButtonClearCache.Click, AddressOf Me.OnClearCacheClick
            '
            'ButtonClearLog
            '
            Me.ButtonClearLog.Text = "Limpar log"
            Me.ButtonClearLog.Width = 220
            Me.ButtonClearLog.Height = 30
            Me.ButtonClearLog.Margin = New Padding(4, 4, 4, 4)
            AddHandler Me.ButtonClearLog.Click, AddressOf Me.OnClearLogClick
            '
            'ButtonRestoreDefaults
            '
            Me.ButtonRestoreDefaults.Text = "Restaurar configurações"
            Me.ButtonRestoreDefaults.Width = 220
            Me.ButtonRestoreDefaults.Height = 30
            Me.ButtonRestoreDefaults.Margin = New Padding(4, 4, 4, 4)
            AddHandler Me.ButtonRestoreDefaults.Click, AddressOf Me.OnRestoreDefaultsClick
            '
            'CheckShowIdentifyUsbButton
            '
            Me.CheckShowIdentifyUsbButton.Text = "Exibir botão ""Imagem do USB"""
            Me.CheckShowIdentifyUsbButton.AutoSize = True
            Me.CheckShowIdentifyUsbButton.Padding = New Padding(4)
            Me.CheckShowIdentifyUsbButton.Margin = New Padding(3, 12, 3, 3)
            AddHandler Me.CheckShowIdentifyUsbButton.CheckedChanged, AddressOf Me.OnAutoMd5Changed
            '
            'CheckShowValidateBootButton
            '
            Me.CheckShowValidateBootButton.Text = "Exibir botão ""Validar Boot do USB"""
            Me.CheckShowValidateBootButton.AutoSize = True
            Me.CheckShowValidateBootButton.Padding = New Padding(4)
            AddHandler Me.CheckShowValidateBootButton.CheckedChanged, AddressOf Me.OnAutoMd5Changed
            '
            'SettingsView
            '
            Me.AutoScaleMode = AutoScaleMode.Font
            Me.BackColor = Color.White
            Me.Controls.Add(Me.LayoutRoot)
            Me.Name = "SettingsView"
            Me.LayoutRoot.ResumeLayout(False)
            Me.LayoutRoot.PerformLayout()
            Me.MultiConnectionPanel.ResumeLayout(False)
            Me.MultiConnectionPanel.PerformLayout()
            Me.CachePathPanel.ResumeLayout(False)
            Me.CachePathPanel.PerformLayout()
            Me.ContentPanel.ResumeLayout(False)
            Me.ContentPanel.PerformLayout()
            Me.ResumeLayout(False)
        End Sub
    End Class
End Namespace


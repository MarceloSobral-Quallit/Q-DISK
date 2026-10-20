Imports System.Drawing
Imports System.Windows.Forms

Namespace Views
    <Global.Microsoft.VisualBasic.CompilerServices.DesignerGenerated()>
    Partial Public Class UsbBootView
        Inherits UserControl

        Private components As System.ComponentModel.IContainer
        Friend WithEvents LayoutRoot As TableLayoutPanel
        Friend WithEvents HeaderPanel As TableLayoutPanel
        Friend WithEvents DriveLabel As Label
        Friend WithEvents _driveCombo As ComboBox
        Friend WithEvents _refreshButton As Button
        Friend WithEvents _infoLabel As Label
        Friend WithEvents AdminPanel As TableLayoutPanel
        Friend WithEvents _adminWarningLabel As Label
        Friend WithEvents _adminRestartButton As Button
        Friend WithEvents ContentLayout As TableLayoutPanel
        Friend WithEvents ListsLayout As TableLayoutPanel
        Friend WithEvents TibxGroup As GroupBox
        Friend WithEvents IsoGroup As GroupBox
        Friend WithEvents _tibxList As ListBox
        Friend WithEvents _isoList As ListBox
        Friend WithEvents ActionsPanel As TableLayoutPanel
        Friend WithEvents ActionsTopPanel As FlowLayoutPanel
        Friend WithEvents ActionsBottomPanel As FlowLayoutPanel
        Friend WithEvents _copyButton As Button
        Friend WithEvents _bootButton As Button
        Friend WithEvents _identifyUsbButton As Button
        Friend WithEvents _verifyBootButton As Button
        Friend WithEvents _verifyTibxButton As Button
        Friend WithEvents _zeroUsbButton As Button
        Friend WithEvents _statusLabel As Label
        Friend WithEvents _progressBar As ProgressBar
        Friend WithEvents _cancelOperationButton As Button
        Friend WithEvents _busyGif As PictureBox
        Friend WithEvents ProgressPanel As TableLayoutPanel

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
            Me.AdminPanel = New TableLayoutPanel()
            Me._adminWarningLabel = New Label()
            Me._adminRestartButton = New Button()
            Me.HeaderPanel = New TableLayoutPanel()
            Me.DriveLabel = New Label()
            Me._driveCombo = New ComboBox()
            Me._refreshButton = New Button()
            Me._infoLabel = New Label()
            Me.ContentLayout = New TableLayoutPanel()
            Me.ListsLayout = New TableLayoutPanel()
            Me.TibxGroup = New GroupBox()
            Me._tibxList = New ListBox()
            Me.IsoGroup = New GroupBox()
            Me._isoList = New ListBox()
            Me.ActionsPanel = New TableLayoutPanel()
            Me.ActionsTopPanel = New FlowLayoutPanel()
            Me.ActionsBottomPanel = New FlowLayoutPanel()
            Me._copyButton = New Button()
            Me._bootButton = New Button()
            Me._identifyUsbButton = New Button()
            Me._verifyBootButton = New Button()
            Me._verifyTibxButton = New Button()
            Me._zeroUsbButton = New Button()
            Me._statusLabel = New Label()
            Me.ProgressPanel = New TableLayoutPanel()
            Me._busyGif = New PictureBox()
            Me._progressBar = New ProgressBar()
            Me._cancelOperationButton = New Button()
            Me.LayoutRoot.SuspendLayout()
            Me.AdminPanel.SuspendLayout()
            Me.HeaderPanel.SuspendLayout()
            Me.ContentLayout.SuspendLayout()
            Me.ListsLayout.SuspendLayout()
            Me.TibxGroup.SuspendLayout()
            Me.IsoGroup.SuspendLayout()
            Me.ActionsPanel.SuspendLayout()
            Me.ActionsTopPanel.SuspendLayout()
            Me.ActionsBottomPanel.SuspendLayout()
            Me.ProgressPanel.SuspendLayout()
            CType(Me._busyGif, System.ComponentModel.ISupportInitialize).BeginInit()
            Me.SuspendLayout()
            '
            'LayoutRoot
            '
            Me.LayoutRoot.ColumnCount = 1
            Me.LayoutRoot.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100.0!))
            Me.LayoutRoot.Dock = DockStyle.Fill
            Me.LayoutRoot.Padding = New Padding(16)
            Me.LayoutRoot.RowCount = 6
            Me.LayoutRoot.RowStyles.Add(New RowStyle(SizeType.Absolute, 48.0!))
            Me.LayoutRoot.RowStyles.Add(New RowStyle(SizeType.Absolute, 48.0!))
            Me.LayoutRoot.RowStyles.Add(New RowStyle(SizeType.Absolute, 32.0!))
            Me.LayoutRoot.RowStyles.Add(New RowStyle(SizeType.Percent, 100.0!))
            Me.LayoutRoot.RowStyles.Add(New RowStyle(SizeType.Absolute, 32.0!))
            Me.LayoutRoot.RowStyles.Add(New RowStyle(SizeType.Absolute, 32.0!))
            Me.LayoutRoot.Controls.Add(Me.AdminPanel, 0, 0)
            Me.LayoutRoot.Controls.Add(Me.HeaderPanel, 0, 1)
            Me.LayoutRoot.Controls.Add(Me._infoLabel, 0, 2)
            Me.LayoutRoot.Controls.Add(Me.ContentLayout, 0, 3)
            Me.LayoutRoot.Controls.Add(Me._statusLabel, 0, 4)
            Me.LayoutRoot.Controls.Add(Me.ProgressPanel, 0, 5)
            '
            'AdminPanel
            '
            Me.AdminPanel.ColumnCount = 2
            Me.AdminPanel.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100.0!))
            Me.AdminPanel.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, 190.0!))
            Me.AdminPanel.Dock = DockStyle.Fill
            Me.AdminPanel.RowCount = 1
            Me.AdminPanel.RowStyles.Add(New RowStyle(SizeType.Absolute, 24.0!))
            Me.AdminPanel.Controls.Add(Me._adminWarningLabel, 0, 0)
            Me.AdminPanel.Controls.Add(Me._adminRestartButton, 1, 0)
            '
            '_adminWarningLabel
            '
            Me._adminWarningLabel.Dock = DockStyle.Fill
            Me._adminWarningLabel.TextAlign = ContentAlignment.MiddleLeft
            Me._adminWarningLabel.ForeColor = Color.FromArgb(200, 40, 40)
            Me._adminWarningLabel.Text = "Aviso: Ative o Modo Administrador para usar as operações USB."
            'Execute como Administrador para usar as operações USB
            '_adminRestartButton
            '
            Me._adminRestartButton.Text = "Ativar Modo Administrador"
            Me._adminRestartButton.Width = 170
            Me._adminRestartButton.Height = 28
            Me._adminRestartButton.Margin = New Padding(0, 0, 0, 0)
            Me._adminRestartButton.Anchor = AnchorStyles.Left
            AddHandler Me._adminRestartButton.Click, AddressOf Me.OnRestartAsAdminClick
            '
            'HeaderPanel
            '
            Me.HeaderPanel.ColumnCount = 3
            Me.HeaderPanel.ColumnStyles.Add(New ColumnStyle(SizeType.AutoSize))
            Me.HeaderPanel.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100.0!))
            Me.HeaderPanel.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, 190.0!))
            Me.HeaderPanel.RowCount = 1
            Me.HeaderPanel.RowStyles.Add(New RowStyle(SizeType.Percent, 100.0!))
            Me.HeaderPanel.Dock = DockStyle.Fill
            Me.HeaderPanel.Controls.Add(Me.DriveLabel, 0, 0)
            Me.HeaderPanel.Controls.Add(Me._driveCombo, 1, 0)
            Me.HeaderPanel.Controls.Add(Me._refreshButton, 2, 0)
            '
            'DriveLabel
            '
            Me.DriveLabel.Text = "USB:"
            Me.DriveLabel.AutoSize = True
            Me.DriveLabel.TextAlign = ContentAlignment.MiddleLeft
            Me.DriveLabel.Padding = New Padding(0, 4, 8, 0)
            '
            '_driveCombo
            '
            Me._driveCombo.Width = 280
            Me._driveCombo.Height = 28
            Me._driveCombo.Anchor = AnchorStyles.Left Or AnchorStyles.Right
            Me._driveCombo.Margin = New Padding(0, 0, 12, 0)
            Me._driveCombo.DropDownStyle = ComboBoxStyle.DropDownList
            Me._driveCombo.DisplayMember = "DisplayName"
            AddHandler Me._driveCombo.SelectedIndexChanged, AddressOf Me.OnDriveChanged
            '
            '_refreshButton
            '
            Me._refreshButton.Text = "Atualizar USB"
            Me._refreshButton.Width = 100
            Me._refreshButton.Height = 28
            Me._refreshButton.Margin = New Padding(0, 0, 0, 0)
            Me._refreshButton.Anchor = AnchorStyles.Left
            AddHandler Me._refreshButton.Click, AddressOf Me.OnRefreshClick
            '
            '_infoLabel
            '
            Me._infoLabel.Dock = DockStyle.Fill
            Me._infoLabel.TextAlign = ContentAlignment.MiddleLeft
            Me._infoLabel.ForeColor = Color.FromArgb(80, 90, 100)
            Me._infoLabel.Text = "USB: nenhum selecionado."
            Me._infoLabel.Padding = New Padding(0, 4, 0, 4)
            '
            'ContentLayout
            '
            Me.ContentLayout.ColumnCount = 2
            Me.ContentLayout.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 68.0!))
            Me.ContentLayout.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 32.0!))
            Me.ContentLayout.Dock = DockStyle.Fill
            Me.ContentLayout.Controls.Add(Me.ListsLayout, 0, 0)
            Me.ContentLayout.Controls.Add(Me.ActionsPanel, 1, 0)
            '
            'ListsLayout
            '
            Me.ListsLayout.ColumnCount = 1
            Me.ListsLayout.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100.0!))
            Me.ListsLayout.Dock = DockStyle.Fill
            Me.ListsLayout.RowCount = 2
            Me.ListsLayout.RowStyles.Add(New RowStyle(SizeType.Percent, 50.0!))
            Me.ListsLayout.RowStyles.Add(New RowStyle(SizeType.Percent, 50.0!))
            Me.ListsLayout.Controls.Add(Me.TibxGroup, 0, 0)
            Me.ListsLayout.Controls.Add(Me.IsoGroup, 0, 1)
            '
            'TibxGroup
            '
            Me.TibxGroup.Text = "Imagens TIBX"
            Me.TibxGroup.Dock = DockStyle.Fill
            Me.TibxGroup.Controls.Add(Me._tibxList)
            '
            '_tibxList
            '
            Me._tibxList.Dock = DockStyle.Fill
            '
            'IsoGroup
            '
            Me.IsoGroup.Text = "Imagens ISO"
            Me.IsoGroup.Dock = DockStyle.Fill
            Me.IsoGroup.Controls.Add(Me._isoList)
            '
            '_isoList
            '
            Me._isoList.Dock = DockStyle.Fill
            '
            'ActionsPanel
            '
            Me.ActionsPanel.Dock = DockStyle.Fill
            Me.ActionsPanel.ColumnCount = 1
            Me.ActionsPanel.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100.0!))
            Me.ActionsPanel.RowCount = 2
            Me.ActionsPanel.RowStyles.Add(New RowStyle(SizeType.Percent, 50.0!))
            Me.ActionsPanel.RowStyles.Add(New RowStyle(SizeType.Percent, 50.0!))
            Me.ActionsPanel.Controls.Add(Me.ActionsTopPanel, 0, 0)
            Me.ActionsPanel.Controls.Add(Me.ActionsBottomPanel, 0, 1)
            '
            'ActionsTopPanel
            '
            Me.ActionsTopPanel.Dock = DockStyle.Fill
            Me.ActionsTopPanel.FlowDirection = FlowDirection.TopDown
            Me.ActionsTopPanel.WrapContents = False
            Me.ActionsTopPanel.Padding = New Padding(4, 12, 12, 8)
            Me.ActionsTopPanel.Controls.Add(Me._copyButton)
            Me.ActionsTopPanel.Controls.Add(Me._verifyTibxButton)
            Me.ActionsTopPanel.Controls.Add(Me._zeroUsbButton)
            '
            'ActionsBottomPanel
            '
            Me.ActionsBottomPanel.Dock = DockStyle.Fill
            Me.ActionsBottomPanel.FlowDirection = FlowDirection.TopDown
            Me.ActionsBottomPanel.WrapContents = False
            Me.ActionsBottomPanel.Padding = New Padding(4, 8, 12, 12)
            Me.ActionsBottomPanel.Controls.Add(Me._bootButton)
            Me.ActionsBottomPanel.Controls.Add(Me._identifyUsbButton)
            Me.ActionsBottomPanel.Controls.Add(Me._verifyBootButton)
            '
            '_copyButton
            '
            Me._copyButton.Text = "Copiar TIBX"
            Me._copyButton.Width = 96
            Me._copyButton.Height = 28
            Me._copyButton.Margin = New Padding(0)
            Me._copyButton.Font = New Font(Me._copyButton.Font.FontFamily, 9.0F, FontStyle.Regular)
            AddHandler Me._copyButton.Click, AddressOf Me.OnCopyClick
            '
            '_bootButton
            '
            Me._bootButton.Text = "Criar USB Bootável"
            Me._bootButton.Width = 132
            Me._bootButton.Height = 28
            Me._bootButton.Margin = New Padding(0)
            Me._bootButton.Font = New Font(Me._bootButton.Font.FontFamily, 9.0F, FontStyle.Regular)
            AddHandler Me._bootButton.Click, AddressOf Me.OnBootClick
            '
            '_identifyUsbButton
            '
            Me._identifyUsbButton.Text = "Imagem do USB"
            Me._identifyUsbButton.Width = 132
            Me._identifyUsbButton.Height = 28
            Me._identifyUsbButton.Margin = New Padding(0, 6, 0, 0)
            Me._identifyUsbButton.Font = New Font(Me._identifyUsbButton.Font.FontFamily, 9.0F, FontStyle.Regular)
            AddHandler Me._identifyUsbButton.Click, AddressOf Me.OnIdentifyUsbClick
            '
            '_verifyBootButton
            '
            Me._verifyBootButton.Text = "Verificar Boot"
            Me._verifyBootButton.Width = 132
            Me._verifyBootButton.Height = 28
            Me._verifyBootButton.Margin = New Padding(0, 6, 0, 0)
            Me._verifyBootButton.Font = New Font(Me._verifyBootButton.Font.FontFamily, 9.0F, FontStyle.Regular)
            AddHandler Me._verifyBootButton.Click, AddressOf Me.OnVerifyBootClick
            '
            '_verifyTibxButton
            '
            Me._verifyTibxButton.Text = "Verificar TIBX no USB"
            Me._verifyTibxButton.Width = 150
            Me._verifyTibxButton.Height = 28
            Me._verifyTibxButton.Margin = New Padding(0, 6, 0, 0)
            Me._verifyTibxButton.Font = New Font(Me._verifyTibxButton.Font.FontFamily, 9.0F, FontStyle.Regular)
            AddHandler Me._verifyTibxButton.Click, AddressOf Me.OnVerifyTibxOnUsbClick
            '
            '_zeroUsbButton
            '
            Me._zeroUsbButton.Text = "Zerar USB"
            Me._zeroUsbButton.Width = 96
            Me._zeroUsbButton.Height = 28
            Me._zeroUsbButton.Margin = New Padding(0, 10, 0, 0)
            Me._zeroUsbButton.Font = New Font(Me._zeroUsbButton.Font.FontFamily, 9.0F, FontStyle.Regular)
            AddHandler Me._zeroUsbButton.Click, AddressOf Me.OnZeroUsbClick
            '
            '_statusLabel
            '
            Me._statusLabel.Dock = DockStyle.Fill
            Me._statusLabel.TextAlign = ContentAlignment.MiddleLeft
            Me._statusLabel.ForeColor = Color.FromArgb(80, 90, 100)
            Me._statusLabel.Text = "Pronto."
            '
            'ProgressPanel
            '
            Me.ProgressPanel.ColumnCount = 3
            Me.ProgressPanel.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, 32.0!))
            Me.ProgressPanel.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100.0!))
            Me.ProgressPanel.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, 104.0!))
            Me.ProgressPanel.Dock = DockStyle.Fill
            Me.ProgressPanel.Controls.Add(Me._busyGif, 0, 0)
            Me.ProgressPanel.Controls.Add(Me._progressBar, 1, 0)
            Me.ProgressPanel.Controls.Add(Me._cancelOperationButton, 2, 0)
            '
            '_busyGif
            '
            Me._busyGif.Width = 28
            Me._busyGif.Height = 20
            Me._busyGif.SizeMode = PictureBoxSizeMode.Zoom
            Me._busyGif.Visible = False
            Me._busyGif.Margin = New Padding(0)
            Me._busyGif.Anchor = AnchorStyles.Left Or AnchorStyles.Top
            '
            '_progressBar
            '
            Me._progressBar.Dock = DockStyle.Fill
            Me._progressBar.Minimum = 0
            Me._progressBar.Maximum = 100
            Me._progressBar.Value = 0
            '
            '_cancelOperationButton
            '
            Me._cancelOperationButton.Text = "Cancelar"
            Me._cancelOperationButton.Width = 92
            Me._cancelOperationButton.Height = 24
            Me._cancelOperationButton.Margin = New Padding(8, 0, 0, 0)
            Me._cancelOperationButton.Anchor = AnchorStyles.Left Or AnchorStyles.Top
            Me._cancelOperationButton.Enabled = False
            AddHandler Me._cancelOperationButton.Click, AddressOf Me.OnCancelOperationClick
            '
            'UsbBootView
            '
            Me.AutoScaleMode = AutoScaleMode.Font
            Me.BackColor = Color.White
            Me.Controls.Add(Me.LayoutRoot)
            Me.Name = "UsbBootView"
            Me.LayoutRoot.ResumeLayout(False)
            Me.AdminPanel.ResumeLayout(False)
            Me.HeaderPanel.ResumeLayout(False)
            Me.HeaderPanel.PerformLayout()
            Me.ContentLayout.ResumeLayout(False)
            Me.ListsLayout.ResumeLayout(False)
            Me.TibxGroup.ResumeLayout(False)
            Me.IsoGroup.ResumeLayout(False)
            Me.ActionsPanel.ResumeLayout(False)
            Me.ActionsTopPanel.ResumeLayout(False)
            Me.ActionsBottomPanel.ResumeLayout(False)
            Me.ActionsBottomPanel.PerformLayout()
            Me.ProgressPanel.ResumeLayout(False)
            CType(Me._busyGif, System.ComponentModel.ISupportInitialize).EndInit()
            Me.ResumeLayout(False)
        End Sub
    End Class
End Namespace


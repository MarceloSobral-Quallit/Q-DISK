Imports System.Drawing
Imports System.Windows.Forms

Namespace Views
    <Global.Microsoft.VisualBasic.CompilerServices.DesignerGenerated()>
    Partial Public Class DownloadsView
        Inherits UserControl

        Private components As System.ComponentModel.IContainer
        Friend WithEvents _grid As DataGridView
        Friend WithEvents _btnRefresh As Button
        Friend WithEvents _btnDownload As Button
        Friend WithEvents _btnCancelDownload As Button
        Friend WithEvents _btnValidate As Button
        Friend WithEvents _btnOpenFolder As Button
        Friend WithEvents _progressBar As ProgressBar
        Friend WithEvents _statusLabel As Label
        Friend WithEvents _selectionLabel As Label
        Friend WithEvents _busyGif As PictureBox
        Friend WithEvents LayoutRoot As TableLayoutPanel
        Friend WithEvents ActionsPanel As FlowLayoutPanel
        Friend WithEvents LowerPanel As TableLayoutPanel
        Friend WithEvents StatusPanel As TableLayoutPanel
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
            Me.ActionsPanel = New FlowLayoutPanel()
            Me._btnRefresh = New Button()
            Me._btnDownload = New Button()
            Me._btnCancelDownload = New Button()
            Me._btnValidate = New Button()
            Me._btnOpenFolder = New Button()
            Me._selectionLabel = New Label()
            Me.LowerPanel = New TableLayoutPanel()
            Me._grid = New DataGridView()
            Me.StatusPanel = New TableLayoutPanel()
            Me._statusLabel = New Label()
            Me.ProgressPanel = New TableLayoutPanel()
            Me._busyGif = New PictureBox()
            Me._progressBar = New ProgressBar()
            Me.LayoutRoot.SuspendLayout()
            Me.ActionsPanel.SuspendLayout()
            Me.LowerPanel.SuspendLayout()
            CType(Me._grid, System.ComponentModel.ISupportInitialize).BeginInit()
            Me.StatusPanel.SuspendLayout()
            Me.ProgressPanel.SuspendLayout()
            CType(Me._busyGif, System.ComponentModel.ISupportInitialize).BeginInit()
            Me.SuspendLayout()
            '
            'LayoutRoot
            '
            Me.LayoutRoot.ColumnCount = 1
            Me.LayoutRoot.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100.0!))
            Me.LayoutRoot.Dock = DockStyle.Fill
            Me.LayoutRoot.RowCount = 2
            Me.LayoutRoot.RowStyles.Add(New RowStyle(SizeType.Absolute, 52.0!))
            Me.LayoutRoot.RowStyles.Add(New RowStyle(SizeType.Percent, 100.0!))
            Me.LayoutRoot.Controls.Add(Me.ActionsPanel, 0, 0)
            Me.LayoutRoot.Controls.Add(Me.LowerPanel, 0, 1)
            '
            'ActionsPanel
            '
            Me.ActionsPanel.Dock = DockStyle.Fill
            Me.ActionsPanel.FlowDirection = FlowDirection.LeftToRight
            Me.ActionsPanel.Padding = New Padding(12, 10, 12, 10)
            Me.ActionsPanel.WrapContents = False
            Me.ActionsPanel.Controls.Add(Me._btnRefresh)
            Me.ActionsPanel.Controls.Add(Me._btnDownload)
            Me.ActionsPanel.Controls.Add(Me._btnCancelDownload)
            Me.ActionsPanel.Controls.Add(Me._btnValidate)
            Me.ActionsPanel.Controls.Add(Me._btnOpenFolder)
            '
            '_btnRefresh
            '
            Me._btnRefresh.Text = "Atualizar"
            Me._btnRefresh.Width = 90
            Me._btnRefresh.Height = 30
            AddHandler Me._btnRefresh.Click, AddressOf Me.OnRefreshClick
            '
            '_btnDownload
            '
            Me._btnDownload.Text = "Baixar"
            Me._btnDownload.Width = 85
            Me._btnDownload.Height = 30
            AddHandler Me._btnDownload.Click, AddressOf Me.OnDownloadClick
            '
            '_btnCancelDownload
            '
            Me._btnCancelDownload.Text = "Cancelar"
            Me._btnCancelDownload.Width = 85
            Me._btnCancelDownload.Height = 30
            Me._btnCancelDownload.Enabled = False
            AddHandler Me._btnCancelDownload.Click, AddressOf Me.OnCancelDownloadClick
            '
            '_btnValidate
            '
            Me._btnValidate.Text = "Validar integridade"
            Me._btnValidate.Width = 130
            Me._btnValidate.Height = 30
            AddHandler Me._btnValidate.Click, AddressOf Me.OnValidateClick
            '
            '_btnOpenFolder
            '
            Me._btnOpenFolder.Text = "Abrir pasta"
            Me._btnOpenFolder.Width = 95
            Me._btnOpenFolder.Height = 30
            AddHandler Me._btnOpenFolder.Click, AddressOf Me.OnOpenFolderClick
            '
            '_selectionLabel
            '
            Me._selectionLabel.AutoSize = False
            Me._selectionLabel.Dock = DockStyle.Fill
            Me._selectionLabel.TextAlign = ContentAlignment.MiddleLeft
            Me._selectionLabel.ForeColor = Color.FromArgb(80, 90, 100)
            Me._selectionLabel.Margin = New Padding(0)
            Me._selectionLabel.Text = "Selecionado: nenhum"
            '
            'LowerPanel
            '
            Me.LowerPanel.ColumnCount = 1
            Me.LowerPanel.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100.0!))
            Me.LowerPanel.Dock = DockStyle.Fill
            Me.LowerPanel.RowCount = 2
            Me.LowerPanel.RowStyles.Add(New RowStyle(SizeType.Percent, 100.0!))
            Me.LowerPanel.RowStyles.Add(New RowStyle(SizeType.Absolute, 76.0!))
            Me.LowerPanel.Controls.Add(Me._grid, 0, 0)
            Me.LowerPanel.Controls.Add(Me.StatusPanel, 0, 1)
            '
            '_grid
            '
            Me._grid.Dock = DockStyle.Fill
            Me._grid.ReadOnly = False
            Me._grid.AllowUserToAddRows = False
            Me._grid.AllowUserToDeleteRows = False
            Me._grid.AutoGenerateColumns = False
            Me._grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect
            Me._grid.MultiSelect = False
            AddHandler Me._grid.SelectionChanged, AddressOf Me.OnSelectionChanged
            AddHandler Me._grid.CellContentClick, AddressOf Me.OnGridCellContentClick
            AddHandler Me._grid.CurrentCellDirtyStateChanged, AddressOf Me.OnGridCurrentCellDirtyStateChanged
            '
            'StatusPanel
            '
            Me.StatusPanel.ColumnCount = 1
            Me.StatusPanel.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100.0!))
            Me.StatusPanel.Dock = DockStyle.Fill
            Me.StatusPanel.RowCount = 3
            Me.StatusPanel.RowStyles.Add(New RowStyle(SizeType.Absolute, 20.0!))
            Me.StatusPanel.RowStyles.Add(New RowStyle(SizeType.Absolute, 20.0!))
            Me.StatusPanel.RowStyles.Add(New RowStyle(SizeType.Absolute, 32.0!))
            Me.StatusPanel.Controls.Add(Me._selectionLabel, 0, 0)
            Me.StatusPanel.Controls.Add(Me._statusLabel, 0, 1)
            Me.StatusPanel.Controls.Add(Me.ProgressPanel, 0, 2)
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
            Me.ProgressPanel.ColumnCount = 2
            Me.ProgressPanel.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, 32.0!))
            Me.ProgressPanel.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100.0!))
            Me.ProgressPanel.Dock = DockStyle.Fill
            Me.ProgressPanel.Controls.Add(Me._busyGif, 0, 0)
            Me.ProgressPanel.Controls.Add(Me._progressBar, 1, 0)
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
            Me._progressBar.Height = 20
            Me._progressBar.Minimum = 0
            Me._progressBar.Maximum = 100
            '
            'DownloadsView
            '
            Me.AutoScaleMode = AutoScaleMode.Font
            Me.BackColor = Color.White
            Me.Controls.Add(Me.LayoutRoot)
            Me.Name = "DownloadsView"
            Me.LayoutRoot.ResumeLayout(False)
            Me.ActionsPanel.ResumeLayout(False)
            Me.ActionsPanel.PerformLayout()
            Me.LowerPanel.ResumeLayout(False)
            CType(Me._grid, System.ComponentModel.ISupportInitialize).EndInit()
            Me.StatusPanel.ResumeLayout(False)
            Me.ProgressPanel.ResumeLayout(False)
            CType(Me._busyGif, System.ComponentModel.ISupportInitialize).EndInit()
            Me.ResumeLayout(False)

        End Sub
    End Class
End Namespace


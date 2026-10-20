Imports System.Drawing
Imports System.Windows.Forms

Namespace Views
    <Global.Microsoft.VisualBasic.CompilerServices.DesignerGenerated()>
    Partial Public Class LogsView
        Inherits UserControl

        Private components As System.ComponentModel.IContainer
        Friend WithEvents TextBoxLogs As TextBox
        Friend WithEvents ButtonRefresh As Button
        Friend WithEvents LayoutRoot As TableLayoutPanel
        Friend WithEvents HeaderPanel As FlowLayoutPanel

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
            Me.HeaderPanel = New FlowLayoutPanel()
            Me.ButtonRefresh = New Button()
            Me.TextBoxLogs = New TextBox()
            Me.LayoutRoot.SuspendLayout()
            Me.HeaderPanel.SuspendLayout()
            Me.SuspendLayout()
            '
            'LayoutRoot
            '
            Me.LayoutRoot.ColumnCount = 1
            Me.LayoutRoot.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100.0!))
            Me.LayoutRoot.Dock = DockStyle.Fill
            Me.LayoutRoot.Padding = New Padding(12)
            Me.LayoutRoot.RowCount = 2
            Me.LayoutRoot.RowStyles.Add(New RowStyle(SizeType.Absolute, 36.0!))
            Me.LayoutRoot.RowStyles.Add(New RowStyle(SizeType.Percent, 100.0!))
            Me.LayoutRoot.Controls.Add(Me.HeaderPanel, 0, 0)
            Me.LayoutRoot.Controls.Add(Me.TextBoxLogs, 0, 1)
            '
            'HeaderPanel
            '
            Me.HeaderPanel.Dock = DockStyle.Fill
            Me.HeaderPanel.FlowDirection = FlowDirection.LeftToRight
            Me.HeaderPanel.Controls.Add(Me.ButtonRefresh)
            '
            'ButtonRefresh
            '
            Me.ButtonRefresh.Text = "Atualizar"
            Me.ButtonRefresh.Width = 100
            Me.ButtonRefresh.Height = 28
            AddHandler Me.ButtonRefresh.Click, AddressOf Me.OnRefreshClick
            '
            'TextBoxLogs
            '
            Me.TextBoxLogs.Dock = DockStyle.Fill
            Me.TextBoxLogs.Multiline = True
            Me.TextBoxLogs.ScrollBars = ScrollBars.Both
            Me.TextBoxLogs.ReadOnly = True
            Me.TextBoxLogs.Font = New Font("Consolas", 9.0!, FontStyle.Regular)
            '
            'LogsView
            '
            Me.AutoScaleMode = AutoScaleMode.Font
            Me.BackColor = Color.White
            Me.Controls.Add(Me.LayoutRoot)
            Me.Name = "LogsView"
            Me.LayoutRoot.ResumeLayout(False)
            Me.LayoutRoot.PerformLayout()
            Me.HeaderPanel.ResumeLayout(False)
            Me.ResumeLayout(False)
        End Sub
    End Class
End Namespace


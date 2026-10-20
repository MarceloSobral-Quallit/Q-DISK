Imports System.Drawing
Imports System.Windows.Forms

Namespace Views
    <Global.Microsoft.VisualBasic.CompilerServices.DesignerGenerated()>
    Partial Public Class DashboardView
        Inherits UserControl

        Private components As System.ComponentModel.IContainer
        Friend WithEvents LayoutRoot As TableLayoutPanel
        Friend WithEvents HeaderPanel As Panel
        Friend WithEvents TitleLabel As Label
        Friend WithEvents ContentPanel As Panel

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
            Me.components = New System.ComponentModel.Container()
            Me.LayoutRoot = New TableLayoutPanel()
            Me.HeaderPanel = New Panel()
            Me.TitleLabel = New Label()
            Me.ContentPanel = New Panel()
            Me.LayoutRoot.SuspendLayout()
            Me.HeaderPanel.SuspendLayout()
            Me.SuspendLayout()
            '
            'LayoutRoot
            '
            Me.LayoutRoot.ColumnCount = 1
            Me.LayoutRoot.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100.0!))
            Me.LayoutRoot.Controls.Add(Me.HeaderPanel, 0, 0)
            Me.LayoutRoot.Controls.Add(Me.ContentPanel, 0, 1)
            Me.LayoutRoot.Dock = DockStyle.Fill
            Me.LayoutRoot.Location = New Point(0, 0)
            Me.LayoutRoot.Name = "LayoutRoot"
            Me.LayoutRoot.RowCount = 2
            Me.LayoutRoot.RowStyles.Add(New RowStyle(SizeType.Absolute, 0.0!))
            Me.LayoutRoot.RowStyles.Add(New RowStyle(SizeType.Percent, 100.0!))
            Me.LayoutRoot.Size = New Size(600, 400)
            Me.LayoutRoot.TabIndex = 0
            '
            'HeaderPanel
            '
            Me.HeaderPanel.Controls.Add(Me.TitleLabel)
            Me.HeaderPanel.Dock = DockStyle.Fill
            Me.HeaderPanel.Location = New Point(3, 3)
            Me.HeaderPanel.Name = "HeaderPanel"
            Me.HeaderPanel.Size = New Size(594, 1)
            Me.HeaderPanel.TabIndex = 0
            Me.HeaderPanel.Visible = False
            '
            'TitleLabel
            '
            Me.TitleLabel.AutoSize = False
            Me.TitleLabel.Dock = DockStyle.Fill
            Me.TitleLabel.Font = New Font("Segoe UI", 18.0!, FontStyle.Bold)
            Me.TitleLabel.ForeColor = Color.FromArgb(32, 45, 64)
            Me.TitleLabel.Location = New Point(0, 0)
            Me.TitleLabel.Name = "TitleLabel"
            Me.TitleLabel.Size = New Size(594, 74)
            Me.TitleLabel.TabIndex = 0
            Me.TitleLabel.Text = ""
            Me.TitleLabel.TextAlign = ContentAlignment.MiddleLeft
            '
            'ContentPanel
            '
            Me.ContentPanel.Dock = DockStyle.Fill
            Me.ContentPanel.Location = New Point(3, 3)
            Me.ContentPanel.Name = "ContentPanel"
            Me.ContentPanel.Size = New Size(594, 394)
            Me.ContentPanel.TabIndex = 1
            '
            'DashboardView
            '
            Me.AutoScaleMode = AutoScaleMode.Font
            Me.BackColor = Color.White
            Me.Controls.Add(Me.LayoutRoot)
            Me.Name = "DashboardView"
            Me.LayoutRoot.ResumeLayout(False)
            Me.LayoutRoot.PerformLayout()
            Me.HeaderPanel.ResumeLayout(False)
            Me.HeaderPanel.PerformLayout()
            Me.ResumeLayout(False)
        End Sub
    End Class
End Namespace

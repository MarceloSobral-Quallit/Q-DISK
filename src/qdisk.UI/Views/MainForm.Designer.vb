Imports System.Drawing
Imports System.Windows.Forms

Namespace Views
    <Global.Microsoft.VisualBasic.CompilerServices.DesignerGenerated()>
    Partial Public Class MainForm
        Inherits Form

        Private components As System.ComponentModel.IContainer
        Friend WithEvents _headerPanel As Panel
        Friend WithEvents _navPanel As Panel
        Friend WithEvents _contentPanel As Panel
        Friend WithEvents _titleLabel As Label
        Friend WithEvents _btnChangeProfile As Button
        Friend WithEvents _contentLabel As Label
        Friend WithEvents _statusStrip As StatusStrip
        Friend WithEvents _statusReady As ToolStripStatusLabel
        Friend WithEvents _statusText As ToolStripStatusLabel

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
            Me._headerPanel = New Panel()
            Me._titleLabel = New Label()
            Me._btnChangeProfile = New Button()
            Me._navPanel = New Panel()
            Me._contentPanel = New Panel()
            Me._contentLabel = New Label()
            Me._statusStrip = New StatusStrip()
            Me._statusReady = New ToolStripStatusLabel()
            Me._statusText = New ToolStripStatusLabel()
            Me._headerPanel.SuspendLayout()
            Me._contentPanel.SuspendLayout()
            Me._statusStrip.SuspendLayout()
            Me.SuspendLayout()
            '
            '_headerPanel
            '
            Me._headerPanel.Dock = DockStyle.Top
            Me._headerPanel.Height = 56
            Me._headerPanel.BackColor = Color.FromArgb(32, 45, 64)
            Me._headerPanel.Controls.Add(Me._titleLabel)
            Me._headerPanel.Controls.Add(Me._btnChangeProfile)
            '
            '_titleLabel
            '
            Me._titleLabel.AutoSize = False
            Me._titleLabel.Dock = DockStyle.Fill
            Me._titleLabel.TextAlign = ContentAlignment.MiddleLeft
            Me._titleLabel.ForeColor = Color.White
            Me._titleLabel.Font = New Font("Segoe UI", 14.0!, FontStyle.Bold)
            Me._titleLabel.Padding = New Padding(16, 0, 0, 0)
            Me._titleLabel.Text = "Inicio"
            '
            '_btnChangeProfile
            '
            Me._btnChangeProfile.Dock = DockStyle.Right
            Me._btnChangeProfile.Width = 150
            Me._btnChangeProfile.Text = "Alterar Perfil"
            Me._btnChangeProfile.FlatStyle = FlatStyle.Flat
            Me._btnChangeProfile.BackColor = Color.FromArgb(32, 45, 64)
            Me._btnChangeProfile.ForeColor = Color.White
            Me._btnChangeProfile.Font = New Font("Segoe UI", 9.0!, FontStyle.Regular)
            Me._btnChangeProfile.Cursor = Cursors.Hand
            Me._btnChangeProfile.FlatAppearance.BorderColor = Color.FromArgb(70, 85, 105)
            AddHandler Me._btnChangeProfile.Click, AddressOf Me.OnChangeProfileClick
            '
            '_navPanel
            '
            Me._navPanel.Dock = DockStyle.Left
            Me._navPanel.Width = 200
            Me._navPanel.BackColor = Color.FromArgb(245, 247, 250)
            '
            '_contentPanel
            '
            Me._contentPanel.Dock = DockStyle.Fill
            Me._contentPanel.BackColor = Color.White
            Me._contentPanel.Controls.Add(Me._contentLabel)
            '
            '_contentLabel
            '
            Me._contentLabel.AutoSize = False
            Me._contentLabel.Dock = DockStyle.Fill
            Me._contentLabel.TextAlign = ContentAlignment.MiddleCenter
            Me._contentLabel.Font = New Font("Segoe UI", 18.0!, FontStyle.Regular)
            Me._contentLabel.ForeColor = Color.FromArgb(60, 70, 80)
            Me._contentLabel.Text = "Inicio"
            '
            '_statusStrip
            '
            Me._statusStrip.Items.Add(Me._statusText)
            Me._statusStrip.Items.Add(Me._statusReady)
            '
            '_statusReady
            '
            Me._statusReady.Alignment = ToolStripItemAlignment.Right
            Me._statusReady.Text = ""
            '
            '_statusText
            '
            Me._statusText.Spring = True
            '
            'MainForm
            '
            Me.AutoScaleMode = AutoScaleMode.Font
            Me.StartPosition = FormStartPosition.CenterScreen
            Me.Size = New Size(800, 650)
            Me.MinimumSize = New Size(800, 650)
            Me.BackColor = Color.White
            Me.Controls.Add(Me._contentPanel)
            Me.Controls.Add(Me._navPanel)
            Me.Controls.Add(Me._headerPanel)
            Me.Controls.Add(Me._statusStrip)
            Me.Name = "MainForm"
            Me._headerPanel.ResumeLayout(False)
            Me._contentPanel.ResumeLayout(False)
            Me._statusStrip.ResumeLayout(False)
            Me._statusStrip.PerformLayout()
            Me.ResumeLayout(False)
            Me.PerformLayout()
        End Sub
    End Class
End Namespace


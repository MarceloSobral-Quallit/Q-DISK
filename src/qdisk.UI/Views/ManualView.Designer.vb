Imports System.Drawing
Imports System.Windows.Forms

Namespace Views
    <Global.Microsoft.VisualBasic.CompilerServices.DesignerGenerated()>
    Partial Public Class ManualView
        Inherits UserControl

        Private components As System.ComponentModel.IContainer
        Friend WithEvents LayoutRoot As TableLayoutPanel
        Friend WithEvents ManualBrowser As WebBrowser

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
            LayoutRoot = New TableLayoutPanel()
            ManualBrowser = New WebBrowser()
            LayoutRoot.SuspendLayout()
            SuspendLayout()
            ' 
            ' LayoutRoot
            ' 
            LayoutRoot.ColumnCount = 1
            LayoutRoot.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100F))
            LayoutRoot.Controls.Add(ManualBrowser, 0, 0)
            LayoutRoot.Dock = DockStyle.Fill
            LayoutRoot.Location = New Point(0, 0)
            LayoutRoot.Name = "LayoutRoot"
            LayoutRoot.Padding = New Padding(16)
            LayoutRoot.RowCount = 1
            LayoutRoot.RowStyles.Add(New RowStyle(SizeType.Percent, 100F))
            LayoutRoot.Size = New Size(862, 584)
            LayoutRoot.TabIndex = 0
            ' 
            ' ManualBrowser
            ' 
            ManualBrowser.Dock = DockStyle.Fill
            ManualBrowser.Location = New Point(16, 16)
            ManualBrowser.Margin = New Padding(0)
            ManualBrowser.MinimumSize = New Size(20, 20)
            ManualBrowser.Name = "ManualBrowser"
            ManualBrowser.Size = New Size(830, 552)
            ManualBrowser.TabIndex = 0
            ' 
            ' ManualView
            ' 
            AutoScaleDimensions = New SizeF(10F, 15F)
            AutoScaleMode = AutoScaleMode.Font
            BackColor = Color.White
            Controls.Add(LayoutRoot)
            Name = "ManualView"
            Size = New Size(0, 0)
            LayoutRoot.ResumeLayout(False)
            ResumeLayout(False)
        End Sub
    End Class
End Namespace

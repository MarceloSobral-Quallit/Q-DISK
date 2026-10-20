Imports System.Drawing
Imports System.Windows.Forms

Namespace Views
    <Global.Microsoft.VisualBasic.CompilerServices.DesignerGenerated()>
    Partial Public Class AboutView
        Inherits UserControl

        Private components As System.ComponentModel.IContainer
        Friend WithEvents LayoutRoot As TableLayoutPanel
        Friend WithEvents PanelLogoHost As Panel
        Friend WithEvents PictureLogo As PictureBox
        Friend WithEvents PanelTextHost As Panel
        Friend WithEvents TableText As TableLayoutPanel
        Friend WithEvents LabelTitle As Label
        Friend WithEvents LabelSpacer1 As Label
        Friend WithEvents LabelDescription1 As Label
        Friend WithEvents LabelDescription2 As Label
        Friend WithEvents LabelDescription3 As Label
        Friend WithEvents LabelDescription4 As Label
        Friend WithEvents LabelSpacer2 As Label
        Friend WithEvents LabelVersion As Label
        Friend WithEvents PanelSpacer As Panel

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
            PanelLogoHost = New Panel()
            PictureLogo = New PictureBox()
            PanelSpacer = New Panel()
            PanelTextHost = New Panel()
            TableText = New TableLayoutPanel()
            LabelTitle = New Label()
            LabelSpacer1 = New Label()
            LabelDescription1 = New Label()
            LabelDescription2 = New Label()
            LabelDescription3 = New Label()
            LabelDescription4 = New Label()
            LabelSpacer2 = New Label()
            LabelVersion = New Label()
            LayoutRoot.SuspendLayout()
            PanelLogoHost.SuspendLayout()
            CType(PictureLogo, System.ComponentModel.ISupportInitialize).BeginInit()
            PanelTextHost.SuspendLayout()
            TableText.SuspendLayout()
            SuspendLayout()
            ' 
            ' LayoutRoot
            ' 
            LayoutRoot.ColumnCount = 1
            LayoutRoot.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100F))
            LayoutRoot.Controls.Add(PanelLogoHost, 0, 0)
            LayoutRoot.Controls.Add(PanelSpacer, 0, 1)
            LayoutRoot.Controls.Add(PanelTextHost, 0, 2)
            LayoutRoot.Dock = DockStyle.Fill
            LayoutRoot.Location = New Point(0, 0)
            LayoutRoot.Name = "LayoutRoot"
            LayoutRoot.Padding = New Padding(24)
            LayoutRoot.RowCount = 3
            LayoutRoot.RowStyles.Add(New RowStyle(SizeType.Percent, 43F))
            LayoutRoot.RowStyles.Add(New RowStyle(SizeType.Absolute, 6F))
            LayoutRoot.RowStyles.Add(New RowStyle(SizeType.Percent, 57F))
            LayoutRoot.Size = New Size(862, 584)
            LayoutRoot.TabIndex = 0
            ' 
            ' PanelLogoHost
            ' 
            PanelLogoHost.Controls.Add(PictureLogo)
            PanelLogoHost.Dock = DockStyle.Fill
            PanelLogoHost.Location = New Point(27, 27)
            PanelLogoHost.Name = "PanelLogoHost"
            PanelLogoHost.Size = New Size(808, 221)
            PanelLogoHost.TabIndex = 0
            ' 
            ' PictureLogo
            ' 
            PictureLogo.Location = New Point(0, 0)
            PictureLogo.Name = "PictureLogo"
            PictureLogo.Size = New Size(100, 50)
            PictureLogo.SizeMode = PictureBoxSizeMode.Zoom
            PictureLogo.TabIndex = 0
            PictureLogo.TabStop = False
            ' 
            ' PanelSpacer
            ' 
            PanelSpacer.Dock = DockStyle.Fill
            PanelSpacer.Location = New Point(27, 254)
            PanelSpacer.Name = "PanelSpacer"
            PanelSpacer.Size = New Size(808, 1)
            PanelSpacer.TabIndex = 1
            ' 
            ' PanelTextHost
            ' 
            PanelTextHost.Controls.Add(TableText)
            PanelTextHost.Dock = DockStyle.Fill
            PanelTextHost.Location = New Point(27, 260)
            PanelTextHost.Name = "PanelTextHost"
            PanelTextHost.Size = New Size(808, 297)
            PanelTextHost.TabIndex = 2
            ' 
            ' TableText
            ' 
            TableText.AutoSize = True
            TableText.ColumnCount = 1
            TableText.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100F))
            TableText.Controls.Add(LabelTitle, 0, 0)
            TableText.Controls.Add(LabelSpacer1, 0, 1)
            TableText.Controls.Add(LabelDescription1, 0, 2)
            TableText.Controls.Add(LabelDescription2, 0, 3)
            TableText.Controls.Add(LabelDescription3, 0, 4)
            TableText.Controls.Add(LabelDescription4, 0, 5)
            TableText.Controls.Add(LabelSpacer2, 0, 6)
            TableText.Controls.Add(LabelVersion, 0, 7)
            TableText.Location = New Point(0, 0)
            TableText.Name = "TableText"
            TableText.RowCount = 9
            TableText.RowStyles.Add(New RowStyle(SizeType.AutoSize))
            TableText.RowStyles.Add(New RowStyle(SizeType.Absolute, 5F))  ' Spacer1 - 5px
            TableText.RowStyles.Add(New RowStyle(SizeType.AutoSize))
            TableText.RowStyles.Add(New RowStyle(SizeType.AutoSize))
            TableText.RowStyles.Add(New RowStyle(SizeType.AutoSize))
            TableText.RowStyles.Add(New RowStyle(SizeType.AutoSize))
            TableText.RowStyles.Add(New RowStyle(SizeType.Absolute, 5F))  ' Spacer2 - 5px
            TableText.RowStyles.Add(New RowStyle(SizeType.AutoSize))
            TableText.RowStyles.Add(New RowStyle(SizeType.AutoSize))
            TableText.Size = New Size(0, 0)
            TableText.TabIndex = 0
            ' 
            ' LabelTitle
            ' 
            LabelTitle.AutoSize = True
            LabelTitle.Dock = DockStyle.Fill
            LabelTitle.Font = New Font("Segoe UI", 11F, FontStyle.Bold)
            LabelTitle.ForeColor = Color.Black
            LabelTitle.Name = "LabelTitle"
            LabelTitle.TabIndex = 0
            LabelTitle.Text = "Q-DISK"
            LabelTitle.TextAlign = ContentAlignment.TopCenter
            ' 
            ' LabelSpacer1
            ' 
            LabelSpacer1.AutoSize = True
            LabelSpacer1.Dock = DockStyle.Fill
            LabelSpacer1.Font = New Font("Segoe UI", 5F)
            LabelSpacer1.ForeColor = Color.Transparent
            LabelSpacer1.Name = "LabelSpacer1"
            LabelSpacer1.TabIndex = 1
            LabelSpacer1.Text = ""
            LabelSpacer1.TextAlign = ContentAlignment.TopCenter
            ' 
            ' LabelDescription1
            ' 
            LabelDescription1.AutoSize = True
            LabelDescription1.Dock = DockStyle.Fill
            LabelDescription1.Font = New Font("Segoe UI", 10F)
            LabelDescription1.ForeColor = Color.Gray
            LabelDescription1.Name = "LabelDescription1"
            LabelDescription1.TabIndex = 2
            LabelDescription1.Text = "Ferramenta desktop para sincronização, validação e provisionamento de mídias de recuperação."
            LabelDescription1.TextAlign = ContentAlignment.TopCenter
            ' 
            ' LabelDescription2
            ' 
            LabelDescription2.AutoSize = True
            LabelDescription2.Dock = DockStyle.Fill
            LabelDescription2.Font = New Font("Segoe UI", 10F)
            LabelDescription2.ForeColor = Color.Gray
            LabelDescription2.Name = "LabelDescription2"
            LabelDescription2.TabIndex = 2
            LabelDescription2.Text = "Automatiza download, verificação de integridade e criação de USBs bootáveis com processos padronizados e rastreáveis."
            LabelDescription2.TextAlign = ContentAlignment.TopCenter
            ' 
            ' LabelDescription3
            ' 
            LabelDescription3.AutoSize = True
            LabelDescription3.Dock = DockStyle.Fill
            LabelDescription3.Font = New Font("Segoe UI", 10F)
            LabelDescription3.ForeColor = Color.Gray
            LabelDescription3.Name = "LabelDescription3"
            LabelDescription3.TabIndex = 3
            LabelDescription3.Text = "Software desenvolvido pela equipe de desenvolvimento Quallit - Marcelo Sobral e Adriano Oliveira."
            LabelDescription3.TextAlign = ContentAlignment.TopCenter
            ' 
            ' LabelDescription4
            ' 
            LabelDescription4.AutoSize = True
            LabelDescription4.Dock = DockStyle.Fill
            LabelDescription4.Font = New Font("Segoe UI", 10F)
            LabelDescription4.ForeColor = Color.Gray
            LabelDescription4.Name = "LabelDescription4"
            LabelDescription4.TabIndex = 4
            LabelDescription4.Text = "Licenciado sob GPLv3 ou posterior. Utiliza DiscUtils (MIT) e pode incorporar lógica/artefatos compatíveis com GPLv3 do Rufus para criação de USB bootável. Código-fonte correspondente: GitHub Quallit. Contato: desenv@quallit.com.br."
            LabelDescription4.TextAlign = ContentAlignment.TopCenter
            ' 
            ' LabelSpacer2
            ' 
            LabelSpacer2.AutoSize = True
            LabelSpacer2.Dock = DockStyle.Fill
            LabelSpacer2.Font = New Font("Segoe UI", 5F)
            LabelSpacer2.ForeColor = Color.Transparent
            LabelSpacer2.Name = "LabelSpacer2"
            LabelSpacer2.TabIndex = 6
            LabelSpacer2.Text = ""
            LabelSpacer2.TextAlign = ContentAlignment.TopCenter
            ' 
            ' LabelVersion
            ' 
            LabelVersion.AutoSize = True
            LabelVersion.Dock = DockStyle.Fill
            LabelVersion.Font = New Font("Segoe UI", 8F)
            LabelVersion.ForeColor = Color.Gray
            LabelVersion.Name = "LabelVersion"
            LabelVersion.TabIndex = 7
            LabelVersion.Text = "Versão: desconhecida"
            LabelVersion.TextAlign = ContentAlignment.TopCenter
            ' 
            ' AboutView
            ' 
            AutoScaleDimensions = New SizeF(10F, 15F)
            AutoScaleMode = AutoScaleMode.Font
            BackColor = Color.White
            Controls.Add(LayoutRoot)
            Name = "AboutView"
            Size = New Size(0, 0)
            LayoutRoot.ResumeLayout(False)
            PanelLogoHost.ResumeLayout(False)
            CType(PictureLogo, System.ComponentModel.ISupportInitialize).EndInit()
            PanelTextHost.ResumeLayout(False)
            PanelTextHost.PerformLayout()
            TableText.ResumeLayout(False)
            TableText.PerformLayout()
            ResumeLayout(False)
        End Sub

        Friend WithEvents JustifiedLabel1 As JustifiedLabel
    End Class
End Namespace


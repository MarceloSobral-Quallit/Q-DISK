' ─────────────────────────────────────────────────────────────────────────────
' qdisk  |  UI / Views  |  DashboardView.vb
' Tela inicial com imagem de fundo e status geral da aplicação.
'
' © Quallit — Licenciado sob GPL-3.0-or-later.
' ─────────────────────────────────────────────────────────────────────────────

Imports System.Drawing
Imports System.Drawing.Drawing2D
Imports System.IO
Imports System.Windows.Forms
Imports qdisk.UI.Theme

Namespace Views
    Partial Public Class DashboardView
        Inherits UserControl

        Private ReadOnly _backgroundImage As Image

        ''' <summary>Pedido de navegação vindo dos atalhos; o MainForm decide se a tela está habilitada.</summary>
        Public Event NavigateRequested(viewName As String)

        Public Sub New()
            InitializeComponent()
            BuildShortcuts()
            _backgroundImage = LoadBackgroundImage()
            If _backgroundImage IsNot Nothing Then
                ContentPanel.BackgroundImage = Nothing
                AddHandler ContentPanel.Paint, AddressOf OnContentPanelPaint
                AddHandler ContentPanel.Resize, AddressOf OnContentPanelResize
                SetDoubleBuffered(ContentPanel, True)
            End If
        End Sub

        Private Function LoadBackgroundImage() As Image
            Dim imagePath = Path.Combine(AppPaths.AssetsDir, "APP_BG.jpg")
            If Not File.Exists(imagePath) Then
                Return Nothing
            End If

            Dim sourceImage = Image.FromFile(imagePath)
            Dim clonedImage = CType(sourceImage.Clone(), Image)
            sourceImage.Dispose()
            Return clonedImage
        End Function

        Private Sub OnContentPanelResize(sender As Object, e As EventArgs)
            ContentPanel.Invalidate()
        End Sub

        Private Sub OnContentPanelPaint(sender As Object, e As PaintEventArgs)
            If _backgroundImage Is Nothing Then
                Return
            End If

            Dim panelSize = ContentPanel.ClientSize
            If panelSize.Width <= 0 OrElse panelSize.Height <= 0 Then
                Return
            End If

            Dim scaleW = panelSize.Width / CSng(_backgroundImage.Width)
            Dim scaleH = panelSize.Height / CSng(_backgroundImage.Height)
            Dim scale = Math.Max(scaleW, scaleH)

            Dim destWidth = CInt(Math.Ceiling(_backgroundImage.Width * scale))
            Dim destHeight = CInt(Math.Ceiling(_backgroundImage.Height * scale))
            Dim destX = 0
            Dim destY = CInt(Math.Floor((panelSize.Height - destHeight) * 0.42F))

            e.Graphics.InterpolationMode = InterpolationMode.HighQualityBicubic
            e.Graphics.DrawImage(_backgroundImage, New Rectangle(destX, destY, destWidth, destHeight))
        End Sub

        ''' <summary>Redesenho: imagem em faixa de 236 px e atalhos para as telas mais usadas.</summary>
        Private Sub BuildShortcuts()
            LayoutRoot.RowStyles(1).SizeType = SizeType.Absolute
            LayoutRoot.RowStyles(1).Height = 236.0F
            LayoutRoot.RowCount = 3
            LayoutRoot.RowStyles.Add(New RowStyle(SizeType.Percent, 100.0F))
            LayoutRoot.Padding = New Padding(0)
            ContentPanel.Margin = New Padding(0)

            Dim host = New TableLayoutPanel() With {
                .Dock = DockStyle.Fill, .ColumnCount = 1, .RowCount = 3, .Padding = New Padding(16), .Margin = New Padding(0),
                .BackColor = Color.White
            }
            host.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100.0F))
            host.RowStyles.Add(New RowStyle(SizeType.Absolute, 118.0F))
            host.RowStyles.Add(New RowStyle(SizeType.AutoSize))
            host.RowStyles.Add(New RowStyle(SizeType.Percent, 100.0F))

            Dim cards = New TableLayoutPanel() With {.Dock = DockStyle.Fill, .ColumnCount = 3, .RowCount = 1, .Margin = New Padding(0)}
            For i = 0 To 2
                cards.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 33.333F))
            Next
            cards.RowStyles.Add(New RowStyle(SizeType.Percent, 100.0F))
            cards.Controls.Add(CreateCard("Downloads", "Fazer o download das imagens.", "download", New Padding(0, 0, 6, 0)), 0, 0)
            cards.Controls.Add(CreateCard("Copiar Backup TIBX", "Copiar TIBX para um pendrive.", "copy", New Padding(6, 0, 6, 0)), 1, 0)
            cards.Controls.Add(CreateCard("Gravar ISO Bootável", "Gerar USB bootável a partir de ISO.", "disc", New Padding(6, 0, 0, 0)), 2, 0)
            host.Controls.Add(cards, 0, 0)

            Dim secondary = New FlowLayoutPanel() With {.AutoSize = True, .WrapContents = False, .Margin = New Padding(0, 12, 0, 0), .Padding = New Padding(0)}
            For Each entry In {("Teste de velocidade", "gauge"), ("Configurações", "sliders"), ("Manual", "book")}
                Dim target = entry.Item1
                Dim btn = New Button() With {.Text = target, .AutoSize = True, .Margin = New Padding(0, 0, 8, 0)}
                UiTheme.StyleButton(btn, ButtonKind.Secondary, UiTheme.Icon16(Me, entry.Item2, "text"))
                AddHandler btn.Click, Sub() RaiseEvent NavigateRequested(target)
                secondary.Controls.Add(btn)
            Next
            host.Controls.Add(secondary, 0, 1)

            LayoutRoot.Controls.Add(host, 0, 2)
        End Sub

        Private Function CreateCard(viewName As String, description As String, iconName As String, margin As Padding) As Control
            Dim outer = New RoundedPanel() With {.BorderColor = UiTheme.BorderLight, .BackColor = Color.White}
            Dim inner As Control = outer
            outer.Dock = DockStyle.Fill
            outer.Margin = margin
            outer.Cursor = Cursors.Hand

            Dim content = New TableLayoutPanel() With {.Dock = DockStyle.Fill, .ColumnCount = 1, .RowCount = 3, .Padding = New Padding(14, 12, 14, 10), .BackColor = Color.White}
            content.RowStyles.Add(New RowStyle(SizeType.Absolute, 34.0F))
            content.RowStyles.Add(New RowStyle(SizeType.AutoSize))
            content.RowStyles.Add(New RowStyle(SizeType.Percent, 100.0F))
            Dim icon = New PictureBox() With {.Size = New Size(24, 24), .SizeMode = PictureBoxSizeMode.Zoom, .Margin = New Padding(0, 0, 0, 6),
                                             .Image = UiTheme.LoadIcon(iconName, "teal", 96, 24)}
            If icon.Image Is Nothing Then
                Dim path24 = Path.Combine(AppPaths.AssetsDir, "icons", "24", $"{iconName}-teal.png")
                If File.Exists(path24) Then icon.Image = Image.FromFile(path24)
            End If
            Dim title = New Label() With {.Text = viewName, .AutoSize = True, .Font = New Font("Segoe UI", 10.0F, FontStyle.Bold), .ForeColor = UiTheme.Navy, .Margin = New Padding(0, 0, 0, 4)}
            Dim desc = New Label() With {.Text = description, .Dock = DockStyle.Fill, .AutoSize = False, .Font = UiTheme.FontBase, .ForeColor = UiTheme.StatusText, .Margin = New Padding(0)}
            content.Controls.Add(icon, 0, 0)
            content.Controls.Add(title, 0, 1)
            content.Controls.Add(desc, 0, 2)
            inner.Controls.Add(content)

            Dim hoverOn As EventHandler = Sub() SetCardBack(content, UiTheme.ActiveBack)
            Dim hoverOff As EventHandler = Sub()
                                               If Not outer.ClientRectangle.Contains(outer.PointToClient(Control.MousePosition)) Then SetCardBack(content, Color.White)
                                           End Sub
            For Each c As Control In New Control() {outer, content, icon, title, desc}
                c.Cursor = Cursors.Hand
                AddHandler c.Click, Sub() RaiseEvent NavigateRequested(viewName)
                AddHandler c.MouseEnter, hoverOn
                AddHandler c.MouseLeave, hoverOff
            Next
            Return outer
        End Function

        Private Shared Sub SetCardBack(content As Control, color As Color)
            content.BackColor = color
            If content.Parent IsNot Nothing Then content.Parent.BackColor = color
        End Sub

        Private Shared Sub SetDoubleBuffered(control As Control, enabled As Boolean)
            Dim prop = GetType(Control).GetProperty("DoubleBuffered", Reflection.BindingFlags.Instance Or Reflection.BindingFlags.NonPublic)
            prop?.SetValue(control, enabled, Nothing)
        End Sub

    End Class
End Namespace


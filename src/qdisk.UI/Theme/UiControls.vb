' ─────────────────────────────────────────────────────────────────────────────
' qdisk  |  UI / Theme  |  UiControls.vb
' Peças reutilizáveis do redesenho.
'   StatusLine        [Nativo]  ícone + texto; painel vermelho claro em erro
'   NavItem           [Nativo]  item do menu com barra de 4 px no ativo
'   TwoLineListRenderer [OnPaint] ListBox de duas linhas com ícone (fase opcional)
'
' © Quallit — Licenciado sob GPL-3.0-or-later.
' ─────────────────────────────────────────────────────────────────────────────

Imports System.Drawing
Imports System.Windows.Forms

Namespace Theme

    ''' <summary>
    ''' Linha de status com ícone. Substitui o uso direto de _statusLabel.Text.
    ''' Mantém um Label interno exposto em TextLabel para código que ainda atribui .Text direto.
    ''' </summary>
    Public Class StatusLine
        Inherits Panel

        Public ReadOnly Property TextLabel As Label
        Private ReadOnly _icon As PictureBox
        Private ReadOnly _inner As Panel
        Private _icons As Dictionary(Of StatusKind, Image)

        Public Sub New()
            MinimumSize = New Size(0, 22)
            Padding = New Padding(1)
            _inner = New Panel() With {.Dock = DockStyle.Fill, .BackColor = Color.White, .Padding = New Padding(0)}
            _icon = New PictureBox() With {.Dock = DockStyle.Left, .Width = 24, .SizeMode = PictureBoxSizeMode.CenterImage}
            TextLabel = New Label() With {.Dock = DockStyle.Fill, .AutoSize = False, .AutoEllipsis = True,
                                         .TextAlign = ContentAlignment.MiddleLeft, .Font = UiTheme.FontBase}
            _inner.Controls.Add(TextLabel)
            _inner.Controls.Add(_icon)
            Controls.Add(_inner)
            SetStatus(String.Empty, StatusKind.Info)
        End Sub

        ''' <summary>Ícones por tipo: info-muted, spinner-navy (ou run.gif), okc-green2, alertc-warn.</summary>
        Public Sub SetIcons(icons As Dictionary(Of StatusKind, Image))
            _icons = icons
        End Sub

        Public Sub SetStatus(text As String, kind As StatusKind)
            TextLabel.Text = text
            Dim isError = kind = StatusKind.Error
            BackColor = If(isError, UiTheme.Red, Color.White)          ' borda de 1 px via Padding
            _inner.BackColor = If(isError, UiTheme.WarningBack, Color.White)
            TextLabel.BackColor = _inner.BackColor
            TextLabel.Font = If(isError OrElse kind = StatusKind.Success, UiTheme.FontBold, UiTheme.FontBase)
            Select Case kind
                Case StatusKind.Error : TextLabel.ForeColor = UiTheme.WarnText
                Case StatusKind.Success : TextLabel.ForeColor = UiTheme.GreenDark
                Case StatusKind.Busy : TextLabel.ForeColor = UiTheme.Navy
                Case Else : TextLabel.ForeColor = UiTheme.StatusText
            End Select
            Height = If(isError, 32, 22)
            Dim img As Image = Nothing
            If _icons IsNot Nothing Then _icons.TryGetValue(kind, img)
            _icon.Image = img
        End Sub
    End Class

    ''' <summary>
    ''' Item do menu lateral: Panel 184 × 34 com barra de 4 px à esquerda e o Button existente em Fill.
    ''' O MainForm continua guardando o Button em _navButtons (Tag = nome da tela).
    ''' </summary>
    Public Class NavItem
        Inherits Panel

        Public ReadOnly Property Button As Button
        Private ReadOnly _indicator As Panel
        Private ReadOnly _iconNormal As Image
        Private ReadOnly _iconActive As Image

        Public Sub New(viewName As String, iconNormal As Image, iconActive As Image)
            Size = New Size(UiTheme.NavItemWidth, UiTheme.NavItemHeight)
            Margin = New Padding(0, 0, 0, 2)
            BackColor = UiTheme.NavBack
            _iconNormal = iconNormal
            _iconActive = iconActive
            _indicator = New Panel() With {.Dock = DockStyle.Left, .Width = 4, .BackColor = UiTheme.NavBack}
            Button = New Button() With {
                .Dock = DockStyle.Fill, .Text = viewName, .Tag = viewName,
                .FlatStyle = FlatStyle.Flat, .TextAlign = ContentAlignment.MiddleLeft,
                .Image = iconNormal, .ImageAlign = ContentAlignment.MiddleLeft,
                .TextImageRelation = TextImageRelation.ImageBeforeText,
                .Padding = New Padding(10, 0, 0, 0), .Font = UiTheme.FontBase,
                .BackColor = UiTheme.NavBack, .ForeColor = UiTheme.MainText, .UseVisualStyleBackColor = False
            }
            Button.FlatAppearance.BorderSize = 0
            Button.FlatAppearance.MouseOverBackColor = UiTheme.ActiveBack
            Button.FlatAppearance.MouseDownBackColor = UiTheme.BorderLight
            _indicator.Visible = False
            Controls.Add(Button)
            Controls.Add(_indicator)
            RoundedButtons.Attach(Button)
            AddHandler Button.Paint, Sub(s As Object, e As PaintEventArgs)
                                         If Not _isActive Then Return
                                         e.Graphics.SmoothingMode = Drawing2D.SmoothingMode.AntiAlias
                                         Dim barHeight = Button.Height - UiRounded.Scaled(Button, 14)
                                         Using path = UiRounded.RoundedPath(New RectangleF(0, (Button.Height - barHeight) / 2.0F, UiRounded.Scaled(Button, 4), barHeight), 2)
                                             Using brush As New SolidBrush(UiTheme.TealDark)
                                                 e.Graphics.FillPath(brush, path)
                                             End Using
                                         End Using
                                     End Sub
        End Sub

        Private _isActive As Boolean

        Public Sub SetActive(isActive As Boolean)
            _isActive = isActive
            _indicator.BackColor = If(isActive, UiTheme.TealDark, UiTheme.NavBack)
            Button.BackColor = If(isActive, UiTheme.ActiveBack, UiTheme.NavBack)
            Button.Font = If(isActive, UiTheme.FontBold, UiTheme.FontBase)
            Button.Image = If(isActive, _iconActive, _iconNormal)
            If isActive Then Button.AccessibleDescription = "Tela atual" Else Button.AccessibleDescription = Nothing
            Button.Invalidate()
        End Sub

        ''' <summary>Separador de grupo (1 px). Grupos: Inicio | operações | Logs, Configurações | Sobre, Manual.</summary>
        Public Shared Function CreateSeparator() As Panel
            Return New Panel() With {.Size = New Size(UiTheme.NavItemWidth - 16, 1), .BackColor = UiTheme.BorderLight, .Margin = New Padding(8, 6, 8, 6)}
        End Function
    End Class

    ''' <summary>
    ''' [OnPaint] ListBox de duas linhas com ícone, para _tibxList e _isoList.
    ''' O provedor devolve título (nome do arquivo), subtítulo ("18.432,5 MB  [TIBX]") e ícone.
    ''' </summary>
    Public NotInheritable Class TwoLineListRenderer

        Public Delegate Function ItemInfoProvider(item As Object, ByRef title As String, ByRef subtitle As String, ByRef icon As Image) As Boolean

        Private ReadOnly _list As ListBox
        Private ReadOnly _provider As ItemInfoProvider
        Private ReadOnly _titleFont As Font = New Font("Segoe UI", 9.0F, FontStyle.Bold)
        Private ReadOnly _subFont As Font = New Font("Segoe UI", 8.0F, FontStyle.Regular)

        Private Sub New(list As ListBox, provider As ItemInfoProvider)
            _list = list
            _provider = provider
        End Sub

        Public Shared Sub Attach(list As ListBox, provider As ItemInfoProvider)
            Dim r = New TwoLineListRenderer(list, provider)
            list.DrawMode = DrawMode.OwnerDrawFixed
            list.ItemHeight = 42
            list.IntegralHeight = False
            AddHandler list.DrawItem, AddressOf r.OnDrawItem
        End Sub

        Private Sub OnDrawItem(sender As Object, e As DrawItemEventArgs)
            If e.Index < 0 OrElse e.Index >= _list.Items.Count Then Return
            Dim selected = (e.State And DrawItemState.Selected) = DrawItemState.Selected
            Dim enabled = _list.Enabled
            Dim g = e.Graphics
            Using back As New SolidBrush(If(selected, UiTheme.ActiveBack, Color.White))
                g.FillRectangle(back, e.Bounds)
            End Using
            If selected Then
                Using bar As New SolidBrush(UiTheme.TealDark)
                    g.FillRectangle(bar, e.Bounds.X, e.Bounds.Y, 3, e.Bounds.Height)
                End Using
            End If
            Using line As New Pen(Color.FromArgb(238, 240, 243))
                g.DrawLine(line, e.Bounds.Left, e.Bounds.Bottom - 1, e.Bounds.Right, e.Bounds.Bottom - 1)
            End Using

            Dim title As String = _list.Items(e.Index).ToString()
            Dim subtitle As String = String.Empty
            Dim icon As Image = Nothing
            _provider(_list.Items(e.Index), title, subtitle, icon)

            Dim x = e.Bounds.X + 10
            If icon IsNot Nothing Then
                Dim iconRect = New Rectangle(x, e.Bounds.Y + (e.Bounds.Height - 20) \ 2, 20, 20)
                If enabled Then g.DrawImage(icon, iconRect) Else ControlPaint.DrawImageDisabled(g, icon, iconRect.X, iconRect.Y, Color.White)
                x += 30
            End If
            Dim textWidth = e.Bounds.Right - x - 8
            Dim titleColor = If(enabled, UiTheme.MainText, SystemColors.GrayText)
            Dim subColor = If(enabled, UiTheme.StatusText, SystemColors.GrayText)
            Const flags As TextFormatFlags = TextFormatFlags.EndEllipsis Or TextFormatFlags.NoPrefix Or TextFormatFlags.SingleLine
            TextRenderer.DrawText(g, title, _titleFont, New Rectangle(x, e.Bounds.Y + 5, textWidth, 18), titleColor, flags)
            TextRenderer.DrawText(g, subtitle, _subFont, New Rectangle(x, e.Bounds.Y + 22, textWidth, 16), subColor, flags)
            If (e.State And DrawItemState.Focus) = DrawItemState.Focus Then e.DrawFocusRectangle()
        End Sub
    End Class

End Namespace

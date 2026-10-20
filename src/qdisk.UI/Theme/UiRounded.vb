' ─────────────────────────────────────────────────────────────────────────────
' qdisk  |  UI / Theme  |  UiRounded.vb
' Cantos arredondados (raio 6 px) em botoes e campos de entrada, com suavizacao.
'   RoundedButtons      [OnPaint]  desenha fundo, borda, icone e texto do Button
'   RoundedPanel        [OnPaint]  painel com borda arredondada (cartoes, contornos)
'   RoundedInputHost    [OnPaint]  contorno de 32 px para TextBox e NumericUpDown
'   RoundedComboPainter [WM_PAINT] contorno arredondado sobre o ComboBox nativo
'
' © Quallit — Licenciado sob GPL-3.0-or-later.
' ─────────────────────────────────────────────────────────────────────────────

Imports System.Drawing
Imports System.Drawing.Drawing2D
Imports System.Runtime.CompilerServices
Imports System.Windows.Forms

Namespace Theme

    Public Module UiRounded
        Public Const Radius As Integer = 6
        ''' <summary>Borda dos campos: MutedText dá 4,69:1 sobre branco (acima de 3:1 para contorno de controle).</summary>
        Public ReadOnly InputBorder As Color = UiTheme.MutedText
        Public ReadOnly InputFocusBorder As Color = UiTheme.TealDark

        Public Function Scaled(owner As Control, value As Integer) As Integer
            Dim dpi = If(owner Is Nothing OrElse owner.DeviceDpi <= 0, 96, owner.DeviceDpi)
            Return CInt(Math.Round(value * dpi / 96.0))
        End Function

        Public Function RoundedPath(rect As RectangleF, r As Single) As GraphicsPath
            Dim path = New GraphicsPath()
            Dim d = Math.Min(r * 2.0F, Math.Min(rect.Width, rect.Height))
            If d <= 0 Then
                path.AddRectangle(rect)
                Return path
            End If
            path.AddArc(rect.X, rect.Y, d, d, 180, 90)
            path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90)
            path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90)
            path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90)
            path.CloseFigure()
            Return path
        End Function

        ''' <summary>Cor de fundo realmente visivel atras do controle (ignora Transparent).</summary>
        Public Function EffectiveBackColor(c As Control) As Color
            Dim p = c
            While p IsNot Nothing
                If p.BackColor.A = 255 Then Return p.BackColor
                p = p.Parent
            End While
            Return SystemColors.Control
        End Function

        ''' <summary>Fundo e borda arredondados, com os cantos pintados na cor de quem esta atras.</summary>
        Public Sub PaintRoundedBox(g As Graphics, owner As Control, bounds As Rectangle, fill As Color, border As Color, Optional borderWidth As Single = 1.0F)
            g.SmoothingMode = SmoothingMode.AntiAlias
            g.PixelOffsetMode = PixelOffsetMode.HighQuality
            Using back As New SolidBrush(If(owner.Parent Is Nothing, owner.BackColor, EffectiveBackColor(owner.Parent)))
                g.FillRectangle(back, bounds)
            End Using
            Dim inset = borderWidth / 2.0F
            Dim rect = New RectangleF(bounds.X + inset, bounds.Y + inset, bounds.Width - borderWidth, bounds.Height - borderWidth)
            Using path = RoundedPath(rect, Scaled(owner, Radius))
                Using brush As New SolidBrush(fill)
                    g.FillPath(brush, path)
                End Using
                If border.A > 0 Then
                    Using pen As New Pen(border, borderWidth)
                        g.DrawPath(pen, path)
                    End Using
                End If
            End Using
        End Sub
    End Module

    ''' <summary>Pinta os botoes estilizados com cantos arredondados. Estado de hover e clique fica aqui.</summary>
    Public Module RoundedButtons
        Private NotInheritable Class PaintState
            Public Hover As Boolean
            Public Pressed As Boolean
            Public FocusFromMouse As Boolean
        End Class

        Private ReadOnly _states As New ConditionalWeakTable(Of Button, PaintState)()

        Public Sub Attach(btn As Button)
            Dim state As PaintState = Nothing
            If _states.TryGetValue(btn, state) Then Return
            state = New PaintState()
            _states.Add(btn, state)
            AddHandler btn.MouseEnter, Sub()
                                           state.Hover = True
                                           btn.Invalidate()
                                       End Sub
            AddHandler btn.MouseLeave, Sub()
                                           state.Hover = False
                                           state.Pressed = False
                                           btn.Invalidate()
                                       End Sub
            AddHandler btn.MouseDown, Sub(s As Object, e As MouseEventArgs)
                                          state.FocusFromMouse = True
                                          If e.Button = MouseButtons.Left Then state.Pressed = True : btn.Invalidate()
                                      End Sub
            AddHandler btn.MouseUp, Sub()
                                        state.Pressed = False
                                        btn.Invalidate()
                                    End Sub
            AddHandler btn.EnabledChanged, Sub() btn.Invalidate()
            AddHandler btn.GotFocus, Sub() btn.Invalidate()
            AddHandler btn.LostFocus, Sub()
                                          state.FocusFromMouse = False
                                          btn.Invalidate()
                                      End Sub
            AddHandler btn.Paint, Sub(s As Object, e As PaintEventArgs) PaintButton(btn, state, e.Graphics)
        End Sub

        Private Sub PaintButton(btn As Button, state As PaintState, g As Graphics)
            Dim fill = btn.BackColor
            If btn.Enabled Then
                If state.Pressed Then
                    fill = btn.FlatAppearance.MouseDownBackColor
                ElseIf state.Hover Then
                    fill = btn.FlatAppearance.MouseOverBackColor
                End If
            End If
            If fill.IsEmpty Then fill = btn.BackColor
            Dim border = btn.FlatAppearance.BorderColor
            If border.IsEmpty OrElse btn.FlatAppearance.BorderSize = 0 Then border = fill
            PaintRoundedBox(g, btn, btn.ClientRectangle, fill, border)

            ' Foco: anel interno de 1 px, so quando o botao tem o foco
            If btn.Focused AndAlso btn.Enabled AndAlso Not state.FocusFromMouse AndAlso Not state.Hover Then
                Dim r = btn.ClientRectangle
                r.Inflate(-3, -3)
                Using path = RoundedPath(r, Math.Max(2, Scaled(btn, Radius) - 2))
                    Using pen As New Pen(If(btn.ForeColor = Color.White, Color.White, UiTheme.TealDark), 1.0F) With {.DashStyle = DashStyle.Dot}
                        g.DrawPath(pen, path)
                    End Using
                End Using
            End If

            ' Icone + texto, centralizados (ou a esquerda, conforme TextAlign)
            Dim text = btn.Text
            Dim textColor = If(btn.Enabled, btn.ForeColor, SystemColors.GrayText)
            Dim img = btn.Image
            Const flags As TextFormatFlags = TextFormatFlags.SingleLine Or TextFormatFlags.VerticalCenter Or TextFormatFlags.NoPrefix Or TextFormatFlags.EndEllipsis
            Dim textSize = TextRenderer.MeasureText(g, text, btn.Font, New Size(Integer.MaxValue, Integer.MaxValue),
                                                    TextFormatFlags.SingleLine Or TextFormatFlags.NoPrefix Or TextFormatFlags.NoPadding)
            Dim gap = If(img Is Nothing OrElse String.IsNullOrEmpty(text), 0, Scaled(btn, 6))
            Dim contentWidth = textSize.Width + If(img Is Nothing, 0, img.Width + gap)
            Dim left As Integer
            Select Case btn.TextAlign
                Case ContentAlignment.MiddleLeft, ContentAlignment.TopLeft, ContentAlignment.BottomLeft
                    left = btn.Padding.Left + Scaled(btn, 6)
                Case Else
                    left = Math.Max(btn.Padding.Left, (btn.ClientSize.Width - contentWidth) \ 2)
            End Select
            If img IsNot Nothing Then
                g.DrawImage(img, left, (btn.ClientSize.Height - img.Height) \ 2, img.Width, img.Height)
                left += img.Width + gap
            End If
            Dim textRect = New Rectangle(left, 0, Math.Max(0, btn.ClientSize.Width - left - btn.Padding.Right - 2), btn.ClientSize.Height)
            TextRenderer.DrawText(g, text, btn.Font, textRect, textColor, flags Or TextFormatFlags.NoPadding)
        End Sub
    End Module

    ''' <summary>Painel com fundo e borda arredondados. Filhos devem ficar a pelo menos 2 px da borda (Padding).</summary>
    Public Class RoundedPanel
        Inherits Panel

        Private _borderColor As Color = UiTheme.BorderLight

        Public Sub New()
            DoubleBuffered = True
            ResizeRedraw = True
            Padding = New Padding(2)
            BackColor = Color.White
        End Sub

        Public Property BorderColor As Color
            Get
                Return _borderColor
            End Get
            Set(value As Color)
                _borderColor = value
                Invalidate()
            End Set
        End Property

        Protected Overrides Sub OnPaintBackground(e As PaintEventArgs)
            PaintRoundedBox(e.Graphics, Me, ClientRectangle, BackColor, _borderColor)
        End Sub
    End Class

    ''' <summary>
    ''' Contorno arredondado de altura fixa para um TextBox ou NumericUpDown (que nao aceitam cantos
    ''' arredondados): o campo fica sem borda propria, centralizado na vertical. Borda TealDark com foco.
    ''' </summary>
    Public Class RoundedInputHost
        Inherits RoundedPanel

        Public ReadOnly Property Inner As Control
        Public Property NormalBorderColor As Color = InputBorder

        Private Sub New(inner As Control)
            Me.Inner = inner
            Padding = New Padding(0)
            BorderColor = InputBorder
            BackColor = Color.White
            AddHandler inner.Enter, Sub() BorderColor = InputFocusBorder
            AddHandler inner.Leave, Sub() BorderColor = NormalBorderColor
            AddHandler Click, Sub() inner.Focus()
        End Sub

        Protected Overrides Sub OnLayout(e As LayoutEventArgs)
            MyBase.OnLayout(e)
            If Inner Is Nothing Then Return
            Dim padX = Scaled(Me, 8)
            Inner.SetBounds(padX, Math.Max(1, (ClientSize.Height - Inner.Height) \ 2), Math.Max(10, ClientSize.Width - padX * 2), Inner.Height)
        End Sub

        ''' <summary>Coloca o controle dentro do contorno, no mesmo lugar e com o mesmo Dock/Anchor/Margin.</summary>
        Public Shared Function Wrap(inner As Control, Optional width As Integer = 0) As RoundedInputHost
            Dim host = New RoundedInputHost(inner) With {
                .Dock = inner.Dock, .Anchor = inner.Anchor, .Margin = inner.Margin, .Location = inner.Location,
                .Width = If(width > 0, width, inner.Width), .Height = Scaled(inner, UiTheme.ButtonHeight)
            }
            If inner.Dock = DockStyle.Fill Then host.Dock = DockStyle.None : host.Anchor = AnchorStyles.Left Or AnchorStyles.Right
            Dim tb = TryCast(inner, TextBox)
            If tb IsNot Nothing Then tb.BorderStyle = BorderStyle.None
            Dim nud = TryCast(inner, NumericUpDown)
            If nud IsNot Nothing Then nud.BorderStyle = BorderStyle.None
            If inner.Parent IsNot Nothing Then UiStatus.ReplaceInParent(inner, host)
            inner.Dock = DockStyle.None
            inner.Anchor = AnchorStyles.None
            inner.Margin = New Padding(0)
            host.Controls.Add(inner)
            Return host
        End Function
    End Class

    ''' <summary>
    ''' ComboBox (DropDownList) com 32 px de altura e contorno arredondado: o item e desenhado pelo app
    ''' (OwnerDrawFixed) e, depois de cada WM_PAINT nativo, o contorno e pintado por cima.
    ''' </summary>
    Public NotInheritable Class RoundedComboPainter
        Inherits NativeWindow

        Private Const WM_PAINT As Integer = &HF
        Private ReadOnly _combo As ComboBox
        Private _focused As Boolean

        Private Sub New(combo As ComboBox)
            _combo = combo
            AddHandler combo.HandleCreated, Sub() AssignHandle(combo.Handle)
            AddHandler combo.HandleDestroyed, Sub() ReleaseHandle()
            AddHandler combo.GotFocus, Sub()
                                           _focused = True
                                           combo.Invalidate()
                                       End Sub
            AddHandler combo.LostFocus, Sub()
                                            _focused = False
                                            combo.Invalidate()
                                        End Sub
            If combo.IsHandleCreated Then AssignHandle(combo.Handle)
        End Sub

        Public Shared Sub Attach(combo As ComboBox)
            combo.FlatStyle = FlatStyle.Flat
            combo.DrawMode = DrawMode.OwnerDrawFixed
            combo.IntegralHeight = False
            combo.ItemHeight = Scaled(combo, UiTheme.ButtonHeight) - 6
            combo.BackColor = Color.White
            combo.ForeColor = UiTheme.MainText
            AddHandler combo.DrawItem, AddressOf DrawComboItem
            Dim painter = New RoundedComboPainter(combo)
        End Sub

        Private Shared Sub DrawComboItem(sender As Object, e As DrawItemEventArgs)
            Dim combo = DirectCast(sender, ComboBox)
            Dim isEdit = (e.State And DrawItemState.ComboBoxEdit) = DrawItemState.ComboBoxEdit
            Dim selected = (e.State And DrawItemState.Selected) = DrawItemState.Selected AndAlso Not isEdit
            Using back As New SolidBrush(If(selected, UiTheme.ActiveBack, Color.White))
                e.Graphics.FillRectangle(back, e.Bounds)
            End Using
            If e.Index < 0 Then Return
            Dim text = combo.GetItemText(combo.Items(e.Index))
            Dim itemColor = If(combo.Enabled, UiTheme.MainText, SystemColors.GrayText)
            Dim r = e.Bounds
            r.X += 4
            r.Width -= 4
            TextRenderer.DrawText(e.Graphics, text, combo.Font, r, itemColor,
                                  TextFormatFlags.VerticalCenter Or TextFormatFlags.SingleLine Or TextFormatFlags.EndEllipsis Or TextFormatFlags.NoPrefix)
        End Sub

        Protected Overrides Sub WndProc(ByRef m As Message)
            MyBase.WndProc(m)
            If m.Msg <> WM_PAINT Then Return
            Using g = Graphics.FromHwnd(_combo.Handle)
                g.SmoothingMode = SmoothingMode.AntiAlias
                g.PixelOffsetMode = PixelOffsetMode.HighQuality
                Dim bounds = _combo.ClientRectangle
                ' Cobre a borda quadrada nativa: moldura de 2 px fora do caminho arredondado
                Dim outer = New RectangleF(0.5F, 0.5F, bounds.Width - 1.0F, bounds.Height - 1.0F)
                Using path = RoundedPath(outer, Scaled(_combo, Radius))
                    Using region = New Region(bounds)
                        region.Exclude(path)
                        Using back As New SolidBrush(EffectiveBackColor(_combo.Parent))
                            g.FillRegion(back, region)
                        End Using
                    End Using
                    Using innerPen As New Pen(Color.White, 2.0F)
                        Dim inner = New RectangleF(1.5F, 1.5F, bounds.Width - 3.0F, bounds.Height - 3.0F)
                        Using innerPath = RoundedPath(inner, Math.Max(2, Scaled(_combo, Radius) - 1))
                            ' limpa a borda nativa de 1 px sem apagar a seta (lado direito)
                            g.SetClip(New Rectangle(0, 0, bounds.Width - SystemInformation.VerticalScrollBarWidth - 2, bounds.Height))
                            g.DrawPath(innerPen, innerPath)
                            g.ResetClip()
                        End Using
                    End Using
                    ' Area da seta: fundo branco e chevron cinza (o estilo Flat nativo desenha um bloco escuro)
                    Dim arrowWidth = SystemInformation.VerticalScrollBarWidth + 2
                    Dim arrowRect = New Rectangle(bounds.Width - arrowWidth - 1, 2, arrowWidth - 1, bounds.Height - 4)
                    Using white As New SolidBrush(Color.White)
                        g.FillRectangle(white, arrowRect)
                    End Using
                    Dim cx = arrowRect.X + arrowRect.Width / 2.0F - 1
                    Dim cy = bounds.Height / 2.0F
                    Dim half = Scaled(_combo, 4)
                    Using chevron As New Pen(If(_combo.Enabled, UiTheme.StatusText, SystemColors.GrayText), 1.6F)
                        g.DrawLines(chevron, New PointF() {New PointF(cx - half, cy - half / 2.0F), New PointF(cx, cy + half / 2.0F), New PointF(cx + half, cy - half / 2.0F)})
                    End Using
                    Using pen As New Pen(If(_focused, InputFocusBorder, If(_combo.Enabled, InputBorder, UiTheme.BorderLight)), 1.0F)
                        g.DrawPath(pen, path)
                    End Using
                End Using
            End Using
        End Sub
    End Class
End Namespace

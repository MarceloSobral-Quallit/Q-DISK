' ─────────────────────────────────────────────────────────────────────────────
' qdisk  |  UI / Theme  |  UiTheme.vb
' Tokens visuais e helpers de estilo do redesenho (apenas controles padrão).
' Tudo aqui é [Nativo]: cores sólidas, FlatStyle.Flat, Dock/Padding, PNG.
'
' © Quallit — Licenciado sob GPL-3.0-or-later.
' ─────────────────────────────────────────────────────────────────────────────

Imports System.Drawing
Imports System.IO
Imports System.Runtime.CompilerServices
Imports System.Windows.Forms

Namespace Theme

    Public Enum ButtonKind
        Primary     ' ação principal da tela (Baixar, Copiar TIBX para USB, Gravar ISO Bootável...)
        Secondary   ' demais ações
        Danger      ' ação destrutiva (Zerar USB, Limpar cache de imagens)
        Header      ' Alterar Perfil, sobre o cabeçalho azul-marinho
    End Enum

    Public Enum StatusKind
        Info
        Busy
        Success
        [Error]
    End Enum

    Public Module UiTheme

        ' ── Cores já usadas pelo app (RESTRICOES_WINFORMS.md, seção 3) ──────────
        Public ReadOnly Navy As Color = Color.FromArgb(32, 45, 64)
        Public ReadOnly NavyLight As Color = Color.FromArgb(70, 85, 105)
        Public ReadOnly NavBack As Color = Color.FromArgb(245, 247, 250)
        Public ReadOnly ActiveBack As Color = Color.FromArgb(230, 236, 245)
        Public ReadOnly BorderLight As Color = Color.FromArgb(220, 224, 230)
        Public ReadOnly TitleText As Color = Color.FromArgb(60, 70, 80)
        Public ReadOnly StatusText As Color = Color.FromArgb(80, 90, 100)
        Public ReadOnly MetricText As Color = Color.FromArgb(73, 80, 87)
        Public ReadOnly MutedText As Color = Color.FromArgb(108, 117, 125)   ' só sobre branco (4,69:1)
        Public ReadOnly MainText As Color = Color.FromArgb(33, 37, 41)
        Public ReadOnly Green As Color = Color.FromArgb(40, 167, 69)         ' só ícones; nunca texto
        Public ReadOnly GreenBack As Color = Color.FromArgb(225, 245, 232)
        Public ReadOnly Red As Color = Color.FromArgb(220, 53, 69)
        Public ReadOnly WarnText As Color = Color.FromArgb(200, 40, 40)
        Public ReadOnly NeutralBadge As Color = Color.FromArgb(233, 236, 239)
        Public ReadOnly LinkBlue As Color = Color.FromArgb(0, 102, 204)
        Public ReadOnly LogBack As Color = Color.FromArgb(248, 250, 252)     ' já usado no log do Teste de velocidade

        ' ── Cores novas (3) ─────────────────────────────────────────────────────
        ' Azul-petróleo escuro: derivado do azul do logo rgb(27,155,168), que dá só 3,33:1 com branco.
        ' Uso: barra do item ativo do menu, números das etapas, ícones dos atalhos do Inicio. Branco sobre ele: 5,49:1.
        Public ReadOnly TealDark As Color = Color.FromArgb(14, 116, 128)
        ' Fundo de aviso: faixa do Modo Administrador e painéis de erro. Texto WarnText sobre ele: 4,78:1.
        Public ReadOnly WarningBack As Color = Color.FromArgb(252, 234, 236)
        ' Verde escuro: selo "Melhor" e texto de sucesso. Branco sobre ele: 5,14:1 (o verde atual dá 3,13:1).
        Public ReadOnly GreenDark As Color = Color.FromArgb(30, 126, 52)

        ' ── Tipografia ──────────────────────────────────────────────────────────
        Public ReadOnly FontBase As New Font("Segoe UI", 9.0F, FontStyle.Regular)
        Public ReadOnly FontBold As New Font("Segoe UI", 9.0F, FontStyle.Bold)
        Public ReadOnly FontSmall As New Font("Segoe UI", 8.0F, FontStyle.Regular)
        Public ReadOnly FontReading As New Font("Segoe UI", 10.0F, FontStyle.Regular)
        Public ReadOnly FontCardTitle As New Font("Segoe UI", 11.0F, FontStyle.Bold)
        Public ReadOnly FontContentTitle As New Font("Segoe UI", 12.0F, FontStyle.Bold)
        Public ReadOnly FontHeaderTitle As New Font("Segoe UI", 14.0F, FontStyle.Bold)
        Public ReadOnly FontLog As New Font("Consolas", 9.0F, FontStyle.Regular)

        ' ── Medidas (px a 96 DPI; AutoScaleMode.Font escala o resto) ────────────
        Public Const ButtonHeight As Integer = 32
        Public Const ButtonHeightCompact As Integer = 28
        Public Const ContentPadding As Integer = 16
        Public Const BlockGap As Integer = 12
        Public Const ButtonGap As Integer = 8
        Public Const NavItemWidth As Integer = 184
        Public Const NavItemHeight As Integer = 34
        Public Const GridRowHeight As Integer = 30
        Public Const GridHeaderHeight As Integer = 32

        ' ── Botões ─────────────────────────────────────────────────────────────
        Private NotInheritable Class ButtonState
            Public Kind As ButtonKind
            Public IconNormal As Image
            Public IconDisabled As Image
        End Class

        Private ReadOnly _buttonStates As New ConditionalWeakTable(Of Button, ButtonState)()

        ''' <summary>Aplica o estilo do redesenho. Não usa Tag (o menu já usa Tag para o nome da tela).</summary>
        Public Sub StyleButton(btn As Button, kind As ButtonKind, Optional icon As Image = Nothing, Optional compact As Boolean = False)
            If btn Is Nothing Then Return
            btn.FlatStyle = FlatStyle.Flat
            btn.UseVisualStyleBackColor = False
            btn.FlatAppearance.BorderSize = 1
            btn.Font = FontBase
            btn.Height = If(compact, ButtonHeightCompact, ButtonHeight)
            btn.Padding = New Padding(6, 0, 6, 0)
            btn.TextAlign = ContentAlignment.MiddleCenter

            Dim state As ButtonState = Nothing
            If Not _buttonStates.TryGetValue(btn, state) Then
                state = New ButtonState()
                _buttonStates.Add(btn, state)
                AddHandler btn.EnabledChanged, AddressOf OnStyledButtonEnabledChanged
            End If
            state.Kind = kind
            state.IconNormal = icon
            state.IconDisabled = If(icon Is Nothing, Nothing, ToolStripRenderer.CreateDisabledImage(icon))

            If icon IsNot Nothing Then
                btn.TextImageRelation = TextImageRelation.ImageBeforeText
                btn.ImageAlign = ContentAlignment.MiddleCenter
            End If
            ApplyButtonColors(btn, state)
            RoundedButtons.Attach(btn)
        End Sub

        Private Sub OnStyledButtonEnabledChanged(sender As Object, e As EventArgs)
            Dim btn = TryCast(sender, Button)
            Dim state As ButtonState = Nothing
            If btn IsNot Nothing AndAlso _buttonStates.TryGetValue(btn, state) Then
                ApplyButtonColors(btn, state)
            End If
        End Sub

        Private Sub ApplyButtonColors(btn As Button, state As ButtonState)
            If Not btn.Enabled Then
                ' Texto desabilitado é desenhado pelo Windows em SystemColors.GrayText.
                btn.BackColor = NavBack
                btn.FlatAppearance.BorderColor = BorderLight
                btn.FlatAppearance.MouseOverBackColor = NavBack
                btn.FlatAppearance.MouseDownBackColor = NavBack
                btn.Image = state.IconDisabled
                btn.Cursor = Cursors.Default
                Return
            End If

            btn.Image = state.IconNormal
            btn.Cursor = Cursors.Hand
            Select Case state.Kind
                Case ButtonKind.Primary
                    btn.BackColor = Navy
                    btn.ForeColor = Color.White
                    btn.FlatAppearance.BorderColor = Navy
                    btn.FlatAppearance.MouseOverBackColor = NavyLight
                    btn.FlatAppearance.MouseDownBackColor = Color.FromArgb(20, 29, 42)
                Case ButtonKind.Danger
                    btn.BackColor = Color.White
                    btn.ForeColor = Red
                    btn.FlatAppearance.BorderColor = Red
                    btn.FlatAppearance.MouseOverBackColor = WarningBack
                    btn.FlatAppearance.MouseDownBackColor = Color.FromArgb(248, 215, 218)
                Case ButtonKind.Header
                    btn.BackColor = Navy
                    btn.ForeColor = Color.White
                    btn.FlatAppearance.BorderColor = NavyLight
                    btn.FlatAppearance.MouseOverBackColor = NavyLight
                    btn.FlatAppearance.MouseDownBackColor = NavyLight
                Case Else
                    btn.BackColor = Color.White
                    btn.ForeColor = MainText
                    btn.FlatAppearance.BorderColor = MutedText
                    btn.FlatAppearance.MouseOverBackColor = ActiveBack
                    btn.FlatAppearance.MouseDownBackColor = BorderLight
            End Select
        End Sub

        ' ── Ícones PNG (assets\icons\<tamanho>\<nome>-<cor>.png) ────────────────
        ''' <summary>Carrega um ícone do pacote. Usa 32 px quando o DPI passa de 144 (150%).</summary>
        Public Function LoadIcon(name As String, iconVariant As String, Optional dpi As Integer = 96, Optional logicalSize As Integer = 16) As Image
            Dim size = If(dpi > 144, logicalSize * 2, logicalSize)
            Dim path = IO.Path.Combine(AppPaths.AssetsDir, "icons", size.ToString(), $"{name}-{iconVariant}.png")
            If Not File.Exists(path) Then Return Nothing
            Using fs = New FileStream(path, FileMode.Open, FileAccess.Read)
                Dim img = Image.FromStream(fs)
                If size = logicalSize Then Return New Bitmap(img)
                Return New Bitmap(img, New Size(CInt(logicalSize * dpi / 96.0), CInt(logicalSize * dpi / 96.0)))
            End Using
        End Function

        ''' <summary>Atalho: ícone de 16 px para a escala do controle.</summary>
        Public Function Icon16(owner As Control, name As String, iconVariant As String) As Image
            Return LoadIcon(name, iconVariant, If(owner Is Nothing, 96, owner.DeviceDpi), 16)
        End Function

        ' ── Painel com borda colorida sem OnPaint ──────────────────────────────
        ''' <summary>Panel externo com BackColor = cor da borda e Padding 1; o interno tem a cor de fundo.</summary>
        Public Function CreateBorderedPanel(borderColor As Color, backColor As Color, ByRef inner As Panel) As Panel
            Dim outer = New Panel() With {.BackColor = borderColor, .Padding = New Padding(1), .Margin = New Padding(0)}
            inner = New Panel() With {.Dock = DockStyle.Fill, .BackColor = backColor}
            outer.Controls.Add(inner)
            Return outer
        End Function

        ' ── Faixa de aviso (Modo Administrador) ─────────────────────────────────
        ''' <summary>
        ''' Substitui AdminPanel nas telas USB. Reaproveite o Label e o Button existentes
        ''' (_adminWarningLabel, _adminRestartButton) para não mexer nos handlers.
        ''' </summary>
        Public Function CreateWarningBanner(warningLabel As Label, actionButton As Button, Optional icon As Image = Nothing) As Panel
            Dim inner As Panel = Nothing
            Dim outer = CreateBorderedPanel(Red, WarningBack, inner)
            outer.Dock = DockStyle.Fill
            outer.MinimumSize = New Size(0, 44)

            Dim table = New TableLayoutPanel() With {
                .Dock = DockStyle.Fill, .ColumnCount = 3, .RowCount = 1,
                .Padding = New Padding(10, 5, 5, 5), .BackColor = WarningBack
            }
            table.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, 26.0F))
            table.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100.0F))
            table.ColumnStyles.Add(New ColumnStyle(SizeType.AutoSize))
            table.RowStyles.Add(New RowStyle(SizeType.Percent, 100.0F))

            Dim pic = New PictureBox() With {.Image = icon, .SizeMode = PictureBoxSizeMode.CenterImage, .Dock = DockStyle.Fill, .Margin = New Padding(0)}
            warningLabel.Dock = DockStyle.Fill
            warningLabel.AutoSize = False
            warningLabel.TextAlign = ContentAlignment.MiddleLeft
            warningLabel.ForeColor = WarnText
            warningLabel.BackColor = WarningBack
            warningLabel.Font = FontBase
            actionButton.Anchor = AnchorStyles.Right
            actionButton.AutoSize = True
            actionButton.Margin = New Padding(8, 0, 0, 0)

            table.Controls.Add(pic, 0, 0)
            table.Controls.Add(warningLabel, 1, 0)
            table.Controls.Add(actionButton, 2, 0)
            inner.Controls.Add(table)
            Return outer
        End Function

        ' ── Número de etapa (1, 2) ─────────────────────────────────────────────
        Public Function CreateStepBadge(number As Integer) As Label
            Return New Label() With {
                .Text = number.ToString(), .AutoSize = False, .Size = New Size(20, 20),
                .TextAlign = ContentAlignment.MiddleCenter, .BackColor = TealDark, .ForeColor = Color.White,
                .Font = FontBold, .Margin = New Padding(0, 0, 8, 0), .Anchor = AnchorStyles.Left
            }
        End Function

        ' ── Título de seção (Configurações; rótulos USB:/Imagens TIBX) ─────────
        Public Sub StyleSectionLabel(lbl As Label)
            lbl.Font = FontBold
            lbl.ForeColor = TitleText
        End Sub

        Public Function CreateDivider(Optional vertical As Boolean = False) As Panel
            Return New Panel() With {
                .BackColor = BorderLight, .Margin = New Padding(0),
                .Height = If(vertical, 0, 1), .Width = If(vertical, 1, 0),
                .Dock = If(vertical, DockStyle.Left, DockStyle.Top)
            }
        End Function

        ' ── DataGridView ───────────────────────────────────────────────────────
        ''' <summary>Grade de Downloads. Coloque a grade dentro de CreateBorderedPanel(BorderLight, White).</summary>
        Public Sub StyleGrid(grid As DataGridView)
            grid.BackgroundColor = Color.White
            grid.BorderStyle = BorderStyle.None
            grid.EnableHeadersVisualStyles = False
            grid.RowHeadersVisible = False
            grid.AllowUserToResizeRows = False
            grid.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal
            grid.GridColor = BorderLight
            grid.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.Single
            grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing
            grid.ColumnHeadersHeight = GridHeaderHeight
            grid.RowTemplate.Height = GridRowHeight

            With grid.ColumnHeadersDefaultCellStyle
                .BackColor = NavBack
                .ForeColor = TitleText
                .Font = FontBold
                .SelectionBackColor = NavBack
                .SelectionForeColor = TitleText
                .Padding = New Padding(4, 0, 4, 0)
            End With
            With grid.DefaultCellStyle
                .BackColor = Color.White
                .ForeColor = MainText
                .Font = FontBase
                .SelectionBackColor = ActiveBack
                .SelectionForeColor = MainText
                .Padding = New Padding(4, 0, 4, 0)
            End With
        End Sub

        ''' <summary>Durante operação longa: além de Enabled = False, deixa o texto cinza para o bloqueio ser visível.</summary>
        Public Sub SetGridBusyLook(grid As DataGridView, isBusy As Boolean)
            grid.DefaultCellStyle.ForeColor = If(isBusy, SystemColors.GrayText, MainText)
            grid.DefaultCellStyle.SelectionForeColor = If(isBusy, SystemColors.GrayText, MainText)
        End Sub

        ' ── Barra de status (StatusStrip) ───────────────────────────────────────
        Public Sub StyleStatusStrip(strip As StatusStrip, adminLabel As ToolStripStatusLabel, Optional shieldIcon As Image = Nothing)
            strip.BackColor = NavBack
            strip.SizingGrip = True
            adminLabel.ForeColor = WarnText                 ' antes: Color.Red (4,0:1)
            adminLabel.Font = New Font("Segoe UI", 9.0F, FontStyle.Bold)
            adminLabel.Image = shieldIcon
            adminLabel.ImageAlign = ContentAlignment.MiddleLeft
        End Sub

    End Module
End Namespace

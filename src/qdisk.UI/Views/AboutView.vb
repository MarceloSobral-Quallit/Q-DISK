' ─────────────────────────────────────────────────────────────────────────────
' qdisk  |  UI / Views  |  AboutView.vb
' Tela ‘Sobre’ com informações de versão e créditos.
'
' © Quallit — Licenciado sob GPL-3.0-or-later.
' ─────────────────────────────────────────────────────────────────────────────

Imports System.Drawing
Imports System.IO
Imports System.Reflection
Imports System.Windows.Forms
Imports qdisk.Core.Services
Imports qdisk.UI.Theme

Namespace Views
    Partial Public Class AboutView
        Inherits UserControl

        Private ReadOnly LinkDownloadUpdate As LinkLabel

        Public Sub New()
            InitializeComponent()

            Dim imagePath = Path.Combine(AppPaths.AssetsDir, "logo-quallit-512x512.png")
            If File.Exists(imagePath) Then
                PictureLogo.Image = Image.FromFile(imagePath)
            End If

            LabelVersion.Text = GetVersionInfo()

            LinkDownloadUpdate = New LinkLabel() With {
                .AutoSize = True,
                .Dock = DockStyle.Fill,
                .Font = New Font("Segoe UI", 9.0F, FontStyle.Bold),
                .LinkColor = Color.FromArgb(0, 102, 204),
                .Name = "LinkDownloadUpdate",
                .TabIndex = 8,
                .Text = "Baixar instalador da atualizacao",
                .TextAlign = ContentAlignment.TopCenter,
                .VisitedLinkColor = Color.FromArgb(0, 102, 204)
            }
            LinkDownloadUpdate.Links.Add(0, LinkDownloadUpdate.Text.Length, CatalogService.RemoteInstallerUrl)
            AddHandler LinkDownloadUpdate.LinkClicked, AddressOf LinkDownloadUpdate_LinkClicked
            TableText.Controls.Add(LinkDownloadUpdate, 0, 8)

            Dim updateTextLayout As Action =
                Sub()
                    ' Usar 100% da largura disponível para todos os Labels
                    Dim targetWidth = Math.Max(300, PanelTextHost.ClientSize.Width - 20) ' 20px de margem total
                    
                    ' Aplicar a todos os 8 Labels consistentemente
                    LabelTitle.MaximumSize = New Size(targetWidth, 0)
                    LabelTitle.Width = targetWidth
                    LabelTitle.Height = MeasureWrappedTextHeight(LabelTitle.Text, LabelTitle.Font, targetWidth)
                    
                    LabelSpacer1.MaximumSize = New Size(targetWidth, 0)
                    LabelSpacer1.Width = targetWidth
                    LabelSpacer1.Height = 15 ' Altura fixa para espaçamento reduzido
                    
                    LabelDescription1.MaximumSize = New Size(targetWidth, 0)
                    LabelDescription1.Width = targetWidth
                    LabelDescription1.Height = MeasureWrappedTextHeight(LabelDescription1.Text, LabelDescription1.Font, targetWidth)
                    
                    LabelDescription2.MaximumSize = New Size(targetWidth, 0)
                    LabelDescription2.Width = targetWidth
                    LabelDescription2.Height = MeasureWrappedTextHeight(LabelDescription2.Text, LabelDescription2.Font, targetWidth)
                    
                    LabelDescription3.MaximumSize = New Size(targetWidth, 0)
                    LabelDescription3.Width = targetWidth
                    LabelDescription3.Height = MeasureWrappedTextHeight(LabelDescription3.Text, LabelDescription3.Font, targetWidth)
                    
                    LabelDescription4.MaximumSize = New Size(targetWidth, 0)
                    LabelDescription4.Width = targetWidth
                    LabelDescription4.Height = MeasureWrappedTextHeight(LabelDescription4.Text, LabelDescription4.Font, targetWidth)
                    
                    LabelSpacer2.MaximumSize = New Size(targetWidth, 0)
                    LabelSpacer2.Width = targetWidth
                    LabelSpacer2.Height = 15 ' Altura fixa para espaçamento reduzido
                    
                    LabelVersion.MaximumSize = New Size(targetWidth, 0)
                    LabelVersion.Width = targetWidth
                    LabelVersion.Height = MeasureWrappedTextHeight(LabelVersion.Text, LabelVersion.Font, targetWidth)

                    LinkDownloadUpdate.MaximumSize = New Size(targetWidth, 0)
                    LinkDownloadUpdate.Width = targetWidth
                    LinkDownloadUpdate.Height = MeasureWrappedTextHeight(LinkDownloadUpdate.Text, LinkDownloadUpdate.Font, targetWidth)
                    
                    ' Centralizar o TableText no PanelTextHost
                    TableText.Width = targetWidth
                    TableText.Left = Math.Max(0, (PanelTextHost.ClientSize.Width - TableText.Width) \ 2)
                    
                    ' Ajustar logo proporcionalmente
                    PictureLogo.Width = CInt(targetWidth * 0.4)
                    PictureLogo.Height = Math.Min(PanelLogoHost.ClientSize.Height, CInt(targetWidth * 0.4))
                    PictureLogo.Left = Math.Max(0, (PanelLogoHost.ClientSize.Width - PictureLogo.Width) \ 2)
                    PictureLogo.Top = Math.Max(0, (PanelLogoHost.ClientSize.Height - PictureLogo.Height) \ 2)
                End Sub

            ' Redesenho (HANDOFF 5.6) substitui o layout centralizado (updateTextLayout).
            ApplyRedesign()
        End Sub

        Private Sub ApplyRedesign()
            SuspendLayout()
            Controls.Remove(LayoutRoot)
            Dim root = New TableLayoutPanel() With {.Dock = DockStyle.Fill, .ColumnCount = 1, .Padding = New Padding(28, 24, 28, 24), .BackColor = Color.White, .AutoScroll = True}
            root.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100.0F))

            ' Topo: logo | titulo, versao e link
            Dim top = New TableLayoutPanel() With {.Dock = DockStyle.Top, .AutoSize = True, .ColumnCount = 2, .RowCount = 1, .Margin = New Padding(0)}
            top.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, 124.0F))
            top.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100.0F))
            PictureLogo.Parent?.Controls.Remove(PictureLogo)
            PictureLogo.Size = New Size(104, 104)
            PictureLogo.SizeMode = PictureBoxSizeMode.Zoom
            PictureLogo.Margin = New Padding(0)
            top.Controls.Add(PictureLogo, 0, 0)
            Dim stack = New FlowLayoutPanel() With {.FlowDirection = FlowDirection.TopDown, .WrapContents = False, .AutoSize = True, .Margin = New Padding(0, 6, 0, 0)}
            For Each c As Control In New Control() {LabelTitle, LabelVersion, LinkDownloadUpdate}
                c.Parent?.Controls.Remove(c)
                c.Dock = DockStyle.None
                c.AutoSize = True
                c.MaximumSize = Size.Empty
                c.Margin = New Padding(0, 0, 0, 4)
                stack.Controls.Add(c)
            Next
            LabelTitle.Font = UiTheme.FontHeaderTitle
            LabelTitle.ForeColor = UiTheme.Navy
            LabelTitle.TextAlign = ContentAlignment.MiddleLeft
            LabelVersion.Font = UiTheme.FontBase
            LabelVersion.ForeColor = UiTheme.StatusText
            LabelVersion.TextAlign = ContentAlignment.TopLeft
            LinkDownloadUpdate.Image = UiTheme.Icon16(Me, "download", "link")
            LinkDownloadUpdate.ImageAlign = ContentAlignment.MiddleLeft
            LinkDownloadUpdate.Padding = New Padding(18, 0, 0, 0)
            LinkDownloadUpdate.TextAlign = ContentAlignment.MiddleLeft
            top.Controls.Add(stack, 1, 0)
            root.Controls.Add(top)

            Dim divider = UiTheme.CreateDivider()
            divider.Dock = DockStyle.Top
            divider.Margin = New Padding(0, 16, 0, 16)
            root.Controls.Add(divider)

            Dim descriptions = New Label() {LabelDescription1, LabelDescription2, LabelDescription3}
            For Each lbl In descriptions
                lbl.Parent?.Controls.Remove(lbl)
                lbl.Font = UiTheme.FontReading
                lbl.ForeColor = UiTheme.StatusText
                lbl.TextAlign = ContentAlignment.TopLeft
                lbl.AutoSize = True
                lbl.Margin = New Padding(0, 0, 0, 14)
                root.Controls.Add(lbl)
            Next

            Dim licenseInner As Panel = Nothing
            Dim licenseBox = UiTheme.CreateBorderedPanel(UiTheme.BorderLight, UiTheme.NavBack, licenseInner)
            licenseBox.Margin = New Padding(0, 4, 0, 0)
            licenseInner.Padding = New Padding(12)
            LabelDescription4.Parent?.Controls.Remove(LabelDescription4)
            LabelDescription4.Font = UiTheme.FontBase
            LabelDescription4.ForeColor = UiTheme.StatusText
            LabelDescription4.BackColor = UiTheme.NavBack
            LabelDescription4.TextAlign = ContentAlignment.TopLeft
            LabelDescription4.AutoSize = True
            LabelDescription4.Location = New Point(12, 12)
            licenseInner.Controls.Add(LabelDescription4)
            root.Controls.Add(licenseBox)

            Dim relayout As Action =
                Sub()
                    Dim w = Math.Max(300, root.ClientSize.Width - root.Padding.Horizontal - 2)
                    For Each lbl In descriptions
                        lbl.MaximumSize = New Size(w, 0)
                    Next
                    LabelDescription4.MaximumSize = New Size(w - 26, 0)
                    licenseBox.Width = w
                    licenseBox.Height = LabelDescription4.PreferredHeight + 26
                    divider.Width = w
                End Sub
            AddHandler root.SizeChanged, Sub() relayout()
            Controls.Add(root)
            relayout()
            ResumeLayout(True)
        End Sub

        Private Function MeasureWrappedTextHeight(textValue As String, font As Font, width As Integer) As Integer
            If String.IsNullOrWhiteSpace(textValue) OrElse width <= 0 Then
                Return 0
            End If
            Dim size = TextRenderer.MeasureText(textValue, font, New Size(width, Integer.MaxValue),
                                                TextFormatFlags.WordBreak Or TextFormatFlags.NoPadding)
            Return size.Height + 4
        End Function

        Private Function GetVersionInfo() As String
            Dim version = Assembly.GetExecutingAssembly().GetName().Version
            Dim versionText = If(AppVersionFormat.Format(version), "desconhecida")

            Dim buildDate = "desconhecida"
            For Each attr As AssemblyMetadataAttribute In Assembly.GetExecutingAssembly().GetCustomAttributes(GetType(AssemblyMetadataAttribute), False)
                If attr.Key = "BuildDate" Then
                    buildDate = attr.Value
                    Exit For
                End If
            Next

            Return $"Versao: {versionText}{Environment.NewLine}Compilado em: {buildDate}"
        End Function

        Private Sub LinkDownloadUpdate_LinkClicked(sender As Object, e As LinkLabelLinkClickedEventArgs)
            Dim targetUrl = TryCast(e.Link.LinkData, String)
            If String.IsNullOrWhiteSpace(targetUrl) Then
                targetUrl = CatalogService.RemoteInstallerUrl
            End If

            Try
                LinkDownloadUpdate.LinkVisited = True
                Process.Start(New ProcessStartInfo(targetUrl) With {.UseShellExecute = True})
            Catch ex As Exception
                MessageBox.Show($"Nao foi possivel abrir o link do instalador:{Environment.NewLine}{targetUrl}{Environment.NewLine}{Environment.NewLine}{ex.Message}",
                                "Atualizacao", MessageBoxButtons.OK, MessageBoxIcon.Warning)
            End Try
        End Sub


        Public Class JustifiedLabel
            Inherits Label

            Protected Overrides Sub OnPaint(e As PaintEventArgs)
                e.Graphics.TextRenderingHint = Drawing.Text.TextRenderingHint.ClearTypeGridFit
                Dim layoutWidth = ClientSize.Width
                If layoutWidth <= 0 Then
                    MyBase.OnPaint(e)
                    Return
                End If

                Dim textValue = If(Text, String.Empty)
                Dim words = textValue.Split(New Char() {" "c}, StringSplitOptions.RemoveEmptyEntries)
                If words.Length = 0 Then
                    Return
                End If

                Dim lineHeight = TextRenderer.MeasureText(e.Graphics, "Ag", Font, New Size(Integer.MaxValue, Integer.MaxValue), TextFormatFlags.NoPadding).Height
                Dim y = 0
                Dim lineWords As New List(Of String)()
                Dim lineWidth = 0
                Dim spaceWidth = TextRenderer.MeasureText(e.Graphics, " ", Font, New Size(Integer.MaxValue, Integer.MaxValue), TextFormatFlags.NoPadding).Width

                Dim flushLine As Action(Of Boolean) =
                    Sub(isLastLine As Boolean)
                        If lineWords.Count = 0 Then
                            Return
                        End If

                        If lineWords.Count = 1 OrElse isLastLine Then
                            Dim lineText = String.Join(" ", lineWords)
                            TextRenderer.DrawText(e.Graphics, lineText, Font, New Point(0, y), ForeColor, TextFormatFlags.NoPadding)
                        Else
                            Dim totalWordsWidth = 0
                            For Each w In lineWords
                                totalWordsWidth += TextRenderer.MeasureText(e.Graphics, w, Font, New Size(Integer.MaxValue, Integer.MaxValue), TextFormatFlags.NoPadding).Width
                            Next
                            Dim gaps = lineWords.Count - 1
                            Dim totalSpace = Math.Max(0, layoutWidth - totalWordsWidth)
                            Dim space = If(gaps > 0, totalSpace \ gaps, spaceWidth)
                            Dim extra = If(gaps > 0, totalSpace Mod gaps, 0)
                            Dim x = 0
                            For i = 0 To lineWords.Count - 1
                                Dim w = lineWords(i)
                                TextRenderer.DrawText(e.Graphics, w, Font, New Point(x, y), ForeColor, TextFormatFlags.NoPadding)
                                x += TextRenderer.MeasureText(e.Graphics, w, Font, New Size(Integer.MaxValue, Integer.MaxValue), TextFormatFlags.NoPadding).Width
                                If i < gaps Then
                                    x += space
                                    If extra > 0 Then
                                        x += 1
                                        extra -= 1
                                    End If
                                End If
                            Next
                        End If

                        y += lineHeight
                        lineWords.Clear()
                        lineWidth = 0
                    End Sub

                For Each word In words
                    Dim wordWidth = TextRenderer.MeasureText(e.Graphics, word, Font, New Size(Integer.MaxValue, Integer.MaxValue), TextFormatFlags.NoPadding).Width
                    Dim newWidth = If(lineWords.Count = 0, wordWidth, lineWidth + spaceWidth + wordWidth)
                    If newWidth > layoutWidth AndAlso lineWords.Count > 0 Then
                        flushLine(False)
                    End If
                    If lineWords.Count > 0 Then
                        lineWidth += spaceWidth + wordWidth
                    Else
                        lineWidth = wordWidth
                    End If
                    lineWords.Add(word)
                Next

                flushLine(True)
            End Sub
        End Class

        Private Sub JustifiedLabel1_Click(sender As Object, e As EventArgs) Handles JustifiedLabel1.Click

        End Sub
    End Class
End Namespace

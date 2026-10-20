' ─────────────────────────────────────────────────────────────────────────────
' qdisk  |  UI / Theme  |  UiSpeedTest.vb
' Peças do Teste de velocidade: cartão da origem (velocidade ao vivo, gráfico por
' segundo, tabela 1/4/8) e o gráfico. Compartilhado com tools/Quallit.NetSpeedTest,
' que compila os arquivos desta pasta.
'
' © Quallit — Licenciado sob GPL-3.0-or-later.
' ─────────────────────────────────────────────────────────────────────────────

Imports System.Drawing
Imports System.Drawing.Drawing2D
Imports System.Windows.Forms
Imports qdisk.Core.Models
Imports qdisk.Core.Services

Namespace Theme
    ''' <summary>Cartão de uma origem: item medido, velocidade ao vivo, gráfico por segundo e tabela 1/4/8.</summary>
    Friend Class OriginCard
        Private ReadOnly _inner As Panel
        Private ReadOnly _check As CheckBox
        Private ReadOnly _badge As Label
        Private ReadOnly _itemCombo As ComboBox
        Private ReadOnly _host As Label
        Private ReadOnly _liveValue As Label
        Private ReadOnly _liveDetail As Label
        Private ReadOnly _chart As SpeedChart
        Private ReadOnly _notes As Label
        Private ReadOnly _rows As New Dictionary(Of Integer, RowLabels)()
        Private ReadOnly _measurements As New List(Of SpeedMeasurement)()

        Public ReadOnly Property Origin As DownloadOrigin
        Public ReadOnly Property Panel As Panel

        Public Sub New(origin As DownloadOrigin, connectionOptions As Integer())
            Me.Origin = origin
            Dim inner As Panel = Nothing
            Panel = UiTheme.CreateBorderedPanel(UiTheme.BorderLight, Color.White, inner)
            Panel.Dock = DockStyle.Fill
            _inner = inner

            Dim grid = New TableLayoutPanel() With {.Dock = DockStyle.Fill, .ColumnCount = 1, .Padding = New Padding(12, 8, 12, 6), .BackColor = Color.Transparent}
            grid.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100.0F))
            inner.Controls.Add(grid)

            ' Cabeçalho: caixa de seleção com o nome da origem, selo à direita
            Dim header = New Panel() With {.Dock = DockStyle.Fill, .Height = 32, .Margin = New Padding(0, 0, 0, 4), .BackColor = Color.Transparent}
            _check = New CheckBox() With {.Text = Title, .Checked = True, .Font = UiTheme.FontCardTitle, .ForeColor = UiTheme.Navy, .Dock = DockStyle.Fill, .BackColor = Color.Transparent}
            _badge = New Label() With {.Text = "Aguardando", .Dock = DockStyle.Fill, .TextAlign = ContentAlignment.MiddleCenter, .BackColor = UiTheme.NeutralBadge, .ForeColor = UiTheme.MetricText}
            Dim badgeHost = New Panel() With {.Dock = DockStyle.Right, .Width = 100, .Padding = New Padding(0, 4, 0, 4), .BackColor = Color.Transparent}
            badgeHost.Controls.Add(_badge)
            header.Controls.Add(_check)
            header.Controls.Add(badgeHost)
            AddRow(grid, header, 32)

            _itemCombo = New ComboBox() With {.DropDownStyle = ComboBoxStyle.DropDownList, .Dock = DockStyle.Fill, .Margin = New Padding(0)}
            RoundedComboPainter.Attach(_itemCombo)
            AddRow(grid, _itemCombo, 32)
            _host = New Label() With {.Text = "Host: -", .Dock = DockStyle.Fill, .ForeColor = UiTheme.MetricText, .TextAlign = ContentAlignment.MiddleLeft, .AutoEllipsis = True, .Margin = New Padding(0)}
            AddRow(grid, _host, 20)

            ' Velocidade ao vivo
            _liveValue = New Label() With {.Text = "— MiB/s", .Dock = DockStyle.Fill, .Font = New Font("Segoe UI", 16.0F, FontStyle.Bold), .ForeColor = UiTheme.Navy, .TextAlign = ContentAlignment.MiddleLeft, .Margin = New Padding(0)}
            AddRow(grid, _liveValue, 32)
            _liveDetail = New Label() With {.Text = "Aguardando o início do teste.", .Dock = DockStyle.Fill, .ForeColor = UiTheme.StatusText, .TextAlign = ContentAlignment.MiddleLeft, .AutoEllipsis = True, .Margin = New Padding(0)}
            AddRow(grid, _liveDetail, 20)
            _chart = New SpeedChart() With {.Dock = DockStyle.Fill, .Margin = New Padding(0, 4, 0, 8)}
            AddRow(grid, _chart, 64)

            ' Tabela por quantidade de conexões
            Dim table = New TableLayoutPanel() With {.Dock = DockStyle.Fill, .ColumnCount = 5, .RowCount = connectionOptions.Length + 1, .Margin = New Padding(0), .BackColor = Color.Transparent}
            For Each width In {25.0F, 15.0F, 22.0F, 17.0F, 21.0F}
                table.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, width))
            Next
            For i = 0 To connectionOptions.Length
                table.RowStyles.Add(New RowStyle(SizeType.Absolute, 20.0F))
            Next
            Dim headers = {"Conexões", "MiB/s", "Mín–máx", "Ativas", "1º byte"}
            For col = 0 To headers.Length - 1
                table.Controls.Add(New Label() With {.Text = headers(col), .Dock = DockStyle.Fill, .Font = UiTheme.FontSmall, .ForeColor = UiTheme.MutedText, .TextAlign = ContentAlignment.MiddleLeft, .Margin = New Padding(0)}, col, 0)
            Next
            For i = 0 To connectionOptions.Length - 1
                Dim row = New RowLabels(connectionOptions(i))
                For col = 0 To row.Cells.Length - 1
                    table.Controls.Add(row.Cells(col), col, i + 1)
                Next
                _rows(connectionOptions(i)) = row
            Next
            AddRow(grid, table, 20 * (connectionOptions.Length + 1) + 2)

            Dim divider = UiTheme.CreateDivider()
            divider.Dock = DockStyle.Fill
            divider.Margin = New Padding(0, 2, 0, 2)
            AddRow(grid, divider, 5)
            _notes = New Label() With {.Text = "Resultado informativo.", .Dock = DockStyle.Fill, .ForeColor = UiTheme.StatusText, .AutoEllipsis = True, .Margin = New Padding(0)}
            grid.RowStyles.Add(New RowStyle(SizeType.Percent, 100.0F))
            grid.Controls.Add(_notes, 0, grid.RowStyles.Count - 1)
        End Sub

        Private Shared Sub AddRow(grid As TableLayoutPanel, control As Control, height As Integer)
            grid.RowStyles.Add(New RowStyle(SizeType.Absolute, height))
            grid.Controls.Add(control, 0, grid.RowStyles.Count - 1)
        End Sub

        Public ReadOnly Property Title As String
            Get
                Select Case Origin
                    Case DownloadOrigin.Dropbox : Return "Dropbox"
                    Case DownloadOrigin.SharePoint : Return "SharePoint"
                    Case Else : Return "Outra"
                End Select
            End Get
        End Property

        Public ReadOnly Property IsSelected As Boolean
            Get
                Return _check.Checked
            End Get
        End Property

        Public ReadOnly Property SelectedItem As CatalogItem
            Get
                Return TryCast(_itemCombo.SelectedItem, ItemOption)?.Item
            End Get
        End Property

        Public ReadOnly Property BestMeasurement As SpeedMeasurement
            Get
                Return _measurements.Where(Function(m) m.IsOk).OrderByDescending(Function(m) m.MiBps).FirstOrDefault()
            End Get
        End Property

        Public Sub SetItems(items As IReadOnlyList(Of CatalogItem))
            _itemCombo.Items.Clear()
            For Each item In items
                _itemCombo.Items.Add(New ItemOption(item))
            Next
            If _itemCombo.Items.Count > 0 Then
                _itemCombo.SelectedIndex = 0
            Else
                _check.Checked = False
                _liveDetail.Text = "Nenhum item desta origem nos catálogos."
            End If
        End Sub

        ''' <summary>Volta a selecionar o item pelo nome (depois de recarregar os catálogos); senão mantém o primeiro.</summary>
        Public Sub SelectItemByName(name As String)
            If String.IsNullOrWhiteSpace(name) Then Return
            For i = 0 To _itemCombo.Items.Count - 1
                If String.Equals(CType(_itemCombo.Items(i), ItemOption).Item.Name, name, StringComparison.OrdinalIgnoreCase) Then
                    _itemCombo.SelectedIndex = i
                    Return
                End If
            Next
        End Sub

        Public Sub SetEditable(editable As Boolean)
            _check.Enabled = editable AndAlso _itemCombo.Items.Count > 0
            _itemCombo.Enabled = editable AndAlso _itemCombo.Items.Count > 0
        End Sub

        Public Sub ResetResults()
            _measurements.Clear()
            _inner.BackColor = Color.White
            SetBadge("Aguardando", UiTheme.NeutralBadge, UiTheme.MetricText)
            _host.Text = "Host: -"
            _liveValue.Text = "— MiB/s"
            _liveValue.ForeColor = UiTheme.Navy
            _liveDetail.Text = If(IsSelected, "Na fila.", "Fora deste teste.")
            _chart.SetData(Array.Empty(Of Double)(), 0, False)
            _notes.Text = "Resultado informativo."
            For Each row In _rows.Values
                row.Reset()
            Next
        End Sub

        ''' <summary>Destaca a linha em medição (Nothing: resolvendo o link).</summary>
        Public Sub SetMeasuring(connections As Integer?)
            SetBadge("Medindo", UiTheme.TealDark, Color.White)
            For Each pair In _rows
                pair.Value.SetActive(connections.HasValue AndAlso pair.Key = connections.Value)
            Next
            If connections.HasValue Then
                _rows(connections.Value).SetPending()
                _liveDetail.Text = $"{connections.Value} {If(connections.Value = 1, "conexão", "conexões")} · abrindo..."
                _chart.SetData(Array.Empty(Of Double)(), 0, True)
            Else
                _liveDetail.Text = "Resolvendo o link..."
            End If
        End Sub

        Public Sub StopMeasuring()
            For Each row In _rows.Values
                row.SetActive(False)
            Next
            If _badge.Text = "Medindo" Then SetBadge("Cancelado", UiTheme.NeutralBadge, UiTheme.MetricText)
        End Sub

        Public Sub SetHost(host As String)
            _host.Text = $"Host: {host}"
        End Sub

        Public Sub SetLinkFailure(message As String)
            SetBadge("Falha", UiTheme.WarningBack, UiTheme.WarnText)
            _liveDetail.Text = "Link não resolvido."
            _notes.Text = message
        End Sub

        Public Sub ShowLive(reading As LiveReading, connections As Integer)
            _liveValue.Text = $"{reading.CurrentMiBps:N1} MiB/s"
            _liveDetail.Text = $"{connections} {If(connections = 1, "conexão", "conexões")} · média {reading.AverageMiBps:N1} MiB/s · {reading.ActiveConnections}/{connections} ativas · {reading.TotalBytes / 1048576.0R:N0} MiB{If(reading.SlowSwaps > 0, $" · {reading.SlowSwaps} troca(s)", "")}"
            _chart.SetData(reading.PerSecondMiBps, reading.CurrentMiBps, True)
            If _rows.ContainsKey(connections) Then _rows(connections).ShowLive(reading.CurrentMiBps, reading.Elapsed, reading.Duration)
        End Sub

        Public Sub ApplyMeasurement(m As SpeedMeasurement)
            _measurements.Add(m)
            If _rows.ContainsKey(m.Connections) Then _rows(m.Connections).Apply(m)
            If m.IsOk Then
                _chart.SetData(m.PerSecondMiBps, 0, False)
                _liveValue.Text = $"{m.MiBps:N1} MiB/s"
                _liveDetail.Text = $"{m.Connections} {If(m.Connections = 1, "conexão", "conexões")} · média sustentada (sem os {NetSpeedTestService.WarmupSeconds} s iniciais)"
            End If
        End Sub

        ''' <summary>Fim da origem: destaca a melhor quantidade de conexões e escreve a recomendação.</summary>
        Public Sub FinishOrigin()
            For Each row In _rows.Values
                row.SetActive(False)
            Next
            Dim best = BestMeasurement
            If best Is Nothing Then
                SetBadge("Falha", UiTheme.WarningBack, UiTheme.WarnText)
                _liveValue.Text = "— MiB/s"
                _notes.Text = If(_measurements.FirstOrDefault()?.Status, "Nenhuma medição concluída.")
                Return
            End If
            SetBadge("Concluído", UiTheme.NeutralBadge, UiTheme.MetricText)
            _rows(best.Connections).MarkBest()
            _liveValue.Text = $"{best.MiBps:N1} MiB/s"
            _liveDetail.Text = $"Melhor resultado: {best.Connections} {If(best.Connections = 1, "conexão", "conexões")}"
            Dim bestSamples = _measurements.Where(Function(m) m.Connections = best.Connections).First()
            _chart.SetData(bestSamples.PerSecondMiBps, 0, False)

            Dim notes As New List(Of String)()
            Dim oneConnection = _measurements.FirstOrDefault(Function(m) m.Connections = 1 AndAlso m.IsOk)
            If best.Connections = 1 Then
                notes.Add("Várias conexões não ajudam nesta origem.")
            ElseIf oneConnection IsNot Nothing AndAlso oneConnection.MiBps > 0 Then
                Dim gain = (best.MiBps / oneConnection.MiBps - 1) * 100
                notes.Add(If(gain < 10, $"Ganho pequeno com {best.Connections} conexões (+{gain:N0}%).", $"{best.Connections} conexões: +{gain:N0}% sobre 1 conexão."))
            Else
                notes.Add($"Melhor com {best.Connections} conexões.")
            End If
            If best.Connections > 1 AndAlso best.ActiveConnections < best.Connections Then notes.Add($"Só {best.ActiveConnections} de {best.Connections} entregaram dados.")
            If _measurements.Any(Function(m) m.Status.StartsWith("Range")) Then notes.Add("Range não suportado.")
            _notes.Text = String.Join(" ", notes)
        End Sub

        Public Sub MarkBest()
            _inner.BackColor = UiTheme.GreenBack
            SetBadge("Melhor", UiTheme.GreenDark, Color.White)
        End Sub

        Private Sub SetBadge(text As String, back As Color, fore As Color)
            _badge.Text = text
            _badge.BackColor = back
            _badge.ForeColor = fore
        End Sub

        Private Class ItemOption
            Public Sub New(item As CatalogItem)
                Me.Item = item
            End Sub
            Public ReadOnly Property Item As CatalogItem
            Public Overrides Function ToString() As String
                Return If(Item.ImageSizeBytes > 0, $"{Item.Name}  ({Item.ImageSizeBytes / 1073741824.0R:N1} GiB)", Item.Name)
            End Function
        End Class

        ''' <summary>Uma linha da tabela (uma quantidade de conexões).</summary>
        Private Class RowLabels
            Public ReadOnly Cells As Label()

            Public Sub New(connections As Integer)
                Cells = Enumerable.Range(0, 5).Select(Function(i) New Label() With {.Dock = DockStyle.Fill, .TextAlign = ContentAlignment.MiddleLeft, .Margin = New Padding(0), .AutoEllipsis = True, .BackColor = Color.Transparent}).ToArray()
                Cells(0).Text = connections.ToString()
                Cells(0).Font = UiTheme.FontBold
                Reset()
            End Sub

            Public Sub Reset()
                For i = 1 To Cells.Length - 1
                    Cells(i).Text = "-"
                    Cells(i).Font = UiTheme.FontBase
                    Cells(i).ForeColor = UiTheme.MainText
                Next
                SetActive(False)
            End Sub

            Public Sub SetPending()
                For i = 1 To Cells.Length - 1
                    Cells(i).Text = "…"
                Next
            End Sub

            Public Sub SetActive(active As Boolean)
                For Each cell In Cells
                    cell.BackColor = If(active, UiTheme.ActiveBack, Color.Transparent)
                Next
            End Sub

            Public Sub ShowLive(currentMiBps As Double, elapsed As TimeSpan, duration As TimeSpan)
                Cells(1).Text = $"{currentMiBps:N1}"
                Cells(1).ForeColor = UiTheme.TealDark
                Cells(2).Text = $"{elapsed.TotalSeconds:N0}/{duration.TotalSeconds:N0} s"
            End Sub

            Public Sub Apply(m As SpeedMeasurement)
                Cells(1).ForeColor = UiTheme.MainText
                If m.IsOk Then
                    Cells(1).Text = $"{m.MiBps:N1}"
                    Cells(2).Text = $"{m.MinMiBps:N0}–{m.MaxMiBps:N0}"
                    Cells(3).Text = $"{m.ActiveConnections}/{m.Connections}"
                    Cells(4).Text = $"{m.FirstByteMs} ms"
                Else
                    Cells(1).Text = If(m.Status.StartsWith("Range"), "sem Range", "falha")
                    Cells(1).ForeColor = UiTheme.WarnText
                    For i = 2 To Cells.Length - 1
                        Cells(i).Text = "-"
                    Next
                End If
            End Sub

            Public Sub MarkBest()
                ' Verde na linha toda; negrito só na média (negrito em tudo não cabe no cartão estreito do app)
                For Each cell In Cells
                    cell.ForeColor = UiTheme.GreenDark
                Next
                Cells(1).Font = UiTheme.FontBold
            End Sub
        End Class
    End Class

    ''' <summary>Gráfico de barras: MiB/s de cada segundo; aquecimento em cinza, linha da média sustentada.</summary>
    Friend Class SpeedChart
        Inherits Control

        Private _values As Double() = Array.Empty(Of Double)()
        Private _current As Double
        Private _live As Boolean

        Public Sub New()
            SetStyle(ControlStyles.AllPaintingInWmPaint Or ControlStyles.OptimizedDoubleBuffer Or ControlStyles.UserPaint Or ControlStyles.ResizeRedraw, True)
            BackColor = UiTheme.LogBack
        End Sub

        ''' <param name="current">Vazão do segundo em andamento (barra parcial à direita), só ao vivo.</param>
        Public Sub SetData(values As Double(), current As Double, live As Boolean)
            _values = If(values, Array.Empty(Of Double)())
            _current = current
            _live = live
            Invalidate()
        End Sub

        Protected Overrides Sub OnPaint(e As PaintEventArgs)
            Dim g = e.Graphics
            g.Clear(BackColor)
            g.SmoothingMode = SmoothingMode.AntiAlias
            Dim area = New Rectangle(6, 17, Width - 12, Height - 31)
            Dim count = _values.Length + If(_live, 1, 0)
            If count = 0 OrElse area.Width <= 0 OrElse area.Height <= 0 Then
                TextRenderer.DrawText(g, If(_live, "medindo…", "sem dados"), UiTheme.FontSmall, ClientRectangle, UiTheme.MutedText, TextFormatFlags.HorizontalCenter Or TextFormatFlags.VerticalCenter)
                Return
            End If
            Dim maxValue = Math.Max(1.0R, Math.Max(If(_values.Length > 0, _values.Max(), 0), _current)) * 1.1
            Dim slots = Math.Max(count, 10)
            Dim slotWidth = area.Width / CDbl(slots)
            Dim barWidth = CSng(Math.Max(2.0R, slotWidth * 0.7))
            Using warm As New SolidBrush(UiTheme.BorderLight), steady As New SolidBrush(UiTheme.TealDark), partialBrush As New SolidBrush(Color.FromArgb(120, UiTheme.TealDark))
                For i = 0 To count - 1
                    Dim value = If(i < _values.Length, _values(i), _current)
                    Dim h = CSng(area.Height * value / maxValue)
                    Dim x = CSng(area.Left + i * slotWidth + (slotWidth - barWidth) / 2)
                    Dim brush = If(i >= _values.Length, partialBrush, If(i < NetSpeedTestService.WarmupSeconds, warm, steady))
                    g.FillRectangle(brush, x, area.Bottom - h, barWidth, h)
                Next
            End Using
            ' Linha da média sustentada (sem o aquecimento)
            If _values.Length > NetSpeedTestService.WarmupSeconds Then
                Dim avg = _values.Skip(NetSpeedTestService.WarmupSeconds).Average()
                Dim y = CSng(area.Bottom - area.Height * avg / maxValue)
                Using pen As New Pen(UiTheme.Navy, 1.0F) With {.DashStyle = DashStyle.Dash}
                    g.DrawLine(pen, area.Left, y, area.Right, y)
                End Using
                TextRenderer.DrawText(g, $"- - média {avg:N1} MiB/s", UiTheme.FontSmall, New Point(area.Left - 2, 1), UiTheme.Navy)
            End If
            TextRenderer.DrawText(g, $"máx {maxValue / 1.1:N0} MiB/s", UiTheme.FontSmall, New Rectangle(0, 1, Width - 4, 16), UiTheme.MutedText, TextFormatFlags.Right)
            TextRenderer.DrawText(g, "segundos →", UiTheme.FontSmall, New Rectangle(0, Height - 14, Width - 4, 14), UiTheme.MutedText, TextFormatFlags.Right)
        End Sub
    End Class
End Namespace

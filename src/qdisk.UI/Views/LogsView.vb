' ─────────────────────────────────────────────────────────────────────────────
' qdisk  |  UI / Views  |  LogsView.vb
' View de exibição e pesquisa do histórico de operações.
'
' © Quallit — Licenciado sob GPL-3.0-or-later.
' ─────────────────────────────────────────────────────────────────────────────

Imports System.Drawing
Imports System.Windows.Forms
Imports qdisk.Core.Services
Imports qdisk.UI.Theme

Namespace Views
    Partial Public Class LogsView
        Inherits UserControl

        Private ReadOnly _logService As LogService
        Private ReadOnly _list As ListView
        Private _rows As New List(Of String())
        Private _rawLines As New List(Of String)

        Public Sub New()
            InitializeComponent()
            _logService = New LogService()

            ' Redesenho (HANDOFF 5.4): colunas Data e hora | Usuario | Versao | Evento
            UiStatus.StyleAutoButton(ButtonRefresh, ButtonKind.Secondary, UiTheme.Icon16(Me, "refresh", "text"))
            LayoutRoot.Padding = New Padding(16)
            LayoutRoot.RowStyles(0).Height = 44.0F
            Dim header = New TableLayoutPanel() With {.Dock = DockStyle.Fill, .ColumnCount = 2, .RowCount = 1, .Margin = New Padding(0)}
            header.ColumnStyles.Add(New ColumnStyle(SizeType.AutoSize))
            header.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100.0F))
            ButtonRefresh.Anchor = AnchorStyles.Left
            ButtonRefresh.Margin = New Padding(0)
            header.Controls.Add(ButtonRefresh, 0, 0)
            header.Controls.Add(New Label() With {.Text = "Clique duas vezes em uma linha para ver o texto completo.", .Dock = DockStyle.Fill,
                                                  .TextAlign = ContentAlignment.MiddleRight, .ForeColor = UiTheme.StatusText, .Margin = New Padding(0)}, 1, 0)
            UiStatus.ReplaceInParent(HeaderPanel, header)

            _list = New ListView() With {
                .View = View.Details, .FullRowSelect = True, .GridLines = False, .VirtualMode = True, .MultiSelect = False,
                .HeaderStyle = ColumnHeaderStyle.Nonclickable, .BorderStyle = BorderStyle.None, .Dock = DockStyle.Fill,
                .ShowItemToolTips = True, .Font = UiTheme.FontBase
            }
            _list.Columns.Add("Data e hora", 146)
            _list.Columns.Add("Usuário", 104)
            _list.Columns.Add("Versão", 80)
            _list.Columns.Add("Evento", 300)
            AddHandler _list.RetrieveVirtualItem, AddressOf OnRetrieveVirtualItem
            AddHandler _list.DoubleClick, AddressOf OnListDoubleClick
            AddHandler _list.Resize, Sub() FitEventColumn()
            Dim inner As Panel = Nothing
            Dim box = UiTheme.CreateBorderedPanel(UiTheme.BorderLight, Color.White, inner)
            box.Dock = DockStyle.Fill
            box.Margin = New Padding(0)
            inner.Controls.Add(_list)
            UiStatus.ReplaceInParent(TextBoxLogs, box)
            TextBoxLogs.Visible = False
            Controls.Add(TextBoxLogs)

            LoadLogs()
            ' Na criacao a lista ainda nao esta visivel e o EnsureVisible do LoadLogs nao rola; rola ao aparecer.
            AddHandler _list.VisibleChanged, Sub() ScrollToLatest()
        End Sub

        Private Sub ScrollToLatest()
            If _list.Visible AndAlso _rows IsNot Nothing AndAlso _rows.Count > 0 Then
                _list.EnsureVisible(_rows.Count - 1)
            End If
        End Sub

        Private Sub FitEventColumn()
            Dim used = 0
            For i = 0 To 2
                used += _list.Columns(i).Width
            Next
            _list.Columns(3).Width = Math.Max(200, _list.ClientSize.Width - used)
        End Sub

        Private Sub OnRetrieveVirtualItem(sender As Object, e As RetrieveVirtualItemEventArgs)
            Dim parts = _rows(e.ItemIndex)
            Dim item = New ListViewItem(parts(0))
            item.SubItems.Add(parts(1))
            item.SubItems.Add(parts(2))
            Dim eventItem = item.SubItems.Add(parts(3))
            eventItem.Font = UiTheme.FontLog
            item.UseItemStyleForSubItems = False
            Dim back = If(e.ItemIndex Mod 2 = 1, UiTheme.LogBack, Color.White)
            For Each s As ListViewItem.ListViewSubItem In item.SubItems
                s.BackColor = back
            Next
            item.ToolTipText = parts(3)
            e.Item = item
        End Sub

        Private Sub OnListDoubleClick(sender As Object, e As EventArgs)
            If _list.SelectedIndices.Count = 0 Then Return
            Dim index = _list.SelectedIndices(0)
            MessageBox.Show(_rawLines(index), "Logs", MessageBoxButtons.OK, MessageBoxIcon.Information)
        End Sub

        ''' <summary>
        ''' Uma linha por entrada: "data | Usuario: x | Versao: y | evento". Linhas fora desse formato
        ''' (ex.: continuacao de excecao) vao inteiras para Evento. Nenhuma linha e descartada.
        ''' </summary>
        Private Shared Function ParseLine(line As String) As String()
            Dim parts = line.Split(New String() {" | "}, 4, StringSplitOptions.None)
            If parts.Length = 4 AndAlso parts(1).StartsWith("Usuario: ") AndAlso parts(2).StartsWith("Versao: ") Then
                Return {parts(0), parts(1).Substring("Usuario: ".Length), parts(2).Substring("Versao: ".Length), parts(3)}
            End If
            Return {String.Empty, String.Empty, String.Empty, line}
        End Function

        Private Sub OnRefreshClick(sender As Object, e As EventArgs)
            LoadLogs()
        End Sub

        Private Sub LoadLogs()
            Dim text = _logService.ReadAll()
            TextBoxLogs.Text = text
            _rawLines = text.Replace(vbCrLf, vbLf).Split(vbLf).Where(Function(l) l.Length > 0).ToList()
            _rows = _rawLines.Select(AddressOf ParseLine).ToList()
            _list.VirtualListSize = _rows.Count
            FitEventColumn()
            If _rows.Count > 0 Then
                Dim last = _rows.Count - 1
                _list.SelectedIndices.Clear()
                _list.SelectedIndices.Add(last)
                _list.EnsureVisible(last)
            End If
            _list.Invalidate()
        End Sub
    End Class
End Namespace


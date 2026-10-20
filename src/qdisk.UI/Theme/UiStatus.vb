' ─────────────────────────────────────────────────────────────────────────────
' qdisk  |  UI / Theme  |  UiStatus.vb
' Liga os rotulos de status existentes a uma StatusLine, sem mudar a logica que
' atribui os textos. A classificacao segue a secao 6.1 do HANDOFF.
'
' © Quallit — Licenciado sob GPL-3.0-or-later.
' ─────────────────────────────────────────────────────────────────────────────

Imports System.Drawing
Imports System.IO
Imports System.Windows.Forms

Namespace Theme
    Public Module UiStatus

        ''' <summary>Classifica um texto de status do app em erro, sucesso, andamento ou informacao.</summary>
        Public Function Classify(text As String) As StatusKind
            Dim t = If(text, String.Empty).Trim()
            If t.Length = 0 Then Return StatusKind.Info
            Dim lower = t.ToLowerInvariant()
            If lower.StartsWith("erro") OrElse lower.StartsWith("falha") OrElse lower.Contains("diverge") OrElse
               lower.Contains("insuficiente") OrElse lower.Contains("requer modo administrador") OrElse
               lower.StartsWith("modo administrador necessario") OrElse lower.StartsWith("ha imagens locais divergentes") Then
                Return StatusKind.Error
            End If
            If lower.EndsWith("...") OrElse lower.EndsWith("…") OrElse System.Text.RegularExpressions.Regex.IsMatch(t, "\.\.\. ?(\d+%|\(.*\))?$") Then
                Return StatusKind.Busy
            End If
            If lower.StartsWith("download concluido") OrElse lower.StartsWith("download concluído") OrElse
               lower.Contains("com sucesso") OrElse lower = "sha-256 valido." OrElse lower = "integridade valida." OrElse
               lower.StartsWith("tibx copiado") OrElse lower.StartsWith("usb zerado") OrElse lower.EndsWith("verificada.") Then
                Return StatusKind.Success
            End If
            Return StatusKind.Info
        End Function

        ''' <summary>
        ''' Esconde o Label original e mostra uma StatusLine na mesma celula. Toda atribuicao a
        ''' label.Text continua funcionando e passa a aparecer com icone e cor por tipo.
        ''' </summary>
        Public Function AttachStatusLine(label As Label, owner As Control) As StatusLine
            Dim line = New StatusLine() With {.Dock = DockStyle.Fill, .Margin = New Padding(0)}
            line.SetIcons(New Dictionary(Of StatusKind, Image) From {
                {StatusKind.Info, UiTheme.Icon16(owner, "info", "muted")},
                {StatusKind.Success, UiTheme.Icon16(owner, "okc", "green2")},
                {StatusKind.Error, UiTheme.Icon16(owner, "alertc", "warn")},
                {StatusKind.Busy, UiTheme.Icon16(owner, "spinner", "navy")}
            })
            Dim parent = label.Parent
            Dim table = TryCast(parent, TableLayoutPanel)
            If table IsNot Nothing Then
                Dim pos = table.GetCellPosition(label)
                table.Controls.Remove(label)
                table.Controls.Add(line, pos.Column, pos.Row)
            ElseIf parent IsNot Nothing Then
                Dim index = parent.Controls.GetChildIndex(label)
                parent.Controls.Remove(label)
                line.Dock = label.Dock
                parent.Controls.Add(line)
                parent.Controls.SetChildIndex(line, index)
            End If
            label.Visible = False
            line.SetStatus(label.Text, Classify(label.Text))
            AddHandler label.TextChanged, Sub() line.SetStatus(label.Text, Classify(label.Text))
            Return line
        End Function

        ''' <summary>Troca um controle por outro na mesma celula do TableLayoutPanel (ou posicao do pai).</summary>
        Public Sub ReplaceInParent(oldControl As Control, newControl As Control)
            Dim parent = oldControl.Parent
            Dim table = TryCast(parent, TableLayoutPanel)
            If table IsNot Nothing Then
                Dim pos = table.GetCellPosition(oldControl)
                table.Controls.Remove(oldControl)
                table.Controls.Add(newControl, pos.Column, pos.Row)
            ElseIf parent IsNot Nothing Then
                Dim index = parent.Controls.GetChildIndex(oldControl)
                parent.Controls.Remove(oldControl)
                parent.Controls.Add(newControl)
                parent.Controls.SetChildIndex(newControl, index)
            End If
        End Sub

        ''' <summary>Botao com largura pelo texto e altura fixa do tema.</summary>
        Public Sub StyleAutoButton(btn As Button, kind As ButtonKind, icon As Image, Optional minWidth As Integer = 0)
            UiTheme.StyleButton(btn, kind, icon)
            Dim textWidth = TextRenderer.MeasureText(btn.Text, btn.Font).Width
            btn.AutoSize = False
            btn.Width = Math.Max(minWidth, textWidth + 24 + If(icon Is Nothing, 0, 24))
        End Sub
    End Module
End Namespace

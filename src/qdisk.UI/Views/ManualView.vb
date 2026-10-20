' ─────────────────────────────────────────────────────────────────────────────
' qdisk  |  UI / Views  |  ManualView.vb
' View que exibe a documentação HTML embutida no executável.
'
' © Quallit — Licenciado sob GPL-3.0-or-later.
' ─────────────────────────────────────────────────────────────────────────────

Imports System.Drawing
Imports System.IO
Imports System.Windows.Forms
Imports qdisk.UI.Theme

Namespace Views
    Partial Public Class ManualView
        Inherits UserControl

        Public Sub New()
            InitializeComponent()
            ' Redesenho (HANDOFF 5.7): navegador dentro de painel com borda
            Dim inner As Panel = Nothing
            Dim box = UiTheme.CreateBorderedPanel(UiTheme.BorderLight, Color.White, inner)
            box.Dock = DockStyle.Fill
            box.Margin = New Padding(0)
            UiStatus.ReplaceInParent(ManualBrowser, box)
            inner.Controls.Add(ManualBrowser)
            LoadManualContent()
        End Sub

        Private Sub LoadManualContent()
            Dim manualPath = Path.Combine(AppPaths.AssetsDir, "manual", "manual.html")
            ManualBrowser.ScriptErrorsSuppressed = True

            If File.Exists(manualPath) Then
                ManualBrowser.Navigate(manualPath)
                Return
            End If

            ManualBrowser.DocumentText =
                "<html><body style='font-family:Segoe UI; color:#666; margin:16px;'>" &
                "<h3>Manual não encontrado</h3>" &
                "<p>Coloque o arquivo <b>manual.html</b> em <b>assets\manual</b> para exibir aqui.</p>" &
                "</body></html>"
        End Sub
    End Class
End Namespace


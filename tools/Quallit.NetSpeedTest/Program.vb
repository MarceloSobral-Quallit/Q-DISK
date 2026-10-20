' ─────────────────────────────────────────────────────────────────────────────
' Quallit NetSpeedTest  |  Program.vb
' Bancada do teste de velocidade: mede as origens de download (Dropbox e SharePoint)
' com 1, 4 e 8 conexoes, pelo mesmo caminho do download real.
'
' © Quallit — Licenciado sob GPL-3.0-or-later.
' ─────────────────────────────────────────────────────────────────────────────

Imports System.IO
Imports System.Windows.Forms

''' <summary>Caminhos usados pelo tema (UiTheme.LoadIcon): os assets ficam ao lado do executavel.</summary>
Friend Module AppPaths
    Public ReadOnly Property AssetsDir As String
        Get
            Return Path.Combine(AppContext.BaseDirectory, "assets")
        End Get
    End Property
End Module

Friend Module Program
    <STAThread>
    Friend Sub Main()
        Application.SetHighDpiMode(HighDpiMode.SystemAware)
        Application.EnableVisualStyles()
        Application.SetCompatibleTextRenderingDefault(False)
        Application.Run(New MainForm())
    End Sub
End Module

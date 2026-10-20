' ─────────────────────────────────────────────────────────────────────────────
' qdisk  |  Core / Services  |  AppConfigSource.vb
' Origem unica dos arquivos de configuracao (endpoints e tokens).
'
' © Quallit — Licenciado sob GPL-3.0-or-later.
' ─────────────────────────────────────────────────────────────────────────────

Imports System.IO
Imports System.Reflection
Imports System.Text

Namespace Services
    ''' <summary>
    ''' I-08 / T-01: em producao (Release), a configuracao vem so do recurso embutido no executavel.
    ''' Antes, valia a copia extraida em %LOCALAPPDATA% (editavel pelo usuario) ou qualquer pasta
    ''' config\ encontrada subindo a partir do executavel (ex.: C:\config\, criavel por usuario comum).
    ''' Em Debug, mantem a busca por arquivo para facilitar o desenvolvimento.
    ''' </summary>
    Friend NotInheritable Class AppConfigSource
        Private Const EmbeddedConfigPrefix As String = "_embedded\config\"

        Private Sub New()
        End Sub

        ''' <summary>Conteudo do arquivo de configuracao, ou Nothing se nao houver.</summary>
        Public Shared Function ReadText(fileName As String) As String
#If DEBUG Then
            Dim devPath = ResolveDevelopmentPath(fileName)
            If devPath IsNot Nothing Then
                Return File.ReadAllText(devPath)
            End If
#End If
            Return ReadEmbedded(fileName)
        End Function

        Private Shared Function ReadEmbedded(fileName As String) As String
            Dim entry = Assembly.GetEntryAssembly()
            If entry Is Nothing Then Return Nothing
            Using stream = entry.GetManifestResourceStream(EmbeddedConfigPrefix & fileName)
                If stream Is Nothing Then Return Nothing
                Using reader As New StreamReader(stream, Encoding.UTF8)
                    Return reader.ReadToEnd()
                End Using
            End Using
        End Function

#If DEBUG Then
        Private Shared Function ResolveDevelopmentPath(fileName As String) As String
            Dim localPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "qdisk",
                "config",
                fileName)
            If File.Exists(localPath) Then
                Return localPath
            End If

            Dim current = New DirectoryInfo(AppContext.BaseDirectory)
            While current IsNot Nothing
                Dim candidate = Path.Combine(current.FullName, "config", fileName)
                If File.Exists(candidate) Then
                    Return candidate
                End If
                current = current.Parent
            End While

            Return Nothing
        End Function
#End If
    End Class
End Namespace

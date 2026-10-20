' ─────────────────────────────────────────────────────────────────────────────
' qdisk  |  Core / Services  |  HashService.vb
' Cálculo assíncrono de hashes MD5 e SHA256 de arquivos locais.
'
' © Quallit — Licenciado sob GPL-3.0-or-later.
' ─────────────────────────────────────────────────────────────────────────────

Imports System.IO
Imports System.Security.Cryptography
Imports System.Text
Imports System.Threading
Imports System.Threading.Tasks

Namespace Services
    Public Class HashService

        Public Function ComputeMd5Async(filePath As String,
                                        cancellationToken As CancellationToken) As Task(Of String)
            Return ComputeInternalAsync(filePath, cancellationToken)
        End Function

        Private Async Function ComputeInternalAsync(filePath As String,
                                                    cancellationToken As CancellationToken) As Task(Of String)
            If String.IsNullOrWhiteSpace(filePath) OrElse Not File.Exists(filePath) Then
                Throw New FileNotFoundException("Arquivo nao encontrado.", filePath)
            End If

            Using stream As FileStream = File.OpenRead(filePath)
                Using md5 As MD5 = MD5.Create()
                    Dim hashBytes As Byte() = Await Task.Run(Of Byte())(Function() md5.ComputeHash(stream), cancellationToken)
                    Dim builder As New StringBuilder(hashBytes.Length * 2)
                    For Each b In hashBytes
                        builder.Append(b.ToString("x2"))
                    Next
                    Return builder.ToString()
                End Using
            End Using
        End Function
    End Class
End Namespace


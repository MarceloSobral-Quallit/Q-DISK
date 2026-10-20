' ─────────────────────────────────────────────────────────────────────────────
' qdisk  |  Core / Interfaces  |  IDownloadService.vb
' Interface do serviço de download para desacoplamento entre camadas.
'
' © Quallit — Licenciado sob GPL-3.0-or-later.
' ─────────────────────────────────────────────────────────────────────────────

Imports System.Threading
Imports System.Threading.Tasks

Namespace Interfaces
    Public Interface IDownloadService
        Function DownloadAsync(url As String,
                               destinationPath As String,
                               progress As IProgress(Of Integer),
                               cancellationToken As CancellationToken) As Task(Of String)
    End Interface
End Namespace


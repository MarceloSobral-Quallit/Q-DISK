' ─────────────────────────────────────────────────────────────────────────────
' qdisk  |  Core / Models  |  DownloadItem.vb
' Representa um item em download com URL, nome, progresso e estado.
'
' © Quallit — Licenciado sob GPL-3.0-or-later.
' ─────────────────────────────────────────────────────────────────────────────

Namespace Models
    Public Class DownloadItem
        Public Property Url As String
        Public Property FileName As String
        Public Property ExpectedMd5 As String
        Public Property SizeBytes As Long
    End Class
End Namespace


' ─────────────────────────────────────────────────────────────────────────────
' qdisk  |  Core / Services  |  DownloadOverwriteCheck.vb
' Antes de baixar uma imagem que ja existe na pasta: a local e a mesma do catalogo, o catalogo tem versao nova, ou o
' arquivo existe sem verificacao registrada. Compara o SHA-256 registrado no download com o do catalogo.
'
' © Quallit — Licenciado sob GPL-3.0-or-later.
' ─────────────────────────────────────────────────────────────────────────────

Namespace Services
    Public Enum DownloadOverwriteCase
        ''' <summary>Nao ha arquivo local: baixa sem perguntar.</summary>
        NotDownloaded
        ''' <summary>Imagem local verificada e com o mesmo SHA-256 do catalogo: baixar de novo e desnecessario.</summary>
        SameAsCatalog
        ''' <summary>Imagem local verificada, mas o catalogo publicou outra versao (SHA-256 diferente).</summary>
        NewVersionInCatalog
        ''' <summary>Arquivo existe, mas sem verificacao registrada (ex.: copiado a mao): nao da para saber se e o mesmo.</summary>
        UnverifiedFile
    End Enum

    Public NotInheritable Class DownloadOverwriteCheck
        Private Sub New()
        End Sub

        Public Shared Function Classify(fileExists As Boolean, downloadedAndVerified As Boolean, storedSha256 As String, catalogSha256 As String) As DownloadOverwriteCase
            If Not fileExists Then Return DownloadOverwriteCase.NotDownloaded
            Dim stored = If(storedSha256, String.Empty).Trim()
            Dim catalog = If(catalogSha256, String.Empty).Trim()
            If Not downloadedAndVerified OrElse stored.Length = 0 Then Return DownloadOverwriteCase.UnverifiedFile
            ' Catalogo sem SHA-256 (so MD5/tamanho): vale a verificacao registrada.
            If catalog.Length = 0 OrElse String.Equals(stored, catalog, StringComparison.OrdinalIgnoreCase) Then Return DownloadOverwriteCase.SameAsCatalog
            Return DownloadOverwriteCase.NewVersionInCatalog
        End Function
    End Class
End Namespace

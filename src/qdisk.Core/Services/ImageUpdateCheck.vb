' ─────────────────────────────────────────────────────────────────────────────
' qdisk  |  Core / Services  |  ImageUpdateCheck.vb
' "Verificar atualizacoes de imagens na inicializacao": imagens ja baixadas cujo SHA-256 registrado no download difere
' do SHA-256 atual do catalogo (o catalogo publicou outra versao do arquivo). So compara hashes ja registrados; nao le
' as imagens (varios GB).
'
' © Quallit — Licenciado sob GPL-3.0-or-later.
' ─────────────────────────────────────────────────────────────────────────────
Imports System.IO
Imports qdisk.Core.Models

Namespace Services
    ''' <summary>Imagem baixada, como registrada em download_status.json.</summary>
    Public NotInheritable Class LocalImageRecord
        Public Property CatalogName As String
        Public Property FilePath As String
        Public Property ImageSha256 As String
        Public Property Downloaded As Boolean
    End Class

    Public NotInheritable Class OutdatedImage
        Public Property CatalogName As String
        Public Property FileName As String
        Public Property LocalSha256 As String
        Public Property CatalogSha256 As String
    End Class

    Public NotInheritable Class ImageUpdateCheck
        Private Sub New()
        End Sub

        ''' <summary>
        ''' Itens do catalogo com imagem local registrada (baixada, arquivo presente, SHA-256 conhecido) cujo SHA-256
        ''' difere do catalogo. O registro vem pelo nome do item ou, sem ele, pelo nome do arquivo (pares
        ''' -sharepoint/-dropbox dividem o arquivo). Se algum registro do mesmo arquivo ja tem o SHA-256 do catalogo, a
        ''' imagem esta em dia. Cada arquivo aparece uma vez.
        ''' </summary>
        Public Shared Function FindOutdated(catalog As IEnumerable(Of CatalogItem), records As IEnumerable(Of LocalImageRecord),
                                            fileExists As Func(Of String, Boolean)) As IReadOnlyList(Of OutdatedImage)
            Dim usable = If(records, Enumerable.Empty(Of LocalImageRecord)()).
                Where(Function(record) record IsNot Nothing AndAlso record.Downloaded AndAlso
                                       Not String.IsNullOrWhiteSpace(record.FilePath) AndAlso
                                       Not String.IsNullOrWhiteSpace(record.ImageSha256) AndAlso
                                       fileExists(record.FilePath)).
                ToList()
            Dim result As New List(Of OutdatedImage)()
            Dim reportedFiles As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)

            For Each item In If(catalog, Enumerable.Empty(Of CatalogItem)())
                If item Is Nothing OrElse String.IsNullOrWhiteSpace(item.Sha256) OrElse String.IsNullOrWhiteSpace(item.FileName) Then Continue For
                Dim record = usable.FirstOrDefault(Function(entry) String.Equals(entry.CatalogName, item.Name, StringComparison.OrdinalIgnoreCase))
                If record Is Nothing Then
                    record = usable.FirstOrDefault(Function(entry) String.Equals(Path.GetFileName(entry.FilePath), item.FileName, StringComparison.OrdinalIgnoreCase))
                End If
                If record Is Nothing Then Continue For

                Dim samePath = usable.Where(Function(entry) String.Equals(entry.FilePath, record.FilePath, StringComparison.OrdinalIgnoreCase))
                If samePath.Any(Function(entry) String.Equals(entry.ImageSha256.Trim(), item.Sha256.Trim(), StringComparison.OrdinalIgnoreCase)) Then Continue For
                If Not reportedFiles.Add(record.FilePath) Then Continue For

                result.Add(New OutdatedImage With {
                    .CatalogName = item.Name,
                    .FileName = item.FileName,
                    .LocalSha256 = record.ImageSha256.Trim().ToLowerInvariant(),
                    .CatalogSha256 = item.Sha256.Trim().ToLowerInvariant()
                })
            Next
            Return result
        End Function
    End Class
End Namespace

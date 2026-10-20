' ─────────────────────────────────────────────────────────────────────────────
' qdisk  |  UI / Services  |  ImageUpdateNotifier.vb
' "Verificar atualizacoes de imagens na inicializacao" (Configuracoes): na abertura, atualiza os catalogos do perfil e
' de ferramentas e avisa quais imagens ja baixadas tem versao nova no catalogo (regra em ImageUpdateCheck, no Core).
'
' © Quallit — Licenciado sob GPL-3.0-or-later.
' ─────────────────────────────────────────────────────────────────────────────
Imports System.IO
Imports System.Text.Json
Imports System.Threading
Imports System.Threading.Tasks
Imports qdisk.Core
Imports qdisk.Core.Services

Namespace Services
    Friend Module ImageUpdateNotifier
        ''' <summary>Imagens baixadas com versao nova no catalogo (lista vazia se nada mudou ou se nao foi possivel verificar).</summary>
        Public Async Function FindOutdatedImagesAsync(cancellationToken As CancellationToken) As Task(Of IReadOnlyList(Of OutdatedImage))
            Dim logService As New LogService()
            Dim catalogService As New CatalogService()
            Dim dataDir = AppPaths.DataDir
            Dim profile = TokenManager.GetUserProfile()
            Dim profilePath = Path.Combine(dataDir, CatalogService.GetProfileCatalogFileName(profile))
            Dim toolsPath = Path.Combine(dataDir, CatalogService.ToolsCatalogFileName)

            Await logService.WriteAsync("IMAGES | STARTUP_CHECK | INICIO")
            Try
                Await catalogService.RefreshCatalogsFromRemoteDetailedAsync(
                    cancellationToken,
                    forceRefresh:=False,
                    New CatalogService.CatalogSource(CatalogService.GetProfileRemoteCatalogUrl(profile), profilePath, CatalogService.GetProfileFallbackCatalogUrl(profile)),
                    New CatalogService.CatalogSource(CatalogService.RemoteToolsCatalogUrl, toolsPath, CatalogService.FallbackToolsCatalogUrl))
            Catch ex As Exception When Not TypeOf ex Is OperationCanceledException
                ' Sem rede: compara com o catalogo local mesmo.
                logService.WriteAsync($"IMAGES | STARTUP_CHECK | CATALOGO_LOCAL | {ex.Message}").GetAwaiter().GetResult()
            End Try

            Dim catalog = catalogService.LoadAndMerge(profilePath, toolsPath)
            Dim outdated = ImageUpdateCheck.FindOutdated(catalog, LoadLocalImageRecords(), AddressOf File.Exists)
            Await logService.WriteAsync($"IMAGES | STARTUP_CHECK | RESULTADO | Itens={catalog.Count} | VersaoNova={outdated.Count}" &
                                        If(outdated.Count > 0, " | " & String.Join(", ", outdated.Select(Function(image) image.FileName)), String.Empty))
            Return outdated
        End Function

        ''' <summary>download_status.json: nome do item do catalogo -> arquivo baixado e SHA-256 registrado.</summary>
        Private Function LoadLocalImageRecords() As IReadOnlyList(Of LocalImageRecord)
            Dim records As New List(Of LocalImageRecord)()
            Dim statusPath = AppPaths.DownloadStatusPath
            If Not File.Exists(statusPath) Then Return records
            Try
                Using document = JsonDocument.Parse(File.ReadAllText(statusPath))
                    If document.RootElement.ValueKind <> JsonValueKind.Object Then Return records
                    For Each entry In document.RootElement.EnumerateObject()
                        If entry.Value.ValueKind <> JsonValueKind.Object Then Continue For
                        records.Add(New LocalImageRecord With {
                            .CatalogName = entry.Name,
                            .FilePath = ReadString(entry.Value, "Path"),
                            .ImageSha256 = ReadString(entry.Value, "ImageSha256"),
                            .Downloaded = ReadBoolean(entry.Value, "Downloaded")
                        })
                    Next
                End Using
            Catch ex As Exception
                Dim logService As New LogService()
                logService.WriteAsync($"IMAGES | STARTUP_CHECK | STATUS_ILEGIVEL | {ex.Message}").GetAwaiter().GetResult()
            End Try
            Return records
        End Function

        Private Function FindProperty(element As JsonElement, name As String) As JsonElement?
            For Each prop In element.EnumerateObject()
                If String.Equals(prop.Name, name, StringComparison.OrdinalIgnoreCase) Then Return prop.Value
            Next
            Return Nothing
        End Function

        Private Function ReadString(element As JsonElement, name As String) As String
            Dim value = FindProperty(element, name)
            Return If(value.HasValue AndAlso value.Value.ValueKind = JsonValueKind.String, value.Value.GetString(), Nothing)
        End Function

        Private Function ReadBoolean(element As JsonElement, name As String) As Boolean
            Dim value = FindProperty(element, name)
            Return value.HasValue AndAlso value.Value.ValueKind = JsonValueKind.True
        End Function
    End Module
End Namespace

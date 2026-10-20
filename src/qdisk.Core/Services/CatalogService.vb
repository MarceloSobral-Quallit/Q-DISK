' ─────────────────────────────────────────────────────────────────────────────
' qdisk  |  Core / Services  |  CatalogService.vb
' Carrega e mescla os catálogos JSON de imagens ISO por perfil de acesso.
'
' © Quallit — Licenciado sob GPL-3.0-or-later.
' ─────────────────────────────────────────────────────────────────────────────

Imports System.IO
Imports System.Net.Http
Imports System.Text
Imports System.Text.Json
Imports System.Text.RegularExpressions
Imports System.Threading
Imports System.Threading.Tasks
Imports qdisk.Core.Models

Namespace Services
    Public Class CatalogService
        Public Const PnbCatalogFileName As String = "qdisk.quallit.pnb.json"
        Public Const ToolsCatalogFileName As String = "qdisk.quallit.tools.json"
        Public Const RdCatalogFileName As String = "qdisk.quallit.rd.json"
        Public Const DevTeamCatalogFileName As String = "qdisk.quallit.devteam.json"

        Public Shared ReadOnly Property PrimaryBaseUrl As String
            Get
                Return EndpointConfig.Current.PrimaryBaseUrl
            End Get
        End Property

        Public Shared ReadOnly Property FallbackBaseUrl As String
            Get
                Return EndpointConfig.Current.FallbackPublicBaseUrl
            End Get
        End Property

        Public Shared ReadOnly Property RemotePnbCatalogUrl As String
            Get
                Return PrimaryBaseUrl & PnbCatalogFileName
            End Get
        End Property

        Public Shared ReadOnly Property RemoteToolsCatalogUrl As String
            Get
                Return PrimaryBaseUrl & ToolsCatalogFileName
            End Get
        End Property

        Public Shared ReadOnly Property RemoteRdCatalogUrl As String
            Get
                Return PrimaryBaseUrl & RdCatalogFileName
            End Get
        End Property

        Public Shared ReadOnly Property RemoteDevTeamCatalogUrl As String
            Get
                Return PrimaryBaseUrl & DevTeamCatalogFileName
            End Get
        End Property

        Public Shared ReadOnly Property RemoteUpdateCatalogUrl As String
            Get
                Return PrimaryBaseUrl & "qdisk.quallit.update.json"
            End Get
        End Property

        Public Shared ReadOnly Property RemoteInstallerUrl As String
            Get
                Return PrimaryBaseUrl & "Setup_qdisk.update.exe"
            End Get
        End Property

        Public Shared ReadOnly Property FallbackPnbCatalogUrl As String
            Get
                Return FallbackBaseUrl & PnbCatalogFileName
            End Get
        End Property

        Public Shared ReadOnly Property FallbackToolsCatalogUrl As String
            Get
                Return FallbackBaseUrl & ToolsCatalogFileName
            End Get
        End Property

        Public Shared ReadOnly Property FallbackRdCatalogUrl As String
            Get
                Return FallbackBaseUrl & RdCatalogFileName
            End Get
        End Property

        Public Shared ReadOnly Property FallbackDevTeamCatalogUrl As String
            Get
                Return FallbackBaseUrl & DevTeamCatalogFileName
            End Get
        End Property

        Public Shared Function GetProfileCatalogFileName(profile As UserProfile) As String
            Return TokenConfig.Current.GetProfileConfig(profile).CatalogFileName
        End Function

        Public Shared Function GetProfileRemoteCatalogUrl(profile As UserProfile) As String
            Return PrimaryBaseUrl & GetProfileCatalogFileName(profile)
        End Function

        Public Shared Function GetProfileFallbackCatalogUrl(profile As UserProfile) As String
            Return FallbackBaseUrl & GetProfileCatalogFileName(profile)
        End Function

        Private Shared ReadOnly _httpClient As HttpClient = CreateHttpClient()

        Private Shared Function CreateHttpClient() As HttpClient
            Dim client As New HttpClient() With {.Timeout = TimeSpan.FromSeconds(20)}
            HttpIdentity.Apply(client.DefaultRequestHeaders, "qdisk/1.0")
            Return client
        End Function

        Public Function LoadFromJson(path As String, Optional includeDisabled As Boolean = False) As IReadOnlyList(Of CatalogItem)
            If String.IsNullOrWhiteSpace(path) OrElse Not File.Exists(path) Then
                Return Array.Empty(Of CatalogItem)()
            End If

            Try
                Dim json = File.ReadAllText(path)
                Return ParseAndValidateCatalog(json, path, includeDisabled)
            Catch
                Return Array.Empty(Of CatalogItem)()
            End Try
        End Function

        Public Async Function RefreshFromRemoteDetailedAsync(remoteUrl As String, localPath As String, cancellationToken As CancellationToken, Optional forceRefresh As Boolean = False) As Task(Of CatalogRefreshResult)
            Dim result As New CatalogRefreshResult(remoteUrl, localPath)
            If String.IsNullOrWhiteSpace(remoteUrl) OrElse String.IsNullOrWhiteSpace(localPath) Then
                result.ErrorMessage = "URL remota ou caminho local nao informado."
                Return result
            End If

            Try
                Dim requestUrl = If(forceRefresh, AddCacheBust(remoteUrl), remoteUrl)
                Dim json = Await GetStringWithRedirectAsync(requestUrl, 0, cancellationToken)
                Dim items = ParseAndValidateCatalog(json, remoteUrl, includeDisabled:=True)

                result.Success = True
                result.ItemCount = items.Count

                If Not forceRefresh AndAlso CatalogMatchesLocalFile(items, localPath) Then
                    result.Changed = False
                    Return result
                End If

                Directory.CreateDirectory(Path.GetDirectoryName(localPath))
                Dim tempPath = localPath & ".tmp"
                Await File.WriteAllTextAsync(tempPath, json, cancellationToken)

                If File.Exists(localPath) Then
                    File.Replace(tempPath, localPath, Nothing)
                Else
                    File.Move(tempPath, localPath)
                End If

                result.Changed = True
            Catch ex As Exception
                result.ErrorMessage = ex.Message
            End Try

            Return result
        End Function

        Private Shared Function CatalogMatchesLocalFile(remoteItems As List(Of CatalogItem), localPath As String) As Boolean
            If remoteItems Is Nothing OrElse String.IsNullOrWhiteSpace(localPath) OrElse Not File.Exists(localPath) Then
                Return False
            End If

            Try
                Dim localJson = File.ReadAllText(localPath)
                Dim localItems = ParseAndValidateCatalog(localJson, localPath, includeDisabled:=True)

                Return NormalizeCatalogItems(remoteItems) = NormalizeCatalogItems(localItems)
            Catch
                Return False
            End Try
        End Function

        Private Shared Function NormalizeCatalogItems(items As IEnumerable(Of CatalogItem)) As String
            Dim builder As New StringBuilder()
            For Each item In items
                If item Is Nothing Then
                    builder.AppendLine("<null>")
                    Continue For
                End If

                builder.Append(NormalizeCatalogValue(item.Name)).Append("|")
                builder.Append(item.Enabled.ToString(Globalization.CultureInfo.InvariantCulture)).Append("|")
                builder.Append(NormalizeCatalogValue(item.Group)).Append("|")
                builder.Append(NormalizeCatalogValue(item.FileUrl)).Append("|")
                builder.Append(NormalizeCatalogValue(item.FileName)).Append("|")
                builder.Append(NormalizeCatalogValue(item.Md5).ToLowerInvariant()).Append("|")
                builder.Append(NormalizeCatalogValue(item.Md5Url)).Append("|")
                builder.Append(NormalizeCatalogValue(item.Sha256).ToLowerInvariant()).Append("|")
                builder.Append(NormalizeCatalogValue(item.Sha256Url)).Append("|")
                builder.Append(item.ImageSizeBytes.ToString(Globalization.CultureInfo.InvariantCulture)).Append("|")
                builder.Append(NormalizeUsbIdentification(item.UsbIdentification)).Append("|")
                builder.Append(NormalizeUsbWritePolicy(item.UsbWritePolicy)).Append("|")
                builder.Append(NormalizeCatalogValue(item.Description)).AppendLine()
            Next
            Return builder.ToString()
        End Function

        Private Shared Function NormalizeUsbWritePolicy(value As UsbWritePolicyInfo) As String
            If value Is Nothing Then
                Return String.Empty
            End If

            Dim options = New JsonSerializerOptions() With {
                .PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            }
            Return JsonSerializer.Serialize(value, options)
        End Function

        Private Shared Function NormalizeUsbIdentification(value As UsbIdentificationInfo) As String
            If value Is Nothing Then
                Return String.Empty
            End If

            Dim options = New JsonSerializerOptions() With {
                .PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            }
            Return JsonSerializer.Serialize(value, options)
        End Function

        Private Shared Function NormalizeCatalogValue(value As String) As String
            Return If(value, String.Empty).Trim()
        End Function

        Private Shared Function ParseAndValidateCatalog(json As String, source As String, Optional includeDisabled As Boolean = False) As List(Of CatalogItem)
            If String.IsNullOrWhiteSpace(json) Then
                Throw New InvalidDataException($"Catalogo vazio: {source}")
            End If

            Try
                Dim itemsJson As String = json
                Using document = JsonDocument.Parse(json)
                    If document.RootElement.ValueKind = JsonValueKind.Object Then
                        Dim itemsElement As JsonElement
                        If Not document.RootElement.TryGetProperty("items", itemsElement) OrElse itemsElement.ValueKind <> JsonValueKind.Array Then
                            Throw New InvalidDataException($"Catalogo invalido em {source}: raiz JSON deve conter items como array.")
                        End If
                        itemsJson = itemsElement.GetRawText()
                    ElseIf document.RootElement.ValueKind <> JsonValueKind.Array Then
                        Throw New InvalidDataException($"Catalogo invalido em {source}: raiz JSON deve ser um array ou objeto com items.")
                    End If
                End Using

                Dim options = New JsonSerializerOptions() With {
                    .PropertyNameCaseInsensitive = True
                }
                Dim items = JsonSerializer.Deserialize(Of List(Of CatalogItem))(itemsJson, options)
                ValidateCatalogItems(items, source)
                If includeDisabled Then
                    Return If(items, New List(Of CatalogItem)())
                End If
                Return FilterEnabledCatalogItems(items)
            Catch ex As JsonException
                Throw New InvalidDataException($"Catalogo invalido em {source}: JSON malformado. {ex.Message}", ex)
            End Try
        End Function

        Private Shared Function FilterEnabledCatalogItems(items As List(Of CatalogItem)) As List(Of CatalogItem)
            Dim enabledItems As New List(Of CatalogItem)()
            If items Is Nothing Then
                Return enabledItems
            End If

            For Each item In items
                If item IsNot Nothing AndAlso item.Enabled Then
                    enabledItems.Add(item)
                End If
            Next

            Return enabledItems
        End Function

        Private Shared Async Function GetStringWithRedirectAsync(url As String, depth As Integer, cancellationToken As CancellationToken) As Task(Of String)
            If depth > 3 Then
                Throw New InvalidOperationException("Numero maximo de redirecionamentos excedido.")
            End If

            Using response = Await _httpClient.GetAsync(url, cancellationToken)
                response.EnsureSuccessStatusCode()
                Dim content = Await response.Content.ReadAsStringAsync(cancellationToken)
                Dim mediaType = response.Content.Headers.ContentType?.MediaType

                If Not String.IsNullOrWhiteSpace(mediaType) AndAlso mediaType.StartsWith("text/html", StringComparison.OrdinalIgnoreCase) Then
                    Dim redirectedUrl = TryExtractRedirectUrl(content)
                    If Not String.IsNullOrWhiteSpace(redirectedUrl) Then
                        Return Await GetStringWithRedirectAsync(redirectedUrl, depth + 1, cancellationToken)
                    End If
                End If

                Return content
            End Using
        End Function

        Private Shared Function TryExtractRedirectUrl(html As String) As String
            If String.IsNullOrWhiteSpace(html) Then
                Return String.Empty
            End If

            Dim patterns = New String() {
                "redirect\(['""\s]*(?<url>https?:\\/\\/[^'"")]+)['""]\)",
                "redirect\(['""\s]*(?<url>https?://[^'"")]+)['""]\)",
                "window\.location\.replace\(['""](?<url>[^'""]+)['""]\)",
                "http-equiv=['""]refresh['""].*?url=(?<url>https?://[^'""<>\s]+)",
                "href=['""](?<url>https?://[^'""<>]+\.json[^'""<>]*)['""]"
            }

            For Each pattern In patterns
                Dim match = Regex.Match(html, pattern, RegexOptions.IgnoreCase)
                If match.Success Then
                    Dim value = match.Groups("url").Value
                    Dim normalized = NormalizeRedirectUrl(value)
                    If Not String.IsNullOrWhiteSpace(normalized) Then
                        Return normalized
                    End If
                End If
            Next

            Return String.Empty
        End Function

        Private Shared Function NormalizeRedirectUrl(value As String) As String
            If String.IsNullOrWhiteSpace(value) Then
                Return String.Empty
            End If

            Dim normalized = value.Trim().Replace("\/", "/")
            normalized = Regex.Replace(normalized, "\\u(?<hex>[0-9a-fA-F]{4})", Function(m) ChrW(Convert.ToInt32(m.Groups("hex").Value, 16)))
            ' Redirecionamento so e seguido para HTTPS.
            Return If(UrlPolicy.IsHttps(normalized), normalized, String.Empty)
        End Function

        Private Shared Sub ValidateCatalogItems(items As List(Of CatalogItem), source As String)
            If items Is Nothing Then
                Throw New InvalidDataException($"Catalogo remoto vazio ou invalido: {source}")
            End If

            If items.Count = 0 Then
                If source.EndsWith(DevTeamCatalogFileName, StringComparison.OrdinalIgnoreCase) OrElse source.IndexOf("devteam", StringComparison.OrdinalIgnoreCase) >= 0 Then
                    Return
                End If

                Throw New InvalidDataException($"Catalogo remoto sem itens: {source}")
            End If

            Dim index = 0
            For Each item In items
                index += 1
                If item Is Nothing Then
                    Throw New InvalidDataException($"Catalogo invalido em {source}: item {index} vazio.")
                End If

                If String.IsNullOrWhiteSpace(item.Name) Then
                    Throw New InvalidDataException($"Catalogo invalido em {source}: item {index} sem name.")
                End If

                If String.IsNullOrWhiteSpace(item.Group) Then
                    Throw New InvalidDataException($"Catalogo invalido em {source}: item {item.Name} sem group.")
                End If

                If String.IsNullOrWhiteSpace(item.FileUrl) Then
                    Throw New InvalidDataException($"Catalogo invalido em {source}: item {item.Name} sem fileUrl.")
                End If

                Dim fileUri As Uri = Nothing
                If Not Uri.TryCreate(item.FileUrl, UriKind.Absolute, fileUri) OrElse fileUri.Scheme <> Uri.UriSchemeHttps Then
                    Throw New InvalidDataException($"Catalogo invalido em {source}: item {item.Name} com fileUrl invalida (exige HTTPS).")
                End If

                If Not String.IsNullOrWhiteSpace(item.Md5Url) AndAlso Not UrlPolicy.IsHttps(item.Md5Url) Then
                    Throw New InvalidDataException($"Catalogo invalido em {source}: item {item.Name} com md5Url invalida (exige HTTPS).")
                End If

                If Not String.IsNullOrWhiteSpace(item.Sha256Url) AndAlso Not UrlPolicy.IsHttps(item.Sha256Url) Then
                    Throw New InvalidDataException($"Catalogo invalido em {source}: item {item.Name} com sha256Url invalida (exige HTTPS).")
                End If

                If Not String.IsNullOrWhiteSpace(item.Md5) AndAlso Not Regex.IsMatch(item.Md5.Trim(), "\A[a-fA-F0-9]{32}\z") Then
                    Throw New InvalidDataException($"Catalogo invalido em {source}: item {item.Name} com md5 invalido.")
                End If

                If Not String.IsNullOrWhiteSpace(item.FileName) AndAlso Not IsValidCatalogFileName(item.FileName) Then
                    Throw New InvalidDataException($"Catalogo invalido em {source}: item {item.Name} com fileName invalido.")
                End If

                If Not String.IsNullOrWhiteSpace(item.Sha256) AndAlso Not Regex.IsMatch(item.Sha256.Trim(), "\A[a-fA-F0-9]{64}\z") Then
                    Throw New InvalidDataException($"Catalogo invalido em {source}: item {item.Name} com sha256 invalido.")
                End If

                If Not String.IsNullOrWhiteSpace(item.Sha256) AndAlso item.ImageSizeBytes <= 0 Then
                    Throw New InvalidDataException($"Catalogo invalido em {source}: item {item.Name} com imageSizeBytes ausente.")
                End If

                ValidateUsbIdentification(item, source)
            Next
        End Sub

        Private Shared Function IsValidCatalogFileName(value As String) As Boolean
            If String.IsNullOrWhiteSpace(value) Then
                Return False
            End If

            Dim trimmed = value.Trim()
            If trimmed.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 Then
                Return False
            End If

            Dim extension = Path.GetExtension(trimmed)
            Return String.Equals(extension, ".tibx", StringComparison.OrdinalIgnoreCase) OrElse
                   String.Equals(extension, ".iso", StringComparison.OrdinalIgnoreCase) OrElse
                   String.Equals(extension, ".img", StringComparison.OrdinalIgnoreCase)
        End Function

        Private Shared Sub ValidateUsbIdentification(item As CatalogItem, source As String)
            If item.UsbIdentification Is Nothing Then
                Return
            End If

            Dim modes = item.UsbIdentification.EffectiveModes
            If modes Is Nothing OrElse modes.Count = 0 Then
                Throw New InvalidDataException($"Catalogo invalido em {source}: item {item.Name} com usbIdentification sem mode/modes.")
            End If

            Dim index = 0
            For Each mode In modes
                index += 1
                Dim label = $"item {item.Name} usbIdentification[{index}]"
                Dim schema = If(mode.Schema.HasValue, mode.Schema, item.UsbIdentification.Schema)
                If schema.HasValue AndAlso schema.Value <= 0 Then
                    Throw New InvalidDataException($"Catalogo invalido em {source}: {label} com schema invalido.")
                End If

                Dim modeName = NormalizeCatalogValue(mode.Mode).ToLowerInvariant()
                Select Case modeName
                    Case "logical"
                        ValidateLogicalUsbIdentification(mode, source, label)
                    Case "raw"
                        ValidateRawUsbIdentification(mode, item, source, label)
                    Case Else
                        Throw New InvalidDataException($"Catalogo invalido em {source}: {label} com mode invalido.")
                End Select
            Next
        End Sub

        Private Shared Sub ValidateLogicalUsbIdentification(mode As UsbIdentificationMode, source As String, label As String)
            If Not IsSha256(mode.TreeHash) Then
                Throw New InvalidDataException($"Catalogo invalido em {source}: {label} com treeHash invalido.")
            End If

            If String.IsNullOrWhiteSpace(mode.ManifestUrl) Then
                Throw New InvalidDataException($"Catalogo invalido em {source}: {label} sem manifestUrl.")
            End If

            If mode.FileCount.GetValueOrDefault() <= 0 Then
                Throw New InvalidDataException($"Catalogo invalido em {source}: {label} com fileCount invalido.")
            End If

            If mode.TotalBytes.GetValueOrDefault() <= 0 Then
                Throw New InvalidDataException($"Catalogo invalido em {source}: {label} com totalBytes invalido.")
            End If

            If Not String.IsNullOrWhiteSpace(mode.ManifestSha256) AndAlso Not IsSha256(mode.ManifestSha256) Then
                Throw New InvalidDataException($"Catalogo invalido em {source}: {label} com manifestSha256 invalido.")
            End If

            If mode.SentinelFiles Is Nothing Then
                Return
            End If

            Dim sentinelIndex = 0
            For Each sentinel In mode.SentinelFiles
                sentinelIndex += 1
                If sentinel Is Nothing Then
                    Throw New InvalidDataException($"Catalogo invalido em {source}: {label} sentinelFiles[{sentinelIndex}] vazio.")
                End If

                If String.IsNullOrWhiteSpace(sentinel.Path) Then
                    Throw New InvalidDataException($"Catalogo invalido em {source}: {label} sentinelFiles[{sentinelIndex}] sem path.")
                End If

                If sentinel.Size.GetValueOrDefault(-1) < 0 Then
                    Throw New InvalidDataException($"Catalogo invalido em {source}: {label} sentinelFiles[{sentinelIndex}] com size invalido.")
                End If

                If Not IsSha256(sentinel.Sha256) Then
                    Throw New InvalidDataException($"Catalogo invalido em {source}: {label} sentinelFiles[{sentinelIndex}] com sha256 invalido.")
                End If
            Next
        End Sub

        Private Shared Sub ValidateRawUsbIdentification(mode As UsbIdentificationMode, item As CatalogItem, source As String, label As String)
            If Not IsSha256(mode.ImageSha256) Then
                Throw New InvalidDataException($"Catalogo invalido em {source}: {label} com imageSha256 invalido.")
            End If

            If mode.SizeBytes.GetValueOrDefault() <= 0 Then
                Throw New InvalidDataException($"Catalogo invalido em {source}: {label} com sizeBytes invalido.")
            End If

            If Not String.IsNullOrWhiteSpace(item.Sha256) AndAlso Not String.Equals(mode.ImageSha256.Trim(), item.Sha256.Trim(), StringComparison.OrdinalIgnoreCase) Then
                Throw New InvalidDataException($"Catalogo invalido em {source}: {label} imageSha256 diferente do sha256 do item.")
            End If

            If item.ImageSizeBytes > 0 AndAlso mode.SizeBytes.HasValue AndAlso mode.SizeBytes.Value <> item.ImageSizeBytes Then
                Throw New InvalidDataException($"Catalogo invalido em {source}: {label} sizeBytes diferente do imageSizeBytes do item.")
            End If
        End Sub

        Private Shared Function IsSha256(value As String) As Boolean
            Return Not String.IsNullOrWhiteSpace(value) AndAlso Regex.IsMatch(value.Trim(), "\A[a-fA-F0-9]{64}\z")
        End Function

        Public Async Function RefreshCatalogsFromRemoteDetailedAsync(cancellationToken As CancellationToken, ParamArray sources As CatalogSource()) As Task(Of IReadOnlyList(Of CatalogRefreshResult))
            Return Await RefreshCatalogsFromRemoteDetailedAsync(cancellationToken, False, sources)
        End Function

        Public Async Function RefreshCatalogsFromRemoteDetailedAsync(cancellationToken As CancellationToken, forceRefresh As Boolean, ParamArray sources As CatalogSource()) As Task(Of IReadOnlyList(Of CatalogRefreshResult))
            Dim results As New List(Of CatalogRefreshResult)()
            For Each source In sources
                If source Is Nothing Then Continue For
                results.Add(Await RefreshFromAnyRemoteDetailedAsync(source, cancellationToken, forceRefresh))
            Next
            Return results
        End Function

        Private Async Function RefreshFromAnyRemoteDetailedAsync(source As CatalogSource, cancellationToken As CancellationToken, forceRefresh As Boolean) As Task(Of CatalogRefreshResult)
            Dim errors As New List(Of String)()
            Dim lastResult As CatalogRefreshResult = Nothing

            For Each remoteUrl In source.GetRemoteUrls()
                Dim result = Await RefreshFromRemoteDetailedAsync(remoteUrl, source.LocalPath, cancellationToken, forceRefresh)
                If result.Success Then
                    Return result
                End If

                lastResult = result
                errors.Add($"{remoteUrl}: {result.ErrorMessage}")
            Next

            If lastResult Is Nothing Then
                lastResult = New CatalogRefreshResult(source.RemoteUrl, source.LocalPath)
            End If
            lastResult.ErrorMessage = String.Join(" | ", errors)
            Return lastResult
        End Function

        Private Shared Function AddCacheBust(url As String) As String
            If String.IsNullOrWhiteSpace(url) Then
                Return url
            End If

            Dim separator = If(url.Contains("?"), "&", "?")
            Return url & separator & "refresh=" & Uri.EscapeDataString(Guid.NewGuid().ToString("N"))
        End Function

        ''' <summary>
        ''' Carrega e mescla múltiplos arquivos de catálogo em uma única lista.
        ''' Arquivos inexistentes são silenciosamente ignorados.
        ''' </summary>
        Public Function LoadAndMerge(ParamArray paths As String()) As IReadOnlyList(Of CatalogItem)
            Dim merged As New List(Of CatalogItem)()
            For Each p In paths
                merged.AddRange(LoadFromJson(p))
            Next
            Return merged
        End Function

        Public Function LoadAllCatalogs(dataDir As String) As IReadOnlyList(Of CatalogItem)
            Return LoadAndMerge(
                Path.Combine(dataDir, RdCatalogFileName),
                Path.Combine(dataDir, PnbCatalogFileName),
                Path.Combine(dataDir, DevTeamCatalogFileName),
                Path.Combine(dataDir, ToolsCatalogFileName))
        End Function

        Public Class CatalogSource
            Public Sub New(remoteUrl As String, localPath As String, ParamArray fallbackRemoteUrls As String())
                Me.RemoteUrl = remoteUrl
                Me.LocalPath = localPath
                Me.FallbackRemoteUrls = If(fallbackRemoteUrls, Array.Empty(Of String)()).ToList()
            End Sub

            Public Property RemoteUrl As String
            Public Property LocalPath As String
            Public Property FallbackRemoteUrls As List(Of String)

            Public Function GetRemoteUrls() As IReadOnlyList(Of String)
                Dim urls As New List(Of String)()
                If Not String.IsNullOrWhiteSpace(RemoteUrl) Then
                    urls.Add(RemoteUrl)
                End If

                If FallbackRemoteUrls IsNot Nothing Then
                    For Each fallback In FallbackRemoteUrls
                        If Not String.IsNullOrWhiteSpace(fallback) AndAlso Not urls.Any(Function(url) url.Equals(fallback, StringComparison.OrdinalIgnoreCase)) Then
                            urls.Add(fallback)
                        End If
                    Next
                End If

                Return urls
            End Function
        End Class

        Public Class CatalogRefreshResult
            Public Sub New(remoteUrl As String, localPath As String)
                Me.RemoteUrl = remoteUrl
                Me.LocalPath = localPath
            End Sub

            Public Property RemoteUrl As String
            Public Property LocalPath As String
            Public Property Success As Boolean
            Public Property Changed As Boolean
            Public Property ItemCount As Integer
            Public Property ErrorMessage As String
        End Class
    End Class
End Namespace


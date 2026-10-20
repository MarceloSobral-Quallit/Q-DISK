' ─────────────────────────────────────────────────────────────────────────────
' qdisk  |  Infrastructure  |  UpdateClient.vb
' Cliente HTTP para verificação de novas versões do aplicativo.
'
' © Quallit — Licenciado sob GPL-3.0-or-later.
' ─────────────────────────────────────────────────────────────────────────────

Imports System.Net.Http
Imports System.Text.Json
Imports System.Text.Json.Serialization
Imports System.Text.RegularExpressions
Imports System.Threading
Imports System.Threading.Tasks
Imports qdisk.Core.Services

Namespace Infrastructure
    Public Class UpdateClient
        Public Shared ReadOnly Property UpdateManifestUrl As String
            Get
                Return EndpointConfig.Current.PrimaryBaseUrl & "qdisk.update.json"
            End Get
        End Property

        Public Shared ReadOnly Property InstallerUrl As String
            Get
                Return EndpointConfig.Current.PrimaryBaseUrl & "Setup_qdisk.update.exe"
            End Get
        End Property

        Public Shared ReadOnly Property DirectUpdateManifestUrl As String
            Get
                Return EndpointConfig.Current.FallbackPublicBaseUrl & "qdisk.update.json"
            End Get
        End Property

        Public Shared ReadOnly Property DirectInstallerBaseUrl As String
            Get
                Return EndpointConfig.Current.FallbackPublicBaseUrl
            End Get
        End Property

        Private Shared ReadOnly _httpClient As HttpClient = CreateHttpClient()

        Public Async Function CheckForUpdatesAsync(currentVersion As Version, cancellationToken As CancellationToken) As Task(Of UpdateCheckResult)
            If currentVersion Is Nothing Then
                currentVersion = New Version(0, 0, 0, 0)
            End If

            Dim errors As New List(Of String)()
            For Each manifestUrl In GetManifestUrls()
                Try
                    Dim json = Await GetStringWithRedirectAsync(manifestUrl, 0, cancellationToken)
                    Dim manifest = ParseManifest(json)
                    Dim latestVersion = manifest.GetVersion()
                    If latestVersion Is Nothing Then
                        Return UpdateCheckResult.Unavailable("Manifesto de atualizacao sem campo version/latestVersion valido.")
                    End If

                    Dim manifestInstallerUrl = manifest.GetInstallerUrl()
                    Dim installer = ResolveInstallerUrl(manifestInstallerUrl, manifest, manifestUrl)
                    Dim installerUri As Uri = Nothing
                    If Not Uri.TryCreate(installer, UriKind.Absolute, installerUri) OrElse installerUri.Scheme <> Uri.UriSchemeHttps Then
                        Return UpdateCheckResult.Unavailable("Manifesto de atualizacao sem installerUrl HTTPS valido.")
                    End If

                    Return New UpdateCheckResult() With {
                        .Success = True,
                        .UpdateAvailable = latestVersion > currentVersion,
                        .CurrentVersion = currentVersion,
                        .LatestVersion = latestVersion,
                        .Manifest = manifest,
                        .ManifestUrl = manifestUrl,
                        .InstallerUrl = installer
                    }
                Catch ex As OperationCanceledException
                    Throw
                Catch ex As Exception
                    errors.Add($"{manifestUrl}: {ex.Message}")
                End Try
            Next

            Return UpdateCheckResult.Unavailable("Manifesto indisponivel. " & String.Join(" | ", errors))
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

                    Throw New InvalidOperationException("O link retornou pagina HTML sem URL de destino.")
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

        Private Shared Function GetManifestUrls() As String()
            Return New String() {
                UpdateManifestUrl,
                DirectUpdateManifestUrl
            }
        End Function

        Private Shared Function ResolveInstallerUrl(manifestInstallerUrl As String, manifest As UpdateManifest, manifestUrl As String) As String
            If manifest IsNot Nothing AndAlso Not String.IsNullOrWhiteSpace(manifest.FileName) AndAlso manifestUrl.Equals(DirectUpdateManifestUrl, StringComparison.OrdinalIgnoreCase) Then
                Return DirectInstallerBaseUrl & Uri.EscapeDataString(manifest.FileName.Trim())
            End If

            If Not String.IsNullOrWhiteSpace(manifestInstallerUrl) Then
                Dim value = manifestInstallerUrl.Trim()
                Return value
            End If

            If manifest IsNot Nothing AndAlso Not String.IsNullOrWhiteSpace(manifest.FileName) Then
                Return EndpointConfig.Current.PrimaryBaseUrl & Uri.EscapeDataString(manifest.FileName.Trim())
            End If

            Return InstallerUrl
        End Function

        Private Shared Function CreateHttpClient() As HttpClient
            Dim client As New HttpClient() With {.Timeout = TimeSpan.FromSeconds(20)}
            HttpIdentity.Apply(client.DefaultRequestHeaders, "qdisk-Updater/1.0")
            Return client
        End Function

        Private Shared Function ParseManifest(json As String) As UpdateManifest
            If String.IsNullOrWhiteSpace(json) Then
                Throw New InvalidOperationException("Manifesto de atualizacao vazio.")
            End If

            Dim options As New JsonSerializerOptions() With {.PropertyNameCaseInsensitive = True}
            Dim manifest = JsonSerializer.Deserialize(Of UpdateManifest)(json, options)
            If manifest Is Nothing Then
                Throw New InvalidOperationException("Manifesto de atualizacao invalido.")
            End If
            Return manifest
        End Function
    End Class

    Public Class UpdateManifest
        Public Property Version As String
        Public Property LatestVersion As String
        Public Property ReleaseDate As String
        Public Property InstallerUrl As String
        Public Property Url As String
        Public Property DownloadUrl As String
        Public Property Sha256 As String
        Public Property Mandatory As Boolean
        Public Property MinimumSupportedVersion As String
        Public Property Notes As List(Of String)
        Public Property FileName As String
        Public Property SizeBytes As Long

        <JsonIgnore>
        Public ReadOnly Property EffectiveVersion As String
            Get
                Return If(Not String.IsNullOrWhiteSpace(Version), Version, LatestVersion)
            End Get
        End Property

        Public Function GetVersion() As Version
            Dim parsed As Version = Nothing
            If System.Version.TryParse(EffectiveVersion, parsed) Then
                Return parsed
            End If
            Return Nothing
        End Function

        Public Function GetInstallerUrl() As String
            If Not String.IsNullOrWhiteSpace(InstallerUrl) Then Return InstallerUrl
            If Not String.IsNullOrWhiteSpace(DownloadUrl) Then Return DownloadUrl
            If Not String.IsNullOrWhiteSpace(Url) Then Return Url
            Return String.Empty
        End Function
    End Class

    Public Class UpdateCheckResult
        Public Property Success As Boolean
        Public Property UpdateAvailable As Boolean
        Public Property CurrentVersion As Version
        Public Property LatestVersion As Version
        Public Property Manifest As UpdateManifest
        Public Property ManifestUrl As String
        Public Property InstallerUrl As String
        Public Property ErrorMessage As String

        Public Shared Function Unavailable(message As String) As UpdateCheckResult
            Return New UpdateCheckResult() With {
                .Success = False,
                .UpdateAvailable = False,
                .ErrorMessage = message
            }
        End Function
    End Class
End Namespace


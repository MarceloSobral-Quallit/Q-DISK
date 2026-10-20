' ─────────────────────────────────────────────────────────────────────────────
' qdisk  |  Core / Services  |  EndpointConfig.vb
' Configuracao centralizada dos endpoints publicos e do fallback.
' ─────────────────────────────────────────────────────────────────────────────

Imports System.IO
Imports System.Text.Json

Namespace Services
    Public Class EndpointConfig
        Public Const FileName As String = "qdisk.endpoints.json"

        Private Shared ReadOnly _current As Lazy(Of EndpointConfig) = New Lazy(Of EndpointConfig)(AddressOf Load)

        Public Property PrimaryBaseUrl As String = "https://installer.quallit.com.br/qdisk/"
        Public Property FallbackPublicBaseUrl As String = "https://mateweb.ddns.net/qdisk/"
        Public Property FallbackHost As String = "mateweb.ddns.net"
        Public Property FallbackPort As Integer = 2205
        Public Property FallbackUser As String = "qdisk-update"
        Public Property FallbackRemotePath As String = "/store/sites/qdisk/www"
        Public Property InstallerRepositoryUrl As String = "https://github.com/quallitdesenv/quallit-installer.git"
        Public Property InstallerTargetSubdir As String = "public/qdisk"

        Public Shared ReadOnly Property Current As EndpointConfig
            Get
                Return _current.Value
            End Get
        End Property

        Private Shared Function Load() As EndpointConfig
            Dim config = New EndpointConfig()
            ' I-08 / T-01: em producao a configuracao vem so do recurso embutido no executavel.
            Dim json = AppConfigSource.ReadText(FileName)
            If String.IsNullOrWhiteSpace(json) Then
                Return config
            End If

            Try
                Using document = JsonDocument.Parse(json)
                    Dim root = document.RootElement
                    Dim primary As JsonElement
                    If root.TryGetProperty("primary", primary) Then
                        config.PrimaryBaseUrl = ReadString(primary, "baseUrl", config.PrimaryBaseUrl)
                    End If

                    Dim fallback As JsonElement
                    If root.TryGetProperty("fallback", fallback) Then
                        config.FallbackPublicBaseUrl = ReadString(fallback, "publicBaseUrl", config.FallbackPublicBaseUrl)
                        config.FallbackHost = ReadString(fallback, "host", config.FallbackHost)
                        config.FallbackPort = ReadInteger(fallback, "port", config.FallbackPort)
                        config.FallbackUser = ReadString(fallback, "user", config.FallbackUser)
                        config.FallbackRemotePath = ReadString(fallback, "remotePath", config.FallbackRemotePath)
                    End If

                    Dim installerGit As JsonElement
                    If root.TryGetProperty("installerGit", installerGit) Then
                        config.InstallerRepositoryUrl = ReadString(installerGit, "repositoryUrl", config.InstallerRepositoryUrl)
                        config.InstallerTargetSubdir = ReadString(installerGit, "targetSubdir", config.InstallerTargetSubdir)
                    End If
                End Using
            Catch
                Return New EndpointConfig()
            End Try

            ' I-08: URLs de catalogo e atualizacao so em HTTPS; fora disso, volta ao padrao.
            Dim defaults = New EndpointConfig()
            If Not UrlPolicy.IsHttps(config.PrimaryBaseUrl) Then config.PrimaryBaseUrl = defaults.PrimaryBaseUrl
            If Not UrlPolicy.IsHttps(config.FallbackPublicBaseUrl) Then config.FallbackPublicBaseUrl = defaults.FallbackPublicBaseUrl

            config.PrimaryBaseUrl = EnsureTrailingSlash(config.PrimaryBaseUrl)
            config.FallbackPublicBaseUrl = EnsureTrailingSlash(config.FallbackPublicBaseUrl)
            config.FallbackRemotePath = config.FallbackRemotePath.TrimEnd("/"c)
            Return config
        End Function

        Private Shared Function ReadString(element As JsonElement, propertyName As String, fallback As String) As String
            Dim value As JsonElement
            If element.TryGetProperty(propertyName, value) AndAlso value.ValueKind = JsonValueKind.String Then
                Dim text = value.GetString()
                If Not String.IsNullOrWhiteSpace(text) Then
                    Return text.Trim()
                End If
            End If
            Return fallback
        End Function

        Private Shared Function ReadInteger(element As JsonElement, propertyName As String, fallback As Integer) As Integer
            Dim value As JsonElement
            If element.TryGetProperty(propertyName, value) Then
                If value.ValueKind = JsonValueKind.Number Then
                    Dim number As Integer
                    If value.TryGetInt32(number) Then
                        Return number
                    End If
                ElseIf value.ValueKind = JsonValueKind.String Then
                    Dim number As Integer
                    If Integer.TryParse(value.GetString(), number) Then
                        Return number
                    End If
                End If
            End If
            Return fallback
        End Function

        Private Shared Function EnsureTrailingSlash(value As String) As String
            If String.IsNullOrWhiteSpace(value) Then
                Return value
            End If
            Return value.TrimEnd("/"c) & "/"
        End Function
    End Class
End Namespace

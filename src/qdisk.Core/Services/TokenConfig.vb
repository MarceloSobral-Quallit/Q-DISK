Imports System.Text.Json
Imports qdisk.Core.Models

Namespace Services
    ''' <summary>
    ''' Perfis de acesso e verificadores do token diario. Le config/qdisk.tokens.app.json (embutido no executavel),
    ''' gerado por tools/utils/generate_token_verifiers.py: nomes, catalogos e verificadores PBKDF2 por dia, sem os
    ''' internal_id (esses ficam so em config/qdisk.tokens.json, no repositorio e no QDisk_Adm).
    ''' </summary>
    Public Class TokenConfig
        Public Const FileName As String = "qdisk.tokens.app.json"

        Private Shared ReadOnly _current As Lazy(Of TokenConfig) = New Lazy(Of TokenConfig)(AddressOf Load)
        Private ReadOnly _profiles As Dictionary(Of UserProfile, TokenProfileConfig)

        Public Sub New()
            _profiles = CreateDefaultProfiles()
        End Sub

        Public ReadOnly Property Profiles As IReadOnlyDictionary(Of UserProfile, TokenProfileConfig)
            Get
                Return _profiles
            End Get
        End Property

        Public Property Verifiers As New TokenVerifierSet()

        Public Shared ReadOnly Property Current As TokenConfig
            Get
                Return _current.Value
            End Get
        End Property

        Public Function GetProfileConfig(profile As UserProfile) As TokenProfileConfig
            Dim config As TokenProfileConfig = Nothing
            If _profiles.TryGetValue(profile, config) Then
                Return config
            End If
            Return _profiles(UserProfile.RD)
        End Function

        Private Shared Function Load() As TokenConfig
            ' I-08 / T-01: em producao a configuracao vem so do recurso embutido no executavel.
            Return Parse(AppConfigSource.ReadText(FileName))
        End Function

        Public Shared Function Parse(json As String) As TokenConfig
            Dim config = New TokenConfig()
            If String.IsNullOrWhiteSpace(json) Then
                Return config
            End If

            Try
                Using document = JsonDocument.Parse(json)
                    Dim root = document.RootElement
                    Dim profilesElement As JsonElement
                    If root.TryGetProperty("profiles", profilesElement) AndAlso profilesElement.ValueKind = JsonValueKind.Array Then
                        For Each profileElement In profilesElement.EnumerateArray()
                            If profileElement.ValueKind <> JsonValueKind.Object Then Continue For
                            Dim profile = ReadProfile(profileElement, "profile")
                            If profile = UserProfile.None Then Continue For
                            Dim current = config.GetProfileConfig(profile)
                            config._profiles(profile) = New TokenProfileConfig With {
                                .Profile = profile,
                                .DisplayName = ReadString(profileElement, "displayName", current.DisplayName),
                                .CatalogFileName = ReadString(profileElement, "catalogFile", current.CatalogFileName)
                            }
                        Next
                    End If

                    Dim verifiersElement As JsonElement
                    If root.TryGetProperty("verifiers", verifiersElement) Then
                        config.Verifiers = TokenVerifierSet.Parse(verifiersElement)
                    End If
                End Using
            Catch
                Return New TokenConfig()
            End Try

            Return config
        End Function

        Private Shared Function CreateDefaultProfiles() As Dictionary(Of UserProfile, TokenProfileConfig)
            Return New Dictionary(Of UserProfile, TokenProfileConfig) From {
                {UserProfile.RD, New TokenProfileConfig With {.Profile = UserProfile.RD, .DisplayName = "RD", .CatalogFileName = "qdisk.catalog.rd.json"}},
                {UserProfile.PNB, New TokenProfileConfig With {.Profile = UserProfile.PNB, .DisplayName = "PNB", .CatalogFileName = "qdisk.catalog.pnb.json"}},
                {UserProfile.DevTeam, New TokenProfileConfig With {.Profile = UserProfile.DevTeam, .DisplayName = "DevTeam", .CatalogFileName = "qdisk.catalog.devteam.json"}}
            }
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

        Private Shared Function ReadProfile(element As JsonElement, propertyName As String) As UserProfile
            Dim text = ReadString(element, propertyName, Nothing)
            Dim profile As UserProfile
            If Not String.IsNullOrWhiteSpace(text) AndAlso [Enum].TryParse(text, ignoreCase:=True, result:=profile) Then
                Return profile
            End If
            Return UserProfile.None
        End Function
    End Class

    Public Class TokenProfileConfig
        Public Property Profile As UserProfile
        Public Property DisplayName As String
        Public Property CatalogFileName As String
    End Class
End Namespace

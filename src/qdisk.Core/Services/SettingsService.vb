' ─────────────────────────────────────────────────────────────────────────────
' qdisk  |  Core / Services  |  SettingsService.vb
' Carrega e persiste as configurações da aplicação em JSON no AppData.
'
' © Quallit — Licenciado sob GPL-3.0-or-later.
' ─────────────────────────────────────────────────────────────────────────────

Imports System.IO
Imports System.Text.Json
Imports qdisk.Core.Models

Namespace Services
    Public Class SettingsService
        Private ReadOnly _settingsPath As String

        Public Sub New()
            Dim configPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                                          "qdisk",
                                          "config")
            Directory.CreateDirectory(configPath)
            _settingsPath = Path.Combine(configPath, "settings.json")
        End Sub

        Public Function Load() As AppSettings
            If Not File.Exists(_settingsPath) Then
                Dim defaults = AppSettings.CreateDefault()
                Save(defaults)
                Return defaults
            End If

            Dim json = File.ReadAllText(_settingsPath)
            Dim options = New JsonSerializerOptions() With {
                .PropertyNameCaseInsensitive = True
            }
            Dim settings = JsonSerializer.Deserialize(Of AppSettings)(json, options)
            If settings Is Nothing Then
                settings = New AppSettings()
            End If
            settings.ImageCacheDir = ResolveImageCacheDir(settings.ImageCacheDir)

            Return settings
        End Function

        Public Sub Save(settings As AppSettings)
            If settings Is Nothing Then
                Return
            End If

            settings.ImageCacheDir = ResolveImageCacheDir(settings.ImageCacheDir)
            Dim options = New JsonSerializerOptions() With {.WriteIndented = True}
            File.WriteAllText(_settingsPath, JsonSerializer.Serialize(settings, options))
        End Sub

        Public Shared Function ResolveImageCacheDir(cacheDir As String) As String
            Dim resolved = If(cacheDir, String.Empty).Trim()
            If String.IsNullOrWhiteSpace(resolved) Then
                resolved = AppSettings.GetDefaultImageCacheDir()
            End If

            resolved = Environment.ExpandEnvironmentVariables(resolved)
            Return Path.GetFullPath(resolved)
        End Function
    End Class
End Namespace


' ─────────────────────────────────────────────────────────────────────────────
' Quallit NetSpeedTest  |  CatalogSource.vb
' Le os catalogos publicados (servidor principal; mateweb se o principal falhar)
' e separa os itens por origem de download.
'
' © Quallit — Licenciado sob GPL-3.0-or-later.
' ─────────────────────────────────────────────────────────────────────────────

Imports System.Net.Http
Imports System.Text.Json
Imports System.Threading
Imports qdisk.Core.Models
Imports qdisk.Core.Services

Public Class CatalogSource
    Private Shared ReadOnly CatalogFileNames As String() = {
        CatalogService.RdCatalogFileName, CatalogService.PnbCatalogFileName,
        CatalogService.DevTeamCatalogFileName, CatalogService.ToolsCatalogFileName
    }

    Private ReadOnly _client As New HttpClient() With {.Timeout = TimeSpan.FromSeconds(30)}

    ''' <summary>Itens habilitados dos 4 catalogos, sem repetir o mesmo link. 'log' recebe uma linha por catalogo.</summary>
    Public Async Function LoadAsync(log As Action(Of String), cancellationToken As CancellationToken) As Task(Of List(Of CatalogItem))
        Dim items As New List(Of CatalogItem)()
        Dim seen As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
        For Each fileName In CatalogFileNames
            Dim loaded As List(Of CatalogItem) = Nothing
            For Each baseUrl In {CatalogService.PrimaryBaseUrl, CatalogService.FallbackBaseUrl}
                Try
                    Dim json = Await _client.GetStringAsync(baseUrl & fileName, cancellationToken)
                    loaded = Parse(json)
                    log($"Catalogo {fileName}: {loaded.Count} item(ns) de {New Uri(baseUrl).Host}.")
                    Exit For
                Catch ex As OperationCanceledException When cancellationToken.IsCancellationRequested
                    Throw
                Catch ex As Exception
                    log($"Catalogo {fileName} em {New Uri(baseUrl).Host}: {ex.Message}")
                End Try
            Next
            If loaded Is Nothing Then Continue For
            For Each item In loaded
                If item IsNot Nothing AndAlso item.Enabled AndAlso Not String.IsNullOrWhiteSpace(item.FileUrl) AndAlso seen.Add(item.FileUrl.Trim()) Then
                    items.Add(item)
                End If
            Next
        Next
        Return items
    End Function

    ' Catalogo publicado: array simples ou objeto {build, items}.
    Private Shared Function Parse(json As String) As List(Of CatalogItem)
        Dim itemsJson = json
        Using document = JsonDocument.Parse(json)
            If document.RootElement.ValueKind = JsonValueKind.Object Then
                itemsJson = document.RootElement.GetProperty("items").GetRawText()
            End If
        End Using
        Dim options As New JsonSerializerOptions() With {.PropertyNameCaseInsensitive = True}
        Return If(JsonSerializer.Deserialize(Of List(Of CatalogItem))(itemsJson, options), New List(Of CatalogItem)())
    End Function
End Class

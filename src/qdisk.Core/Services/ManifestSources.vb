Namespace Services
    ''' <summary>
    ''' Origens do manifesto de USB (P3): o manifestUrl do catalogo e, quando ele aponta para o servidor principal, o
    ''' mesmo caminho no fallback (mateweb), que recebe os manifestos na mesma publicacao. Em qualquer origem o app exige
    ''' o manifestSha256 do catalogo.
    ''' </summary>
    Public NotInheritable Class ManifestSources
        Private Sub New()
        End Sub

        Public Shared Function GetCandidateUrls(manifestUrl As String, primaryBaseUrl As String, fallbackBaseUrl As String) As IReadOnlyList(Of String)
            Dim result As New List(Of String)()
            Dim url = If(manifestUrl, String.Empty).Trim()
            If url.Length = 0 Then Return result
            result.Add(url)

            Dim primary = EnsureTrailingSlash(primaryBaseUrl)
            Dim fallback = EnsureTrailingSlash(fallbackBaseUrl)
            If primary.Length > 0 AndAlso fallback.Length > 0 AndAlso
               Not String.Equals(primary, fallback, StringComparison.OrdinalIgnoreCase) AndAlso
               url.StartsWith(primary, StringComparison.OrdinalIgnoreCase) Then
                result.Add(fallback & url.Substring(primary.Length))
            End If
            Return result
        End Function

        Private Shared Function EnsureTrailingSlash(value As String) As String
            Dim text = If(value, String.Empty).Trim()
            If text.Length = 0 Then Return text
            Return If(text.EndsWith("/"), text, text & "/")
        End Function
    End Class
End Namespace

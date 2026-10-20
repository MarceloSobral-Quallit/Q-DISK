' ─────────────────────────────────────────────────────────────────────────────
' qdisk  |  Core / Services  |  UrlPolicy.vb
' Regra unica para URLs vindas de catalogos, manifestos e paginas de redirecionamento.
'
' © Quallit — Licenciado sob GPL-3.0-or-later.
' ─────────────────────────────────────────────────────────────────────────────

Namespace Services
    Public NotInheritable Class UrlPolicy
        Private Sub New()
        End Sub

        ''' <summary>
        ''' Verdadeiro so para URL absoluta com esquema HTTPS. Usado em todo redirecionamento
        ''' extraido de pagina HTML e nas URLs de catalogo, para que um intermediario nao possa
        ''' trocar o conteudo por um canal sem criptografia.
        ''' </summary>
        Public Shared Function IsHttps(url As String) As Boolean
            If String.IsNullOrWhiteSpace(url) Then
                Return False
            End If
            Dim parsed As Uri = Nothing
            Return Uri.TryCreate(url.Trim(), UriKind.Absolute, parsed) AndAlso parsed.Scheme = Uri.UriSchemeHttps
        End Function
    End Class
End Namespace

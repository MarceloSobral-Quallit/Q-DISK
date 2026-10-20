' ─────────────────────────────────────────────────────────────────────────────
' qdisk  |  Core / Services  |  HttpIdentity.vb
' User-Agent das requisicoes HTTP: o produto de cada cliente (ex.: QuallitDownloader/1.0)
' seguido da identificacao que o SharePoint Online pede a aplicacoes de empresa
' (NONISV|Empresa|App/Versao; "well-decorated traffic will be prioritized").
' Ref.: learn.microsoft.com/sharepoint/dev/general-development/how-to-avoid-getting-throttled-or-blocked-in-sharepoint-online
'
' © Quallit — Licenciado sob GPL-3.0-or-later.
' ─────────────────────────────────────────────────────────────────────────────

Imports System.Net.Http.Headers

Namespace Services
    Public NotInheritable Class HttpIdentity
        Private Sub New()
        End Sub

        ''' <summary>Identificacao da aplicacao no formato do SharePoint Online, com a versao do produto.</summary>
        Public Shared ReadOnly Property AppToken As String =
            $"NONISV|Quallit|qdisk/{AppVersionFormat.Format(GetType(HttpIdentity).Assembly.GetName().Version)}"

        ''' <summary>Acrescenta ao cabecalho o produto do cliente e a identificacao da aplicacao.</summary>
        Public Shared Sub Apply(headers As HttpRequestHeaders, clientProduct As String)
            headers.UserAgent.ParseAdd(clientProduct)
            headers.UserAgent.ParseAdd(AppToken)
        End Sub
    End Class
End Namespace

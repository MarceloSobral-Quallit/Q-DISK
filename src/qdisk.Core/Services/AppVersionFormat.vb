' ─────────────────────────────────────────────────────────────────────────────
' qdisk  |  Core / Services  |  AppVersionFormat.vb
' Formato unico de exibicao da versao (ex.: 3.00.10.26), igual ao do Directory.Build.props.
'
' © Quallit — Licenciado sob GPL-3.0-or-later.
' ─────────────────────────────────────────────────────────────────────────────

Namespace Services
    Public NotInheritable Class AppVersionFormat
        Private Sub New()
        End Sub

        ''' <summary>
        ''' System.Version descarta zeros a esquerda ("3.00.10.26" vira 3.0.10.26); aqui eles voltam
        ''' com no minimo dois digitos no segundo e no terceiro campo. Retorna Nothing para Nothing.
        ''' </summary>
        Public Shared Function Format(version As Version) As String
            If version Is Nothing Then
                Return Nothing
            End If
            Return $"{version.Major}.{version.Minor:D2}.{version.Build:D2}.{version.Revision}"
        End Function
    End Class
End Namespace

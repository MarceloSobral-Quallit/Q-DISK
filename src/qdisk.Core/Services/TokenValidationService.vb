' ─────────────────────────────────────────────────────────────────────────────
' qdisk  |  Core / Services  |  TokenValidationService.vb
' Valida o token diário e resolve automaticamente o perfil de acesso.
'
' © Quallit — Licenciado sob GPL-3.0-or-later.
' ─────────────────────────────────────────────────────────────────────────────

Imports qdisk.Core.Models

Namespace Services
    ''' <summary>
    ''' Valida o token diario (6 digitos, gerado pelo site pela regra de docs/DOCUMENTACAO_TECNICA.md §14) contra os
    ''' verificadores embutidos (<see cref="TokenVerifierSet"/>). O app nao conhece os internal_id nem calcula tokens.
    ''' </summary>
    Public Class TokenValidationService
        Private ReadOnly _verifiers As TokenVerifierSet

        Public Sub New()
            Me.New(TokenConfig.Current.Verifiers)
        End Sub

        Public Sub New(verifiers As TokenVerifierSet)
            _verifiers = If(verifiers, New TokenVerifierSet())
        End Sub

        ''' <summary>
        ''' True quando os verificadores embutidos nao cobrem a data de hoje (versao do app antiga demais): nenhum token
        ''' valida ate atualizar o app.
        ''' </summary>
        Public ReadOnly Property VerifiersExpired As Boolean
            Get
                Return Not _verifiers.Covers(DateTime.Today)
            End Get
        End Property

        ''' <summary>
        ''' Valida o token e retorna o perfil correspondente.
        ''' Retorna UserProfile.None se o token for inválido.
        ''' </summary>
        Public Function ResolveProfile(inputToken As String) As UserProfile
            If String.IsNullOrWhiteSpace(inputToken) Then
                Return UserProfile.None
            End If

            Dim cleanToken = System.Text.RegularExpressions.Regex.Replace(inputToken.Trim(), "\D", "")
            If cleanToken.Length <> 6 Then
                Return UserProfile.None
            End If

            ' Data local; e a data UTC quando diferente (mesma tolerancia da regra antiga).
            Dim profile = _verifiers.Match(cleanToken, DateTime.Today)
            If profile = UserProfile.None AndAlso DateTime.UtcNow.Date <> DateTime.Today Then
                profile = _verifiers.Match(cleanToken, DateTime.UtcNow.Date)
            End If
            Return profile
        End Function

        ''' <summary>
        ''' Mantém compatibilidade: retorna True se o token for válido para qualquer perfil.
        ''' </summary>
        Public Function ValidateToken(inputToken As String) As Boolean
            Return ResolveProfile(inputToken) <> UserProfile.None
        End Function
    End Class
End Namespace

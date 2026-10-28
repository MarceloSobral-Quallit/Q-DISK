' ─────────────────────────────────────────────────────────────────────────────
' qdisk  |  Core / Services  |  TokenValidationService.vb
' Valida o token (qualquer dia da semana corrente) e resolve automaticamente o perfil de acesso.
'
' © Quallit — Licenciado sob GPL-3.0-or-later.
' ─────────────────────────────────────────────────────────────────────────────

Imports qdisk.Core.Models

Namespace Services
    ''' <summary>
    ''' Valida o token diario (6 digitos, gerado pelo site pela regra de docs/DOCUMENTACAO_TECNICA.md §14) contra os
    ''' verificadores embutidos (<see cref="TokenVerifierSet"/>). O app nao conhece os internal_id nem calcula tokens.
    ''' O token de qualquer dia da semana corrente (domingo a sabado) e aceito.
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
            Return ResolveProfile(inputToken, DateTime.Today, DateTime.UtcNow.Date)
        End Function

        ''' <summary>
        ''' Aceita o token de qualquer dia da semana corrente (domingo 00:00 a sabado 23:59) da data local; e da semana
        ''' da data UTC quando for outra (mesma tolerancia da regra diaria).
        ''' </summary>
        Public Function ResolveProfile(inputToken As String, today As Date, utcToday As Date) As UserProfile
            If String.IsNullOrWhiteSpace(inputToken) Then
                Return UserProfile.None
            End If

            Dim cleanToken = System.Text.RegularExpressions.Regex.Replace(inputToken.Trim(), "\D", "")
            If cleanToken.Length <> 6 Then
                Return UserProfile.None
            End If

            Dim profile = MatchWeek(cleanToken, today)
            If profile = UserProfile.None AndAlso WeekStart(utcToday) <> WeekStart(today) Then
                profile = MatchWeek(cleanToken, utcToday)
            End If
            Return profile
        End Function

        ''' <summary>Domingo da semana de <paramref name="day"/>.</summary>
        Public Shared Function WeekStart(day As Date) As Date
            Return day.Date.AddDays(-CInt(day.DayOfWeek))
        End Function

        ' O dia informado primeiro (caso comum), depois os outros dias da mesma semana.
        Private Function MatchWeek(token As String, day As Date) As UserProfile
            Dim profile = _verifiers.Match(token, day.Date)
            If profile <> UserProfile.None Then Return profile
            Dim sunday = WeekStart(day)
            For offset = 0 To 6
                Dim other = sunday.AddDays(offset)
                If other = day.Date Then Continue For
                profile = _verifiers.Match(token, other)
                If profile <> UserProfile.None Then Return profile
            Next
            Return UserProfile.None
        End Function

        ''' <summary>
        ''' Mantém compatibilidade: retorna True se o token for válido para qualquer perfil.
        ''' </summary>
        Public Function ValidateToken(inputToken As String) As Boolean
            Return ResolveProfile(inputToken) <> UserProfile.None
        End Function
    End Class
End Namespace

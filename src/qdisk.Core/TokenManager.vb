' ─────────────────────────────────────────────────────────────────────────────
' qdisk  |  Core  |  TokenManager.vb
' Singleton estático que mantém o token e o perfil de usuário em memória durante a sessão.
'
' © Quallit — Licenciado sob GPL-3.0-or-later.
' ─────────────────────────────────────────────────────────────────────────────

Imports System
Imports qdisk.Core.Models

''' <summary>
''' Armazena o token fornecido via linha de comando para uso em reinicializações
''' </summary>
Public NotInheritable Class TokenManager
    Private Shared _cachedToken As String = Nothing
    Private Shared _tokenFromCommandLine As Boolean = False
    Private Shared _userProfile As UserProfile = UserProfile.None

    ''' <summary>
    ''' Armazena o token fornecido via linha de comando
    ''' </summary>
    Public Shared Sub SetToken(token As String, fromCommandLine As Boolean)
        _cachedToken = token
        _tokenFromCommandLine = fromCommandLine
    End Sub

    ''' <summary>
    ''' Armazena o token e o perfil de usuário resolvido
    ''' </summary>
    Public Shared Sub SetTokenWithProfile(token As String, fromCommandLine As Boolean, profile As UserProfile)
        _cachedToken = token
        _tokenFromCommandLine = fromCommandLine
        _userProfile = profile
    End Sub

    ''' <summary>
    ''' Obtém o perfil de usuário resolvido na validação do token
    ''' </summary>
    Public Shared Function GetUserProfile() As UserProfile
        Return _userProfile
    End Function

    ''' <summary>
    ''' Obtém o token armazenado
    ''' </summary>
    ''' <returns>Token armazenado ou Nothing</returns>
    Public Shared Function GetToken() As String
        Return _cachedToken
    End Function

    ''' <summary>
    ''' Verifica se o token foi fornecido via linha de comando
    ''' </summary>
    ''' <returns>True se o token veio da linha de comando</returns>
    Public Shared Function IsTokenFromCommandLine() As Boolean
        Return _tokenFromCommandLine
    End Function

    ''' <summary>
    ''' Limpa o token armazenado
    ''' </summary>
    Public Shared Sub ClearToken()
        _cachedToken = Nothing
        _tokenFromCommandLine = False
        _userProfile = UserProfile.None
    End Sub

    ''' <summary>
    ''' Constrói os argumentos de linha de comando incluindo o token se disponível
    ''' </summary>
    ''' <param name="additionalArgs">Argumentos adicionais</param>
    ''' <returns>String com argumentos completos</returns>
    Public Shared Function BuildArgsWithToken(ParamArray additionalArgs As String()) As String
        Dim args As New List(Of String)()
        
        ' Adicionar token se disponível (tanto via linha de comando quanto manual)
        If Not String.IsNullOrEmpty(_cachedToken) Then
            args.Add($"--token={_cachedToken}")
        End If
        
        ' Adicionar argumentos adicionais
        If additionalArgs IsNot Nothing AndAlso additionalArgs.Length > 0 Then
            args.AddRange(additionalArgs)
        End If
        
        Return String.Join(" ", args)
    End Function
End Class


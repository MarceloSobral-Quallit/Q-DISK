' ─────────────────────────────────────────────────────────────────────────────
' qdisk  |  Core / Interfaces  |  ILogService.vb
' Interface do serviço de log para desacoplamento entre camadas.
'
' © Quallit — Licenciado sob GPL-3.0-or-later.
' ─────────────────────────────────────────────────────────────────────────────

Namespace qdisk.Core

    ''' <summary>
    ''' Interface for logging services
    ''' </summary>
    Public Interface ILogService

        ''' <summary>
        ''' Logs an informational message
        ''' </summary>
        Sub LogInformation(message As String)

        ''' <summary>
        ''' Logs a warning message
        ''' </summary>
        Sub LogWarning(message As String)

        ''' <summary>
        ''' Logs an error message
        ''' </summary>
        Sub LogError(message As String)

        ''' <summary>
        ''' Logs an error message with exception details
        ''' </summary>
        Sub LogError(message As String, exception As Exception)

    End Interface

End Namespace

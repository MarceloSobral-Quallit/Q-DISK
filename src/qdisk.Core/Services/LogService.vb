' ─────────────────────────────────────────────────────────────────────────────
' qdisk  |  Core / Services  |  LogService.vb
' Escrita assíncrona thread-safe de entradas de log no arquivo local.
'
' © Quallit — Licenciado sob GPL-3.0-or-later.
' ─────────────────────────────────────────────────────────────────────────────

Imports System.IO
Imports System.Reflection
Imports System.Text
Imports System.Threading
Imports System.Threading.Tasks

Namespace Services
    Public Class LogService
        Implements qdisk.Core.ILogService
        Private ReadOnly _logPath As String
        Private ReadOnly _userName As String
        Private ReadOnly _appVersion As String
        Private Shared ReadOnly _writeGate As New SemaphoreSlim(1, 1)

        ' Caminho de debug secundário — ativo somente quando o diretório existir em disco.
        ' Crie o diretório manualmente para ativar o log secundário; apague para desativar sem recompilar.
        Private ReadOnly _debugLogDir As String =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                         "qdisk",
                         "debug_log")

        Public Sub New()
            Dim logDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                                      "qdisk",
                                      "logs")
            Directory.CreateDirectory(logDir)
            _logPath = Path.Combine(logDir, "app.log")
            _userName = Environment.UserName
            Dim version = Assembly.GetEntryAssembly()?.GetName().Version
            _appVersion = If(AppVersionFormat.Format(version), "0.0.0.0")
        End Sub

        ''' <summary>Grava em outro arquivo (usado pelos testes para nao poluir o log real).
        ''' Nao grava no log de debug secundario, que e compartilhado com o aplicativo.</summary>
        Public Sub New(logPath As String)
            Me.New()
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(logPath)))
            _logPath = logPath
            _debugLogDir = Nothing
        End Sub

        Public Function ReadAll() As String
            If Not File.Exists(_logPath) Then
                Return String.Empty
            End If
            Return File.ReadAllText(_logPath)
        End Function

        Public Async Function WriteAsync(message As String) As Task
            Dim line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} | Usuario: {_userName} | Versao: {_appVersion} | {message}{Environment.NewLine}"
            Dim bytes = Encoding.UTF8.GetBytes(line)

            ' P-08: ConfigureAwait(False) em todos os Await. O log e chamado de forma sincrona
            ' (.GetAwaiter().GetResult()) na thread da interface; sem isso, a continuacao esperaria
            ' essa mesma thread, que esta bloqueada, e o app travaria.
            Await _writeGate.WaitAsync().ConfigureAwait(False)
            Try
                ' Log principal
                Using stream As New FileStream(_logPath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite, 4096, True)
                    Await stream.WriteAsync(bytes, 0, bytes.Length).ConfigureAwait(False)
                End Using

                ' Log de debug secundário — gravado somente quando o diretório de debug existir
                If _debugLogDir IsNot Nothing AndAlso Directory.Exists(_debugLogDir) Then
                    Dim debugPath = Path.Combine(_debugLogDir, $"debug_{DateTime.Now:yyyy-MM-dd}.log")
                    Try
                        Using stream As New FileStream(debugPath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite, 4096, True)
                            Await stream.WriteAsync(bytes, 0, bytes.Length).ConfigureAwait(False)
                        End Using
                    Catch
                        ' Falha silenciosa — não interrompe o fluxo principal
                    End Try
                End If
            Finally
                _writeGate.Release()
            End Try
        End Function

        Public Sub LogInformation(message As String) Implements qdisk.Core.ILogService.LogInformation
            WriteAsync($"INFO | {message}").GetAwaiter().GetResult()
        End Sub

        Public Sub LogWarning(message As String) Implements qdisk.Core.ILogService.LogWarning
            WriteAsync($"WARN | {message}").GetAwaiter().GetResult()
        End Sub

        Public Sub LogError(message As String) Implements qdisk.Core.ILogService.LogError
            WriteAsync($"ERROR | {message}").GetAwaiter().GetResult()
        End Sub

        Public Sub LogError(message As String, exception As Exception) Implements qdisk.Core.ILogService.LogError
            Dim details = If(exception Is Nothing, String.Empty, $" | EX: {exception.Message}")
            WriteAsync($"ERROR | {message}{details}").GetAwaiter().GetResult()
        End Sub
    End Class
End Namespace


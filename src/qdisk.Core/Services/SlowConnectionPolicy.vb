' ─────────────────────────────────────────────────────────────────────────────
' qdisk  |  Core / Services  |  SlowConnectionPolicy.vb
' Troca de conexao lenta no SharePoint. Cada conexao nova cai ao acaso num caminho
' lento (~0,4 MiB/s, quase constante) ou rapido (8 a 19 MiB/s), e o caminho nao muda
' durante a conexao (medido em 16-17/10/2026). Reabrir do mesmo ponto e um novo sorteio.
' Bancada NetSpeedTest: 1 conexao 0,38 -> 8,40 MiB/s; 4 conexoes 13,6 -> 29,2 MiB/s.
'
' Limitacao (sem teto fixo, que deixava o ultimo fragmento preso no caminho lento):
'  - por conexao, espera crescente entre trocas seguidas: janela de 3 s, 6, 12... ate 60 s;
'    uma janela rapida volta a 3 s;
'  - por download (SlowSwapCoordinator): no maximo uma troca por segundo somando as conexoes,
'    e pausa geral quando o servidor manda Retry-After (429/503), como pede a Microsoft;
'  - nenhuma troca quando faltam menos de MinRemainingBytes no trecho.
'
' © Quallit — Licenciado sob GPL-3.0-or-later.
' ─────────────────────────────────────────────────────────────────────────────

Imports System.Diagnostics

Namespace Services
    ''' <summary>Monitor de uma conexao: diz quando trocar, com espera crescente entre trocas seguidas.</summary>
    Public NotInheritable Class SlowConnectionPolicy
        Public Const InitialWindowMs As Integer = 3000
        Public Const MaxWindowMs As Integer = 60000
        Public Const ThresholdBytesPerSec As Long = 1048576L
        Public Const MinRemainingBytes As Long = 16L * 1048576L

        Private Shared ReadOnly HostSuffixes As String() = {"sharepoint.com"}

        Private ReadOnly _coordinator As SlowSwapCoordinator
        Private ReadOnly _clock As Stopwatch = Stopwatch.StartNew()
        Private _windowMs As Integer = InitialWindowMs
        Private _windowStartMs As Long
        Private _windowBytes As Long
        Private _swaps As Integer
        Private _gaveUp As Boolean

        ''' <param name="coordinator">Compartilhado pelas conexoes do mesmo download (Nothing: sem limite global).</param>
        Public Sub New(Optional coordinator As SlowSwapCoordinator = Nothing)
            _coordinator = coordinator
        End Sub

        ''' <summary>Origens onde a troca vale (pelo host que respondeu, depois dos redirecionamentos).</summary>
        Public Shared Function AppliesTo(uri As Uri) As Boolean
            If uri Is Nothing Then Return False
            Dim host = uri.Host.ToLowerInvariant()
            Return HostSuffixes.Any(Function(suffix) host = suffix OrElse host.EndsWith("." & suffix, StringComparison.Ordinal))
        End Function

        ''' <summary>Trocas feitas por esta conexao.</summary>
        Public ReadOnly Property Swaps As Integer
            Get
                Return _swaps
            End Get
        End Property

        ''' <summary>Janela atual de avaliacao (cresce a cada troca seguida).</summary>
        Public ReadOnly Property WindowMs As Integer
            Get
                Return _windowMs
            End Get
        End Property

        ''' <summary>
        ''' Soma os bytes lidos. True quando a janela fechou abaixo do limite, faltam pelo menos
        ''' MinRemainingBytes e o coordenador libera a troca.
        ''' </summary>
        Public Function IsSlow(bytes As Integer, remainingBytes As Long) As Boolean
            _windowBytes += bytes
            Dim elapsedMs = _clock.ElapsedMilliseconds - _windowStartMs
            If elapsedMs < _windowMs Then Return False
            Dim slow = _windowBytes * 1000L \ elapsedMs < ThresholdBytesPerSec
            If Not slow Then _windowMs = InitialWindowMs
            RestartWindow()
            If Not slow OrElse _gaveUp OrElse remainingBytes < MinRemainingBytes Then Return False
            Return _coordinator Is Nothing OrElse _coordinator.TryAcquire()
        End Function

        ''' <summary>Registra a troca: a proxima avaliacao espera o dobro (ate MaxWindowMs).</summary>
        Public Sub RecordSwap()
            _swaps += 1
            _windowMs = Math.Min(_windowMs * 2, MaxWindowMs)
            RestartWindow()
        End Sub

        ''' <summary>O servidor pediu para esperar (429/503): pausa as trocas do download inteiro.</summary>
        Public Sub Throttled(retryAfter As TimeSpan)
            _coordinator?.PauseFor(retryAfter)
            RestartWindow()
        End Sub

        ''' <summary>A conexao nao pode ser trocada (servidor nao aceitou retomar): para de avaliar.</summary>
        Public Sub GiveUp()
            _gaveUp = True
        End Sub

        Public Sub RestartWindow()
            _windowStartMs = _clock.ElapsedMilliseconds
            _windowBytes = 0
        End Sub
    End Class

    ''' <summary>Limite das trocas de um download: uma por segundo e pausa geral em Retry-After.</summary>
    Public NotInheritable Class SlowSwapCoordinator
        Public Const MinIntervalMs As Integer = 1000
        Private ReadOnly _gate As New Object()
        Private ReadOnly _clock As Stopwatch = Stopwatch.StartNew()
        Private _lastSwapMs As Long = -MinIntervalMs
        Private _pausedUntilMs As Long

        Public Function TryAcquire() As Boolean
            SyncLock _gate
                Dim now = _clock.ElapsedMilliseconds
                If now < _pausedUntilMs OrElse now - _lastSwapMs < MinIntervalMs Then Return False
                _lastSwapMs = now
                Return True
            End SyncLock
        End Function

        Public Sub PauseFor(retryAfter As TimeSpan)
            SyncLock _gate
                Dim untilMs = _clock.ElapsedMilliseconds + CLng(Math.Max(1000.0R, retryAfter.TotalMilliseconds))
                If untilMs > _pausedUntilMs Then _pausedUntilMs = untilMs
            End SyncLock
        End Sub
    End Class
End Namespace

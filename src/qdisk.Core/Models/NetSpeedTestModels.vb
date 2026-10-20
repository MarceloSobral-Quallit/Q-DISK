' ─────────────────────────────────────────────────────────────────────────────
' qdisk  |  Core / Models  |  NetSpeedTestModels.vb
' Resultado e leitura ao vivo do teste de velocidade por tempo (origem, N conexões).
'
' © Quallit — Licenciado sob GPL-3.0-or-later.
' ─────────────────────────────────────────────────────────────────────────────

Namespace Models
    ''' <summary>Origens de download dos catálogos.</summary>
    Public Enum DownloadOrigin
        Dropbox
        SharePoint
        Outra
    End Enum

    ''' <summary>Resultado de uma medição (uma origem, uma quantidade de conexões).</summary>
    Public Class SpeedMeasurement
        Public Property Origin As DownloadOrigin
        Public Property ItemName As String
        Public Property FileName As String
        Public Property Host As String
        Public Property Connections As Integer
        ''' <summary>"OK", "Range não suportado" ou "Falha: ...".</summary>
        Public Property Status As String
        ''' <summary>Média sustentada em MiB/s, sem os primeiros segundos de aceleração.</summary>
        Public Property MiBps As Double
        Public Property MinMiBps As Double
        Public Property MaxMiBps As Double
        ''' <summary>Tempo até os cabeçalhos da primeira resposta do arquivo.</summary>
        Public Property FirstByteMs As Long
        ''' <summary>Conexões que receberam pelo menos 1 MiB (o SharePoint pode entregar por uma só).</summary>
        Public Property ActiveConnections As Integer
        Public Property TotalBytes As Long
        Public Property Seconds As Double
        Public Property PerSecondMiBps As Double() = Array.Empty(Of Double)()
        Public Property TestedAt As DateTimeOffset
        ''' <summary>Observações: conexões que caíram, arquivo menor que o tempo de medição.</summary>
        Public Property Notes As String = String.Empty
        ''' <summary>"origem" (cada requisição segue os redirecionamentos) ou "final" (link já resolvido, como o app).</summary>
        Public Property LinkMode As String = String.Empty
        ''' <summary>Medicao com troca de conexao lenta ligada (prototipo da bancada).</summary>
        Public Property SwapSlowEnabled As Boolean
        ''' <summary>Quantas vezes uma conexao lenta foi fechada e reaberta.</summary>
        Public Property SlowSwaps As Integer

        Public ReadOnly Property IsOk As Boolean
            Get
                Return String.Equals(Status, "OK", StringComparison.Ordinal)
            End Get
        End Property
    End Class

    ''' <summary>Leitura ao vivo durante a medição (para a tela mostrar o progresso).</summary>
    Public Class LiveReading
        Public Property Elapsed As TimeSpan
        Public Property Duration As TimeSpan
        ''' <summary>Vazão do último segundo, em MiB/s.</summary>
        Public Property CurrentMiBps As Double
        ''' <summary>Média desde o início da medição, em MiB/s.</summary>
        Public Property AverageMiBps As Double
        Public Property TotalBytes As Long
        Public Property ActiveConnections As Integer
        ''' <summary>Trocas de conexao lenta ate agora.</summary>
        Public Property SlowSwaps As Integer
        ''' <summary>MiB/s de cada segundo já completo.</summary>
        Public Property PerSecondMiBps As Double()
    End Class
End Namespace

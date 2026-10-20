' ─────────────────────────────────────────────────────────────────────────────
' qdisk  |  Core / Services  |  SegmentedDownloadService.vb
' Download multi-conexão (segmentado, via HTTP Range) restrito ao Dropbox e ao SharePoint
' (com troca de conexão lenta), com fallback automático e total para DownloadService nos demais casos.
'
' © Quallit — Licenciado sob GPL-3.0-or-later.
' ─────────────────────────────────────────────────────────────────────────────

Imports System.IO
Imports System.Net
Imports System.Net.Http
Imports System.Net.Http.Headers
Imports System.Threading
Imports System.Threading.Tasks
Imports qdisk.Core.Interfaces

Namespace Services
    Public Class SegmentedDownloadService
        Implements IDownloadService

        Private Const MinSegmentSizeBytes As Long = 20L * 1024 * 1024 ' 20 MB
        Private Const DefaultMaxConnections As Integer = 4
        Private Const MinConnections As Integer = 2
        Private Const MaxConnectionsCeiling As Integer = 8
        Private Const ChunkMaxAttempts As Integer = 5
        Private Const ChunkRetryBaseDelayMs As Integer = 500
        Private Const StreamBufferBytes As Integer = 81920 ' igual ao DownloadService
        Private Const ProgressReportMinIntervalMs As Integer = 150
        Private Const MaxRedirectDepth As Integer = 3
        ' Redivisao da cauda: so divide se cada metade ficar com pelo menos isto.
        Private Const RebalanceMinBytes As Long = 16L * 1024 * 1024 ' 16 MB

        ' Sufixos de host elegíveis para download segmentado — restrição explícita de escopo.
        ' Qualquer outro host (incluindo installer.quallit.com.br) nunca é segmentado.
        ' SharePoint de volta (17/10/2026), com troca de conexao lenta por fragmento (SlowConnectionPolicy):
        ' sem ela, as conexoes que caiam no caminho lento (~0,4 MiB/s) explicavam os 2,0 MiB/s com 8 conexoes
        ' do teste real de 16/10/2026; na bancada, 4 conexoes com troca chegaram a 29 MiB/s.
        Private Shared ReadOnly EligibleHostSuffixes As String() = {
            "dropbox.com",
            "dropboxusercontent.com",
            "sharepoint.com"
        }

        ''' <summary>Trocas de conexao lenta somadas de todos os fragmentos do ultimo download.</summary>
        Public ReadOnly Property LastRunSlowSwaps As Integer
            Get
                Return _slowSwaps
            End Get
        End Property
        Private _slowSwaps As Integer

        ''' <summary>Quantas vezes um trecho foi dividido para uma conexao livre (redivisao da cauda).</summary>
        Public ReadOnly Property LastRunRebalances As Integer
            Get
                Return _rebalances
            End Get
        End Property
        Private _rebalances As Integer

        ''' <summary>Trocas de conexao lenta quando o download caiu para conexao unica.</summary>
        Public ReadOnly Property FallbackSlowSwaps As Integer
            Get
                Return _fallback.LastRunSlowSwaps
            End Get
        End Property

        Private ReadOnly _maxConnections As Integer
        Private ReadOnly _fallback As New DownloadService()

        ''' <summary>Novas tentativas da requisicao inicial quando o download caiu para conexao unica.</summary>
        Public ReadOnly Property FallbackRequestRetries As List(Of String)
            Get
                Return _fallback.LastRunRequestRetries
            End Get
        End Property

        ' Chamado quando o download segmentado falha de vez (esgotou tentativas de um fragmento
        ' ou a junção final falhou), para o chamador perguntar ao usuário se deve continuar em
        ' conexão única. Se Nothing, cai automaticamente para conexão única (sem perguntar).
        Private ReadOnly _confirmFallback As Func(Of Task(Of Boolean))
        ' Chamado uma vez, logo apos todos os fragmentos terminarem com sucesso e antes da
        ' juncao final em disco, para o chamador poder exibir algo como "unindo fragmentos...".
        Private ReadOnly _onMergeStarting As Action

        Public Property LastRunUsedSegmentedMode As Boolean
        Public Property LastRunConnectionsUsed As Integer
        Public Property LastRunFallbackReason As String

        Public Sub New(Optional maxConnections As Integer = DefaultMaxConnections,
                       Optional confirmFallback As Func(Of Task(Of Boolean)) = Nothing,
                       Optional onMergeStarting As Action = Nothing)
            _maxConnections = Math.Max(MinConnections, Math.Min(MaxConnectionsCeiling, maxConnections))
            _confirmFallback = confirmFallback
            _onMergeStarting = onMergeStarting
        End Sub

        Private Async Function ShouldFallbackToSingleConnectionAsync() As Task(Of Boolean)
            If _confirmFallback Is Nothing Then
                Return True
            End If
            Return Await _confirmFallback()
        End Function

        Public Async Function DownloadAsync(url As String,
                                            destinationPath As String,
                                            progress As IProgress(Of Integer),
                                            cancellationToken As CancellationToken) As Task(Of String) _
                                            Implements IDownloadService.DownloadAsync
            LastRunUsedSegmentedMode = False
            LastRunConnectionsUsed = 0
            LastRunFallbackReason = Nothing
            _slowSwaps = 0
            _rebalances = 0

            Dim sourceUrl As String = Nothing
            Dim resolveFailed As Boolean = False
            Try
                sourceUrl = Await ResolveSourceUrlAsync(url, cancellationToken, 0)
            Catch ex As OperationCanceledException
                Throw
            Catch
                ' Falha ao resolver redirecionamento HTML — deixa o download simples tratar tudo do zero.
                resolveFailed = True
            End Try
            If resolveFailed Then
                Return Await _fallback.DownloadAsync(url, destinationPath, progress, cancellationToken)
            End If

            Dim probe As SegmentProbeResult = Nothing
            Dim probeFailed As Boolean = False
            Try
                probe = Await ProbeAsync(sourceUrl, cancellationToken)
            Catch ex As OperationCanceledException
                Throw
            Catch
                probeFailed = True
            End Try
            If probeFailed Then
                LastRunFallbackReason = "FalhaNaSondagem"
                Return Await _fallback.DownloadAsync(url, destinationPath, progress, cancellationToken)
            End If

            Dim resolvedFinalPath As String = destinationPath
            Try
                If probe.Response IsNot Nothing Then
                    resolvedFinalPath = _fallback.ResolveDestinationPath(probe.Response, destinationPath)
                End If
            Finally
                probe.Response?.Dispose()
            End Try

            If Not IsEligibleOrigin(probe.Host) Then
                LastRunFallbackReason = $"OrigemNaoElegivel:{probe.Host}"
                Return Await _fallback.DownloadAsync(url, destinationPath, progress, cancellationToken)
            End If

            If Not probe.SupportsRanges Then
                LastRunFallbackReason = "RangeNaoSuportado"
                Return Await _fallback.DownloadAsync(url, destinationPath, progress, cancellationToken)
            End If

            If probe.ContentLength <= 0 Then
                LastRunFallbackReason = "TamanhoDesconhecido"
                Return Await _fallback.DownloadAsync(url, destinationPath, progress, cancellationToken)
            End If

            If probe.ContentLength < MinSegmentSizeBytes Then
                LastRunFallbackReason = "ArquivoPequeno"
                Return Await _fallback.DownloadAsync(url, destinationPath, progress, cancellationToken)
            End If

            LastRunUsedSegmentedMode = True
            Return Await DownloadSegmentedAsync(sourceUrl, url, destinationPath, resolvedFinalPath,
                                                probe.ContentLength, progress, cancellationToken)
        End Function

        Private Async Function DownloadSegmentedAsync(sourceUrl As String, originalUrl As String, originalDestination As String,
                                                       finalPath As String, contentLength As Long,
                                                       progress As IProgress(Of Integer),
                                                       cancellationToken As CancellationToken) As Task(Of String)
            Dim segmentCount = CInt(Math.Max(MinConnections, Math.Min(_maxConnections, contentLength \ MinSegmentSizeBytes)))
            ' Exposto já aqui (antes de iniciar os pedaços) para que o chamador possa exibir
            ' "N conexões em uso" durante o progresso, não só depois que o download termina.
            LastRunConnectionsUsed = segmentCount
            Dim plan As New SegmentPlan(finalPath, ComputeRanges(contentLength, segmentCount))

            Dim dirPath = Path.GetDirectoryName(finalPath)
            If Not String.IsNullOrWhiteSpace(dirPath) Then
                Directory.CreateDirectory(dirPath)
            End If

            Dim accumulator As New ProgressAccumulator()
            Dim reporterCts As New CancellationTokenSource()
            Dim reporterTask As Task = If(progress IsNot Nothing,
                                          RunProgressReporterAsync(accumulator, contentLength, progress, reporterCts.Token),
                                          Task.CompletedTask)

            Dim chunksCancelled As Boolean = False
            Dim chunksFailed As Boolean = False
            Dim chunkError As Exception = Nothing
            Try
                ' Cada conexao termina o seu trecho e depois assume a metade final do trecho com mais
                ' bytes faltando (redivisao da cauda): sem isso, o ultimo fragmento lento seguia sozinho.
                Dim workers = plan.InitialSegments().Select(Function(segment) RunConnectionAsync(sourceUrl, segment, plan, accumulator, cancellationToken)).ToList()
                Await Task.WhenAll(workers)
            Catch ex As OperationCanceledException
                chunksCancelled = True
            Catch ex As Exception
                chunksFailed = True
                chunkError = ex
            End Try

            reporterCts.Cancel()
            Await SafeAwaitAsync(reporterTask)
            _rebalances = plan.Splits
            Dim partPaths = plan.PartPathsInOrder()

            If chunksCancelled Then
                CleanupParts(partPaths)
                Throw New OperationCanceledException(cancellationToken)
            End If

            If chunksFailed Then
                CleanupParts(partPaths)
                LastRunFallbackReason = $"ChunkFalhou:{chunkError.Message}"
                If Not Await ShouldFallbackToSingleConnectionAsync() Then
                    Throw New OperationCanceledException(cancellationToken)
                End If
                Return Await _fallback.DownloadAsync(originalUrl, originalDestination, progress, cancellationToken)
            End If

            _onMergeStarting?.Invoke()

            Dim mergeCancelled As Boolean = False
            Dim mergeFailed As Boolean = False
            Dim mergeError As Exception = Nothing
            Try
                Await MergePartsAsync(partPaths, finalPath, contentLength, progress, cancellationToken)
            Catch ex As OperationCanceledException
                mergeCancelled = True
            Catch ex As Exception
                mergeFailed = True
                mergeError = ex
            End Try

            If mergeCancelled Then
                CleanupParts(partPaths)
                DeleteBestEffort(finalPath)
                Throw New OperationCanceledException(cancellationToken)
            End If

            If mergeFailed Then
                CleanupParts(partPaths)
                DeleteBestEffort(finalPath)
                LastRunFallbackReason = $"FalhaNoMerge:{mergeError.Message}"
                If Not Await ShouldFallbackToSingleConnectionAsync() Then
                    Throw New OperationCanceledException(cancellationToken)
                End If
                Return Await _fallback.DownloadAsync(originalUrl, originalDestination, progress, cancellationToken)
            End If

            CleanupParts(partPaths)
            progress?.Report(100)
            LastRunFallbackReason = Nothing
            Return finalPath
        End Function

        Private Async Function RunConnectionAsync(url As String, first As Segment, plan As SegmentPlan,
                                                  accumulator As ProgressAccumulator,
                                                  cancellationToken As CancellationToken) As Task
            Dim segment = first
            Do While segment IsNot Nothing
                Await DownloadChunkAsync(url, segment, plan, accumulator, cancellationToken)
                segment = plan.TrySplit(RebalanceMinBytes)
            Loop
        End Function

        Private Async Function DownloadChunkAsync(url As String, segment As Segment, plan As SegmentPlan,
                                                   accumulator As ProgressAccumulator,
                                                   cancellationToken As CancellationToken) As Task
            Dim lastError As Exception = Nothing
            Dim start = segment.Start
            For attempt = 1 To ChunkMaxAttempts
                cancellationToken.ThrowIfCancellationRequested()
                ' Nova tentativa recomeca o trecho; o fim e o atual (pode ter encolhido por redivisao).
                Dim finish = plan.BeginAttempt(segment)
                Dim bytesWrittenThisAttempt As Long = 0
                Dim attemptFailed As Boolean = False
                Try
                    Using request As New HttpRequestMessage(HttpMethod.Get, url)
                        request.Headers.Range = New RangeHeaderValue(start, finish)
                        Using response = Await DownloadService._httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                            If response.StatusCode <> HttpStatusCode.PartialContent Then
                                Throw New IOException($"Servidor nao retornou 206 Partial Content para o segmento (status={CInt(response.StatusCode)}).")
                            End If

                            ' I-07: o trecho devolvido precisa ser exatamente o pedido.
                            Dim contentRange = response.Content.Headers.ContentRange
                            If contentRange Is Nothing OrElse Not contentRange.From.HasValue OrElse Not contentRange.To.HasValue OrElse
                               contentRange.From.Value <> start OrElse contentRange.To.Value <> finish Then
                                Throw New IOException($"Content-Range divergente para o segmento {start}-{finish}: {If(contentRange?.ToString(), "ausente")}.")
                            End If

                            ' Troca de conexao lenta (SharePoint): reabre o restante do fragmento e so troca
                            ' depois de confirmar 206 exatamente nesse trecho.
                            Dim fileUri = response.RequestMessage?.RequestUri
                            Dim monitor = If(SlowConnectionPolicy.AppliesTo(fileUri), New SlowConnectionPolicy(plan.SwapCoordinator), Nothing)
                            Dim swapResponse As HttpResponseMessage = Nothing
                            Dim contentStream = Await response.Content.ReadAsStreamAsync(cancellationToken)
                            Try
                                Using fileStream As New FileStream(segment.PartPath, FileMode.Create, FileAccess.Write, FileShare.None, StreamBufferBytes, True)
                                    Dim buffer(StreamBufferBytes - 1) As Byte
                                    Dim bytesRead As Integer
                                    Do
                                        bytesRead = Await DownloadService.ReadWithIdleTimeoutAsync(contentStream, buffer.AsMemory(0, buffer.Length), cancellationToken)
                                        If bytesRead = 0 Then
                                            Exit Do
                                        End If
                                        ' So grava o que ainda pertence ao trecho: outra conexao pode ter assumido o fim.
                                        Dim take = plan.Accept(segment, bytesRead)
                                        If take > 0 Then
                                            Await fileStream.WriteAsync(buffer.AsMemory(0, take), cancellationToken)
                                            accumulator.Add(take)
                                            bytesWrittenThisAttempt += take
                                        End If
                                        If take < bytesRead Then
                                            Exit Do
                                        End If
                                        Dim nextByte = start + bytesWrittenThisAttempt
                                        Dim currentFinish = plan.CurrentFinish(segment)
                                        If nextByte > currentFinish Then
                                            Exit Do
                                        End If
                                        If monitor IsNot Nothing AndAlso monitor.IsSlow(bytesRead, currentFinish - nextByte + 1) Then
                                            Dim reopened = Await DownloadService.TryReopenFromAsync(fileUri, nextByte, currentFinish, cancellationToken)
                                            If reopened.Response IsNot Nothing Then
                                                contentStream.Dispose()
                                                swapResponse?.Dispose()
                                                swapResponse = reopened.Response
                                                contentStream = Await swapResponse.Content.ReadAsStreamAsync(cancellationToken)
                                                monitor.RecordSwap()
                                                Interlocked.Increment(_slowSwaps)
                                            ElseIf reopened.RetryAfter.HasValue Then
                                                monitor.Throttled(reopened.RetryAfter.Value)
                                            Else
                                                monitor.GiveUp()
                                            End If
                                        End If
                                    Loop
                                End Using
                            Finally
                                contentStream.Dispose()
                                swapResponse?.Dispose()
                            End Try
                        End Using
                    End Using
                    ' I-07: o segmento so conta se chegou inteiro (ate o fim atual do trecho).
                    Dim expectedBytes = plan.CurrentFinish(segment) - start + 1
                    If bytesWrittenThisAttempt <> expectedBytes Then
                        Throw New IOException($"Segmento {start}-{start + expectedBytes - 1} incompleto: {bytesWrittenThisAttempt} de {expectedBytes} bytes.")
                    End If
                    plan.Complete(segment)
                    Return
                Catch ex As OperationCanceledException When cancellationToken.IsCancellationRequested
                    accumulator.Add(-bytesWrittenThisAttempt)
                    Throw
                Catch ex As Exception
                    accumulator.Add(-bytesWrittenThisAttempt)
                    lastError = ex
                    attemptFailed = True
                End Try

                If attemptFailed AndAlso attempt < ChunkMaxAttempts Then
                    Await Task.Delay(ChunkRetryBaseDelayMs * CInt(Math.Pow(2, attempt - 1)), cancellationToken)
                End If
            Next

            Throw New IOException($"Falha ao baixar segmento apos {ChunkMaxAttempts} tentativas.", lastError)
        End Function

        Private Async Function RunProgressReporterAsync(accumulator As ProgressAccumulator, contentLength As Long,
                                                         progress As IProgress(Of Integer),
                                                         cancellationToken As CancellationToken) As Task
            Try
                Do
                    Await Task.Delay(ProgressReportMinIntervalMs, cancellationToken)
                    Dim percent = CInt(Math.Min(100L, accumulator.GetTotal() * 100L \ contentLength))
                    progress.Report(Math.Max(0, percent))
                Loop
            Catch ex As OperationCanceledException
                ' Encerramento normal do laço de progresso quando o download termina/cancela.
            End Try
        End Function

        Private Shared Async Function SafeAwaitAsync(task As Task) As Task
            Try
                Await task
            Catch
                ' Tarefa de progresso é auxiliar — falha/cancelamento dela não deve mascarar o resultado real.
            End Try
        End Function

        Private Async Function MergePartsAsync(partPaths As IReadOnlyList(Of String), finalPath As String,
                                               expectedLength As Long, progress As IProgress(Of Integer),
                                               cancellationToken As CancellationToken) As Task
            Dim totalWritten As Long = 0
            Dim lastReportedPercent As Integer = -1

            Using destStream As New FileStream(finalPath, FileMode.Create, FileAccess.Write, FileShare.None, StreamBufferBytes, True)
                For Each partPath In partPaths
                    Using partStream = File.OpenRead(partPath)
                        Dim buffer(StreamBufferBytes - 1) As Byte
                        Dim bytesRead As Integer
                        Do
                            bytesRead = Await partStream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken)
                            If bytesRead = 0 Then
                                Exit Do
                            End If
                            Await destStream.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken)
                            totalWritten += bytesRead

                            If progress IsNot Nothing AndAlso expectedLength > 0 Then
                                Dim percent = CInt(Math.Min(100L, totalWritten * 100L \ expectedLength))
                                If percent <> lastReportedPercent Then
                                    lastReportedPercent = percent
                                    progress.Report(percent)
                                End If
                            End If
                        Loop
                    End Using
                Next
            End Using

            Dim actualLength = New FileInfo(finalPath).Length
            If actualLength <> expectedLength Then
                Throw New IOException($"Tamanho final ({actualLength}) nao confere com o esperado ({expectedLength}) apos o merge dos segmentos.")
            End If
        End Function

        Private Function ComputeRanges(totalLength As Long, segmentCount As Integer) As (Start As Long, Finish As Long)()
            Dim ranges((segmentCount - 1)) As (Start As Long, Finish As Long)
            Dim baseSize = totalLength \ segmentCount
            Dim start As Long = 0
            For i = 0 To segmentCount - 1
                Dim endPos As Long = If(i = segmentCount - 1, totalLength - 1, start + baseSize - 1)
                ranges(i) = (start, endPos)
                start = endPos + 1
            Next
            Return ranges
        End Function

        Private Shared Sub CleanupParts(partPaths As IEnumerable(Of String))
            For Each p In partPaths
                DeleteBestEffort(p)
            Next
        End Sub

        Private Shared Sub DeleteBestEffort(path As String)
            Try
                If File.Exists(path) Then
                    File.Delete(path)
                End If
            Catch
                ' Limpeza de arquivo temporário é best-effort; não deve mascarar o resultado real do download.
            End Try
        End Sub

        Friend Shared Function IsEligibleOrigin(host As String) As Boolean
            If String.IsNullOrWhiteSpace(host) Then
                Return False
            End If

            For Each suffix In EligibleHostSuffixes
                If host.EndsWith(suffix, StringComparison.OrdinalIgnoreCase) Then
                    Return True
                End If
            Next

            Return False
        End Function

        Private Async Function ResolveSourceUrlAsync(url As String, cancellationToken As CancellationToken, depth As Integer) As Task(Of String)
            If depth > MaxRedirectDepth Then
                Throw New InvalidOperationException("Numero maximo de redirecionamentos excedido.")
            End If

            Using response = Await DownloadService._httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                response.EnsureSuccessStatusCode()

                Dim mediaType = response.Content.Headers.ContentType?.MediaType
                If Not String.IsNullOrWhiteSpace(mediaType) AndAlso mediaType.StartsWith("text/html", StringComparison.OrdinalIgnoreCase) Then
                    Dim html = Await response.Content.ReadAsStringAsync(cancellationToken)
                    Dim redirectedUrl = _fallback.TryExtractRedirectUrl(html)
                    If Not String.IsNullOrWhiteSpace(redirectedUrl) Then
                        Return Await ResolveSourceUrlAsync(redirectedUrl, cancellationToken, depth + 1)
                    End If

                    Throw New InvalidOperationException("O link retornou pagina HTML sem URL de destino.")
                End If

                ' Devolve o link tirado da pagina (ex.: www.dropbox.com/...?dl=1), nao o destino dos
                ' redirecionamentos HTTP: o link final do Dropbox (uc...dl.dropboxusercontent.com) e de uso
                ' unico (206 na 1a requisicao, 403 nas seguintes - bancada NetSpeedTest, 16/10/2026), o que
                ' derrubava a sondagem ("RangeNaoSuportado") e os fragmentos. Cada requisicao segue os
                ' redirecionamentos e recebe um link final proprio; o host elegivel vem da sondagem.
                Return url
            End Using
        End Function

        Private Async Function ProbeAsync(url As String, cancellationToken As CancellationToken) As Task(Of SegmentProbeResult)
            Dim headResponse As HttpResponseMessage = Nothing
            Dim headFailed As Boolean = False
            Try
                Using headRequest As New HttpRequestMessage(HttpMethod.Head, url)
                    headResponse = Await DownloadService._httpClient.SendAsync(headRequest, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                End Using
            Catch ex As OperationCanceledException
                Throw
            Catch
                headFailed = True
            End Try
            If headFailed Then
                headResponse = Nothing
            End If

            If headResponse IsNot Nothing Then
                If headResponse.IsSuccessStatusCode AndAlso Not IsHtmlResponse(headResponse) Then
                    Dim supportsRangesHead = headResponse.Headers.AcceptRanges IsNot Nothing AndAlso headResponse.Headers.AcceptRanges.Contains("bytes")
                    Dim contentLengthHead = If(headResponse.Content.Headers.ContentLength, -1L)
                    If supportsRangesHead AndAlso contentLengthHead > 0 Then
                        Return New SegmentProbeResult With {
                            .ContentLength = contentLengthHead,
                            .SupportsRanges = True,
                            .Host = headResponse.RequestMessage?.RequestUri?.Host,
                            .Response = headResponse
                        }
                    End If
                End If

                headResponse.Dispose()
            End If

            ' HEAD indisponível/inconclusivo — confirma com uma requisição Range real.
            Dim rangeRequest As New HttpRequestMessage(HttpMethod.Get, url)
            rangeRequest.Headers.Range = New RangeHeaderValue(0, 0)
            Dim rangeResponse = Await DownloadService._httpClient.SendAsync(rangeRequest, HttpCompletionOption.ResponseHeadersRead, cancellationToken)

            If IsHtmlResponse(rangeResponse) Then
                Dim htmlHost = rangeResponse.RequestMessage?.RequestUri?.Host
                rangeResponse.Dispose()
                Return New SegmentProbeResult With {.ContentLength = -1, .SupportsRanges = False, .Host = htmlHost, .Response = Nothing}
            End If

            Dim supportsRanges = rangeResponse.StatusCode = HttpStatusCode.PartialContent
            Dim total As Long = -1
            If supportsRanges AndAlso rangeResponse.Content.Headers.ContentRange IsNot Nothing AndAlso
               rangeResponse.Content.Headers.ContentRange.Length.HasValue Then
                total = rangeResponse.Content.Headers.ContentRange.Length.Value
            ElseIf rangeResponse.Content.Headers.ContentLength.HasValue Then
                total = rangeResponse.Content.Headers.ContentLength.Value
            End If

            Return New SegmentProbeResult With {
                .ContentLength = total,
                .SupportsRanges = supportsRanges,
                .Host = rangeResponse.RequestMessage?.RequestUri?.Host,
                .Response = rangeResponse
            }
        End Function

        Private Shared Function IsHtmlResponse(response As HttpResponseMessage) As Boolean
            Dim mediaType = response.Content.Headers.ContentType?.MediaType
            Return Not String.IsNullOrWhiteSpace(mediaType) AndAlso mediaType.StartsWith("text/html", StringComparison.OrdinalIgnoreCase)
        End Function

''' <summary>Trecho do arquivo baixado por uma conexao. O fim pode encolher quando outra conexao assume a metade final.</summary>
        Friend NotInheritable Class Segment
            Public ReadOnly Start As Long
            Public ReadOnly PartPath As String
            Public Finish As Long
            Public Position As Long
            Public Done As Boolean

            Public Sub New(start As Long, finish As Long, partPath As String)
                Me.Start = start
                Me.Finish = finish
                Me.Position = start
                Me.PartPath = partPath
            End Sub
        End Class

        ''' <summary>Trechos do download e a redivisao, tudo sob a mesma trava.</summary>
        Friend NotInheritable Class SegmentPlan
            Private ReadOnly _gate As New Object()
            Private ReadOnly _segments As New List(Of Segment)()
            Private ReadOnly _basePath As String
            Private _splits As Integer

            ''' <summary>Limite das trocas de conexao lenta deste download (uma por segundo; pausa em Retry-After).</summary>
            Public ReadOnly Property SwapCoordinator As New SlowSwapCoordinator()

            Public Sub New(basePath As String, ranges As (Start As Long, Finish As Long)())
                _basePath = basePath
                For Each range In ranges
                    _segments.Add(New Segment(range.Start, range.Finish, $"{basePath}.part{_segments.Count}"))
                Next
            End Sub

            Public ReadOnly Property Splits As Integer
                Get
                    SyncLock _gate
                        Return _splits
                    End SyncLock
                End Get
            End Property

            Public Function InitialSegments() As List(Of Segment)
                SyncLock _gate
                    Return _segments.ToList()
                End SyncLock
            End Function

            ''' <summary>Arquivos .partN na ordem do arquivo (para a juncao).</summary>
            Public Function PartPathsInOrder() As List(Of String)
                SyncLock _gate
                    Return _segments.OrderBy(Function(segment) segment.Start).Select(Function(segment) segment.PartPath).ToList()
                End SyncLock
            End Function

            ''' <summary>Recomeca o trecho e devolve o fim atual.</summary>
            Public Function BeginAttempt(segment As Segment) As Long
                SyncLock _gate
                    segment.Position = segment.Start
                    Return segment.Finish
                End SyncLock
            End Function

            ''' <summary>Quantos dos bytes lidos ainda cabem no trecho; avanca a posicao.</summary>
            Public Function Accept(segment As Segment, count As Integer) As Integer
                SyncLock _gate
                    Dim room = segment.Finish - segment.Position + 1
                    Dim take = CInt(Math.Max(0L, Math.Min(CLng(count), room)))
                    segment.Position += take
                    Return take
                End SyncLock
            End Function

            Public Function CurrentFinish(segment As Segment) As Long
                SyncLock _gate
                    Return segment.Finish
                End SyncLock
            End Function

            Public Sub Complete(segment As Segment)
                SyncLock _gate
                    segment.Done = True
                End SyncLock
            End Sub

            ''' <summary>
            ''' Divide o trecho com mais bytes faltando: devolve a metade final como trecho novo, para a
            ''' conexao livre. Nothing quando nenhum trecho tem pelo menos 2 x minBytes faltando.
            ''' </summary>
            Public Function TrySplit(minBytes As Long) As Segment
                SyncLock _gate
                    Dim victim = _segments.Where(Function(segment) Not segment.Done).OrderByDescending(Function(segment) segment.Finish - segment.Position).FirstOrDefault()
                    If victim Is Nothing Then Return Nothing
                    Dim remaining = victim.Finish - victim.Position + 1
                    If remaining < 2 * minBytes Then Return Nothing
                    Dim newStart = victim.Position + remaining \ 2
                    Dim stolen As New Segment(newStart, victim.Finish, $"{_basePath}.part{_segments.Count}")
                    victim.Finish = newStart - 1
                    _segments.Add(stolen)
                    _splits += 1
                    Return stolen
                End SyncLock
            End Function
        End Class

        Private NotInheritable Class SegmentProbeResult
            Public Property ContentLength As Long
            Public Property SupportsRanges As Boolean
            Public Property Host As String
            Public Property Response As HttpResponseMessage
        End Class

        Private NotInheritable Class ProgressAccumulator
            Private _total As Long

            Public Sub Add(bytes As Long)
                Interlocked.Add(_total, bytes)
            End Sub

            Public Function GetTotal() As Long
                Return Interlocked.Read(_total)
            End Function
        End Class
    End Class
End Namespace

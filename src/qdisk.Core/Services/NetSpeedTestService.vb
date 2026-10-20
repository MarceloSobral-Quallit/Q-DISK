' ─────────────────────────────────────────────────────────────────────────────
' qdisk  |  Core / Services  |  NetSpeedTestService.vb
' Teste de velocidade por tempo fixo, com N conexões, sem gravar em disco. Mesmo caminho
' do download real: atalho quallit.link -> página de redirecionamento -> link do arquivo.
' Vindo da bancada tools/Quallit.NetSpeedTest.
'
' © Quallit — Licenciado sob GPL-3.0-or-later.
' ─────────────────────────────────────────────────────────────────────────────

Imports System.Diagnostics
Imports System.Net
Imports System.Net.Http
Imports System.Net.Http.Headers
Imports System.Text.Json
Imports System.Text.RegularExpressions
Imports System.Threading
Imports qdisk.Core.Models

Namespace Services
    Public Class NetSpeedTestService
        Private Const UserAgent As String = "QuallitDownloader/1.0"
        ''' <summary>Segundos iniciais fora da média (abertura das conexões e aceleração do TCP).</summary>
        Public Const WarmupSeconds As Integer = 2

        ' Cookies compartilhados: o SharePoint pode exigir o cookie recebido no redirecionamento.
        Private ReadOnly _cookies As New CookieContainer()
        Private ReadOnly _client As HttpClient

        Public Sub New()
            _client = CreateClient()
        End Sub

        ' Cliente novo por medição: uma conexão já aquecida não favorece a medição seguinte.
        Private Function CreateClient() As HttpClient
            Dim handler As New SocketsHttpHandler() With {
                .AllowAutoRedirect = True,
                .AutomaticDecompression = DecompressionMethods.None,
                .MaxConnectionsPerServer = 32,
                .UseCookies = True,
                .CookieContainer = _cookies
            }
            Dim client = New HttpClient(handler) With {.Timeout = Timeout.InfiniteTimeSpan}
            HttpIdentity.Apply(client.DefaultRequestHeaders, UserAgent)
            Return client
        End Function

        ''' <summary>Itens da origem, do maior para o menor (arquivo grande não acaba antes do fim da medição).</summary>
        Public Shared Function ItemsForOrigin(items As IEnumerable(Of CatalogItem), origin As DownloadOrigin) As List(Of CatalogItem)
            Return items.Where(Function(i) ClassifyOrigin(i) = origin).OrderByDescending(Function(i) i.ImageSizeBytes).ToList()
        End Function

        ''' <summary>Origem pelo nome do item (sufixo -dropbox/-db, -sharepoint/-sp) ou pelo host do link.</summary>
        Public Shared Function ClassifyOrigin(item As CatalogItem) As DownloadOrigin
            Dim name = If(item?.Name, String.Empty).ToLowerInvariant()
            Dim url = If(item?.FileUrl, String.Empty).ToLowerInvariant()
            If name.EndsWith("dropbox") OrElse name.EndsWith("-db") OrElse url.EndsWith("-db") OrElse url.Contains("dropbox") Then Return DownloadOrigin.Dropbox
            If name.EndsWith("sharepoint") OrElse name.EndsWith("-sp") OrElse url.EndsWith("-sp") OrElse url.Contains("sharepoint") Then Return DownloadOrigin.SharePoint
            Return DownloadOrigin.Outra
        End Function

        ''' <summary>
        ''' Resolve o link do catálogo (atalho + página de redirecionamento).
        ''' SourceUrl: endereço tirado da página (ex.: www.dropbox.com/...?dl=1), antes dos redirecionamentos HTTP.
        ''' FinalUrl: endereço depois dos redirecionamentos (ex.: uc...dl.dropboxusercontent.com), o que o app usa hoje.
        ''' </summary>
        Public Async Function ResolveLinkAsync(url As String, cancellationToken As CancellationToken) As Task(Of (SourceUrl As String, FinalUrl As String))
            Dim current = url
            For depth = 0 To 3
                Using response = Await SendWithRetryAsync(_client, current, Nothing, Nothing, cancellationToken)
                    response.EnsureSuccessStatusCode()
                    Dim mediaType = response.Content.Headers.ContentType?.MediaType
                    If Not String.IsNullOrWhiteSpace(mediaType) AndAlso mediaType.StartsWith("text/html", StringComparison.OrdinalIgnoreCase) Then
                        Dim html = Await response.Content.ReadAsStringAsync(cancellationToken)
                        Dim redirect = TryExtractRedirectUrl(html)
                        If String.IsNullOrWhiteSpace(redirect) Then Throw New InvalidOperationException("O link retornou página HTML sem endereço do arquivo.")
                        current = redirect
                        Continue For
                    End If
                    Return (current, If(response.RequestMessage?.RequestUri?.AbsoluteUri, current))
                End Using
            Next
            Throw New InvalidOperationException("Redirecionamentos demais.")
        End Function

        ''' <summary>
        ''' Lê o arquivo durante 'duration' com 'connections' conexões e devolve a medição (nada é gravado).
        ''' 'fileUrl' pode ser o SourceUrl (cada requisição segue os redirecionamentos) ou o FinalUrl.
        ''' Com mais de uma conexão, cada uma pede o arquivo a partir de um ponto diferente (Range "a partir de");
        ''' os trechos podem se sobrepor no fim, o que não importa: só a vazão é medida.
        ''' </summary>
        ''' <param name="swapSlowConnections">
        ''' Troca de conexao lenta com a mesma regra do download (<see cref="SlowConnectionPolicy"/>):
        ''' conexao lenta e fechada e reaberta do mesmo ponto, com espera crescente entre trocas seguidas
        ''' e no maximo uma troca por segundo na medicao. O SharePoint sorteia por conexao um caminho
        ''' lento (~0,4 MiB/s) ou rapido (8 a 19 MiB/s); reabrir e um novo sorteio.
        ''' </param>
        Public Async Function MeasureAsync(item As CatalogItem, fileUrl As String, connections As Integer, duration As TimeSpan,
                                           live As IProgress(Of LiveReading), cancellationToken As CancellationToken,
                                           Optional swapSlowConnections As Boolean = False) As Task(Of SpeedMeasurement)
            Dim result As New SpeedMeasurement() With {
                .Origin = ClassifyOrigin(item), .ItemName = item.Name, .FileName = item.FileName,
                .Connections = connections, .TestedAt = DateTimeOffset.Now, .Host = SafeHost(fileUrl)
            }
            Using client = CreateClient()
                Try
                    Dim offsets As Long() = {0L}
                    If connections > 1 Then
                        Dim total = Await ProbeLengthWithRangeAsync(client, fileUrl, cancellationToken)
                        If total <= 0 Then
                            result.Status = "Range não suportado"
                            Return result
                        End If
                        offsets = Enumerable.Range(0, connections).Select(Function(i) CLng(total * (i / CDbl(connections)))).ToArray()
                    End If

                    Dim seconds = CInt(Math.Ceiling(duration.TotalSeconds))
                    Dim buckets(seconds) As Long
                    Dim perConnection(offsets.Length - 1) As Long
                    Dim swaps(offsets.Length - 1) As Integer
                    Dim coordinator As New SlowSwapCoordinator()
                    Dim firstByte As Long = -1
                    Dim clock = Stopwatch.StartNew()
                    Dim tasks As Task()
                    Dim timedOut As Boolean
                    Using deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken)
                        deadline.CancelAfter(duration)
                        tasks = offsets.Select(Function(offset, index) ReadConnectionAsync(client, fileUrl, offset, connections > 1, index, clock, buckets, perConnection,
                                                                                        Sub(ms, host)
                                                                                            If Interlocked.CompareExchange(firstByte, ms, -1L) = -1L Then result.Host = host
                                                                                        End Sub, swapSlowConnections, swaps, coordinator, deadline.Token)).ToArray()
                        Dim reading = Task.WhenAll(tasks)
                        Await ReportLiveAsync(reading, clock, duration, buckets, perConnection, swaps, live)
                        Try
                            Await reading
                        Catch ex As Exception
                            ' falhas avaliadas abaixo, conexão por conexão
                        End Try
                        ' O próprio limite de tempo diz se a leitura foi até o fim (o relógio pode parar ms antes)
                        timedOut = deadline.IsCancellationRequested
                    End Using
                    clock.Stop()
                    cancellationToken.ThrowIfCancellationRequested()

                    Dim failures = tasks.Where(Function(t) t.IsFaulted).Select(Function(t) t.Exception.GetBaseException().Message).ToList()
                    result.TotalBytes = perConnection.Sum()
                    If result.TotalBytes = 0 Then
                        result.Status = "Falha: " & If(failures.FirstOrDefault(), "nenhum byte recebido.")
                        Return result
                    End If

                    ' Leitura parou antes do tempo (arquivo acabou ou conexões caíram): só conta os segundos completos.
                    Dim endedEarly = Not timedOut AndAlso failures.Count = 0
                    Dim usable = If(Not timedOut,Math.Max(1, CInt(Math.Floor(clock.Elapsed.TotalSeconds))), seconds)
                    result.Seconds = Math.Min(clock.Elapsed.TotalSeconds, duration.TotalSeconds)
                    result.FirstByteMs = Math.Max(0, firstByte)
                    result.ActiveConnections = perConnection.Count(Function(b) b >= 1048576L)
                    Dim perSecond = buckets.Take(usable).Select(Function(b) b / 1048576.0R).ToArray()
                    result.PerSecondMiBps = perSecond
                    Dim steady = perSecond.Skip(Math.Min(WarmupSeconds, perSecond.Length - 1)).ToArray()
                    result.MiBps = Math.Round(steady.Average(), 2)
                    result.MinMiBps = Math.Round(steady.Min(), 2)
                    result.MaxMiBps = Math.Round(steady.Max(), 2)
                    result.Status = "OK"
                    Dim notes As New List(Of String)()
                    If endedEarly Then notes.Add($"Arquivo terminou em {clock.Elapsed.TotalSeconds:N0} s.")
                    If failures.Count > 0 Then notes.Add($"{failures.Count} conexão(ões) caíram: {failures(0)}")
                    result.SwapSlowEnabled = swapSlowConnections
                    result.SlowSwaps = swaps.Sum()
                    If result.SlowSwaps > 0 Then notes.Add($"{result.SlowSwaps} troca(s) de conexão lenta.")
                    result.Notes = String.Join(" ", notes)
                Catch ex As OperationCanceledException When cancellationToken.IsCancellationRequested
                    Throw
                Catch ex As Exception
                    result.Status = "Falha: " & ex.Message
                End Try
            End Using
            Return result
        End Function

        ' A cada 250 ms: vazão do último segundo (janela deslizante) e média até agora.
        Private Shared Async Function ReportLiveAsync(reading As Task, clock As Stopwatch, duration As TimeSpan, buckets As Long(),
                                                      perConnection As Long(), swaps As Integer(), live As IProgress(Of LiveReading)) As Task
            Dim history As New Queue(Of (Ms As Long, Bytes As Long))()
            Do
                Dim finished = (Await Task.WhenAny(reading, Task.Delay(250))) Is reading
                If live IsNot Nothing Then
                    Dim ms = clock.ElapsedMilliseconds
                    Dim total = perConnection.Sum()
                    history.Enqueue((ms, total))
                    While history.Count > 1 AndAlso ms - history.Peek().Ms > 1000
                        history.Dequeue()
                    End While
                    Dim oldest = history.Peek()
                    Dim windowSeconds = Math.Max(1L, ms - oldest.Ms) / 1000.0R
                    Dim completed = CInt(Math.Min(buckets.Length - 1, ms \ 1000))
                    live.Report(New LiveReading() With {
                        .Elapsed = TimeSpan.FromMilliseconds(Math.Min(ms, CLng(duration.TotalMilliseconds))),
                        .Duration = duration,
                        .CurrentMiBps = If(history.Count > 1, (total - oldest.Bytes) / 1048576.0R / windowSeconds, 0),
                        .AverageMiBps = If(ms > 0, total / 1048576.0R / (ms / 1000.0R), 0),
                        .TotalBytes = total,
                        .ActiveConnections = perConnection.Count(Function(b) b >= 1048576L),
                        .SlowSwaps = swaps.Sum(),
                        .PerSecondMiBps = buckets.Take(completed).Select(Function(b) b / 1048576.0R).ToArray()
                    })
                End If
                If finished Then Exit Do
            Loop
        End Function

        Private Shared Async Function ReadConnectionAsync(client As HttpClient, url As String, offset As Long, useRange As Boolean, index As Integer, clock As Stopwatch,
                                                          buckets As Long(), perConnection As Long(), onFirstByte As Action(Of Long, String),
                                                          swapSlow As Boolean, swaps As Integer(), coordinator As SlowSwapCoordinator,
                                                          token As CancellationToken) As Task
            Try
                ' Com troca, sempre pede "a partir de" para reabrir no ponto onde parou.
                Dim position = offset
                Dim ranged = useRange OrElse swapSlow
                ' O monitor vale para a conexao inteira (espera crescente entre trocas seguidas).
                Dim monitor = If(swapSlow, New SlowConnectionPolicy(coordinator), Nothing)
                Do
                    Dim swapNow = False
                    Using response = Await SendWithRetryAsync(client, url, If(ranged, CType(position, Long?), Nothing), Nothing, token)
                        ' Host de quem respondeu de fato (depois dos redirecionamentos)
                        onFirstByte(clock.ElapsedMilliseconds, If(response.RequestMessage?.RequestUri?.Host, String.Empty))
                        If ranged AndAlso response.StatusCode <> HttpStatusCode.PartialContent Then
                            Throw New InvalidOperationException($"conexão {index + 1}: servidor respondeu {CInt(response.StatusCode)} em vez de 206.")
                        End If
                        response.EnsureSuccessStatusCode()
                        Using stream = Await response.Content.ReadAsStreamAsync(token)
                            Dim buffer(131071) As Byte
                            monitor?.RestartWindow()
                            Do
                                Dim read = Await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), token)
                                If read = 0 Then Exit Do
                                Dim slot = CInt(Math.Min(buckets.Length - 1, clock.ElapsedMilliseconds \ 1000))
                                Interlocked.Add(buckets(slot), read)
                                Interlocked.Add(perConnection(index), read)
                                position += read
                                ' Tamanho do arquivo nao importa aqui: a medicao para pelo tempo.
                                If monitor IsNot Nothing AndAlso monitor.IsSlow(read, Long.MaxValue) Then
                                    swapNow = True
                                    Exit Do
                                End If
                            Loop
                        End Using
                    End Using
                    If Not swapNow Then Exit Do
                    ' Fechar a resposta no meio descarta a conexao TCP; a proxima requisicao abre outra (novo sorteio).
                    monitor.RecordSwap()
                    Interlocked.Increment(swaps(index))
                Loop
            Catch ex As Exception When token.IsCancellationRequested
                ' fim do tempo de medição (o cancelamento pelo usuário é tratado por quem chamou)
            End Try
        End Function

        ''' <summary>Tamanho do arquivo se o servidor aceitar Range (206 com Content-Range); senão, -1.</summary>
        Private Shared Async Function ProbeLengthWithRangeAsync(client As HttpClient, url As String, cancellationToken As CancellationToken) As Task(Of Long)
            Using response = Await SendWithRetryAsync(client, url, 0L, 0L, cancellationToken)
                If response.StatusCode = HttpStatusCode.PartialContent AndAlso response.Content.Headers.ContentRange?.Length.HasValue Then
                    Return response.Content.Headers.ContentRange.Length.Value
                End If
                Return -1
            End Using
        End Function

        Friend Const RequestMaxAttempts As Integer = 3

        ''' <summary>
        ''' GET (cabeçalhos) com nova tentativa quando o servidor pede para esperar: 429 ou 503.
        ''' O SharePoint responde 429 com Retry-After: 5 depois de muitas requisições seguidas e entrega
        ''' normalmente na tentativa seguinte. Espera o Retry-After (até maxWait) ou 2 s, 4 s.
        ''' </summary>
        Friend Shared Async Function SendWithRetryAsync(client As HttpClient, url As String, rangeFrom As Long?, rangeTo As Long?,
                                                        cancellationToken As CancellationToken, Optional maxWait As TimeSpan? = Nothing) As Task(Of HttpResponseMessage)
            Dim limit = If(maxWait, TimeSpan.FromSeconds(10))
            For attempt = 1 To RequestMaxAttempts
                Dim request As New HttpRequestMessage(HttpMethod.Get, url)
                If rangeFrom.HasValue Then request.Headers.Range = New RangeHeaderValue(rangeFrom, rangeTo)
                Dim response = Await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                Dim code = CInt(response.StatusCode)
                If (code <> 429 AndAlso code <> 503) OrElse attempt = RequestMaxAttempts Then Return response
                Dim wait = If(response.Headers.RetryAfter?.Delta, TimeSpan.FromSeconds(2 * attempt))
                If wait > limit Then wait = limit
                response.Dispose()
                Await Task.Delay(wait, cancellationToken)
            Next
            Throw New InvalidOperationException("Inalcançável.")
        End Function

        Private Shared Function SafeHost(url As String) As String
            Dim uri As Uri = Nothing
            Return If(Uri.TryCreate(url, UriKind.Absolute, uri), uri.Host, String.Empty)
        End Function

        ' Mesmas expressões do teste de velocidade anterior (RepositorySpeedTestService, removido). O
        ' DownloadService usa uma variação: sem aspas tipográficas e href só para arquivo (.iso, .tibx...).
        Private Shared Function TryExtractRedirectUrl(html As String) As String
            If String.IsNullOrWhiteSpace(html) Then Return String.Empty
            Dim patterns = New String() {
                "redirect\(['""“”](?<url>[^'""“”]+)['""“”]\)",
                "window\.location\.replace\(['""“”](?<url>[^'""“”]+)['""“”]\)",
                "href=['""“”](?<url>https?://[^'""“”]+)['""“”]"
            }
            For Each pattern In patterns
                Dim match = Regex.Match(html, pattern, RegexOptions.IgnoreCase)
                If Not match.Success Then Continue For
                Dim value = match.Groups("url").Value
                Dim candidates As New List(Of String) From {value}
                Try
                    Dim decoded = JsonSerializer.Deserialize(Of String)("""" & value & """")
                    If Not String.IsNullOrWhiteSpace(decoded) Then candidates.Insert(0, decoded)
                Catch
                End Try
                For Each candidate In candidates
                    Dim uri As Uri = Nothing
                    If Uri.TryCreate(candidate.Trim(), UriKind.Absolute, uri) AndAlso uri.Scheme = Uri.UriSchemeHttps Then Return candidate.Trim()
                Next
            Next
            Return String.Empty
        End Function
    End Class
End Namespace

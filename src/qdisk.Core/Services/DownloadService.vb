' ─────────────────────────────────────────────────────────────────────────────
' qdisk  |  Core / Services  |  DownloadService.vb
' Download assíncrono com suporte a redirecionamentos HTTP e páginas HTML intermediárias.
'
' © Quallit — Licenciado sob GPL-3.0-or-later.
' ─────────────────────────────────────────────────────────────────────────────

Imports System.IO
Imports System.Net
Imports System.Net.Http
Imports System.Net.Http.Headers
Imports System.Text.Json
Imports System.Text.RegularExpressions
Imports System.Threading
Imports System.Threading.Tasks
Imports qdisk.Core.Interfaces

Namespace Services
    Public Class DownloadService
        Implements IDownloadService

        ' HttpClient compartilhado — evita esgotamento de sockets (TIME_WAIT) por instâncias descartadas.
        ' Friend: reaproveitado por SegmentedDownloadService para manter TLS/proxy/UserAgent idênticos.
        Friend Shared ReadOnly _handler As New HttpClientHandler() With {
            .AutomaticDecompression = DecompressionMethods.GZip Or DecompressionMethods.Deflate
        }
        Friend Shared ReadOnly _httpClient As New HttpClient(_handler) With {
            .Timeout = TimeSpan.FromMinutes(30)
        }

        Shared Sub New()
            HttpIdentity.Apply(_httpClient.DefaultRequestHeaders, "QuallitDownloader/1.0")
        End Sub

        ''' <summary>
        ''' I-06: prazo maximo sem receber nenhum byte. O Timeout do HttpClient nao cobre a leitura
        ''' do corpo quando se usa ResponseHeadersRead; sem este prazo, uma conexao travada esperava para sempre.
        ''' </summary>
        Friend Shared ReadOnly ReadIdleTimeout As TimeSpan = TimeSpan.FromSeconds(60)

        ''' <summary>
        ''' Tentativas da requisicao inicial (antes de gravar qualquer byte). O mateweb respondeu 404 uma vez
        ''' para um arquivo que existia e foi baixado 12 minutos depois (teste real de 16/10/2026).
        ''' </summary>
        Friend Const RequestMaxAttempts As Integer = 3
        Friend Shared ReadOnly RequestRetryDelays As TimeSpan() = {TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(15)}

        ''' <summary>Motivos das novas tentativas do ultimo download (vazio quando a primeira deu certo).</summary>
        Public ReadOnly Property LastRunRequestRetries As New List(Of String)

        ''' <summary>Trocas de conexao lenta do ultimo download (SharePoint; ver SlowConnectionPolicy).</summary>
        Public Property LastRunSlowSwaps As Integer

        ''' <summary>
        ''' Abre o arquivo a partir de 'from' (ate 'to', se informado) e so devolve a resposta se ela for
        ''' 206 comecando exatamente em 'from'. Qualquer outra coisa: Nothing, e quem chamou segue com a atual.
        ''' </summary>
        Friend Shared Async Function TryReopenFromAsync(fileUri As Uri, from As Long, [to] As Long?,
                                                        cancellationToken As CancellationToken) As Task(Of ReopenResult)
            If fileUri Is Nothing Then Return New ReopenResult(Nothing, Nothing)
            Try
                Dim request As New HttpRequestMessage(HttpMethod.Get, fileUri)
                request.Headers.Range = New RangeHeaderValue(from, [to])
                Dim response = Await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                Dim range = response.Content.Headers.ContentRange
                If response.StatusCode = HttpStatusCode.PartialContent AndAlso range IsNot Nothing AndAlso
                   range.From.HasValue AndAlso range.From.Value = from AndAlso
                   (Not [to].HasValue OrElse (range.To.HasValue AndAlso range.To.Value = [to].Value)) Then
                    Return New ReopenResult(response, Nothing)
                End If
                ' 429/503: o servidor pediu para esperar; quem chamou pausa as trocas pelo Retry-After.
                Dim retryAfter As TimeSpan? = Nothing
                If CInt(response.StatusCode) = 429 OrElse CInt(response.StatusCode) = 503 Then
                    retryAfter = If(response.Headers.RetryAfter?.Delta, TimeSpan.FromSeconds(10))
                End If
                response.Dispose()
                Return New ReopenResult(Nothing, retryAfter)
            Catch ex As Exception When Not cancellationToken.IsCancellationRequested
            End Try
            Return New ReopenResult(Nothing, Nothing)
        End Function

        ''' <summary>Resposta da reabertura (Nothing se nao serviu) e, se o servidor pediu, quanto esperar.</summary>
        Friend NotInheritable Class ReopenResult
            Public ReadOnly Response As HttpResponseMessage
            Public ReadOnly RetryAfter As TimeSpan?
            Public Sub New(response As HttpResponseMessage, retryAfter As TimeSpan?)
                Me.Response = response
                Me.RetryAfter = retryAfter
            End Sub
        End Class

        ''' <summary>Respostas que costumam ser passageiras e merecem nova tentativa.</summary>
        Friend Shared Function IsTransientStatus(status As HttpStatusCode) As Boolean
            Select Case CInt(status)
                Case 404, 408, 429, 500, 502, 503, 504
                    Return True
                Case Else
                    Return False
            End Select
        End Function

        ''' <summary>
        ''' GET com ate RequestMaxAttempts tentativas em erro passageiro (status de IsTransientStatus ou falha de rede).
        ''' Devolve a ultima resposta, mesmo de erro, para o chamador decidir; anota os motivos em 'retries'.
        ''' </summary>
        Friend Shared Async Function GetWithRetryAsync(client As HttpClient, url As String, retries As List(Of String),
                                                      cancellationToken As CancellationToken,
                                                      Optional delays As TimeSpan() = Nothing) As Task(Of HttpResponseMessage)
            Dim waits = If(delays, RequestRetryDelays)
            For attempt = 1 To RequestMaxAttempts
                Dim isLast = attempt = RequestMaxAttempts
                Try
                    Dim response = Await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                    If response.IsSuccessStatusCode OrElse isLast OrElse Not IsTransientStatus(response.StatusCode) Then
                        Return response
                    End If
                    retries?.Add($"{CInt(response.StatusCode)} {response.ReasonPhrase}")
                    response.Dispose()
                Catch ex As HttpRequestException When Not isLast
                    retries?.Add(ex.Message)
                End Try
                Await Task.Delay(waits(Math.Min(attempt - 1, waits.Length - 1)), cancellationToken)
            Next
            Throw New InvalidOperationException("Inalcancavel: a ultima tentativa sempre devolve ou lanca.")
        End Function

        Friend Shared Async Function ReadWithIdleTimeoutAsync(stream As Stream, buffer As Memory(Of Byte), cancellationToken As CancellationToken,
                                                              Optional idleTimeout As TimeSpan? = Nothing) As Task(Of Integer)
            Dim limit = If(idleTimeout, ReadIdleTimeout)
            Using idle = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken)
                idle.CancelAfter(limit)
                Try
                    Return Await stream.ReadAsync(buffer, idle.Token)
                Catch ex As OperationCanceledException When Not cancellationToken.IsCancellationRequested
                    Throw New TimeoutException($"Nenhum dado recebido em {limit.TotalSeconds:N0} s; a conexao parece travada.", ex)
                End Try
            End Using
        End Function

        Public Function DownloadAsync(url As String,
                                      destinationPath As String,
                                      progress As IProgress(Of Integer),
                                      cancellationToken As CancellationToken) As Task(Of String) Implements IDownloadService.DownloadAsync
            LastRunRequestRetries.Clear()
            LastRunSlowSwaps = 0
            Return DownloadInternalAsync(url, destinationPath, progress, cancellationToken, 0)
        End Function

        Private Async Function DownloadInternalAsync(url As String,
                                                     destinationPath As String,
                                                     progress As IProgress(Of Integer),
                                                     cancellationToken As CancellationToken,
                                                     depth As Integer) As Task(Of String)
            If depth > 3 Then
                Throw New InvalidOperationException("Numero maximo de redirecionamentos excedido.")
            End If

            If String.IsNullOrWhiteSpace(url) Then
                Throw New ArgumentException("URL invalida.", NameOf(url))
            End If

            If String.IsNullOrWhiteSpace(destinationPath) Then
                Throw New ArgumentException("Destino invalido.", NameOf(destinationPath))
            End If

            Dim dirPath = Path.GetDirectoryName(destinationPath)
            If Not String.IsNullOrWhiteSpace(dirPath) Then
                IO.Directory.CreateDirectory(dirPath)
            End If

            Using response = Await GetWithRetryAsync(_httpClient, url, LastRunRequestRetries, cancellationToken)
                response.EnsureSuccessStatusCode()

                Dim mediaType = response.Content.Headers.ContentType?.MediaType
                If Not String.IsNullOrWhiteSpace(mediaType) AndAlso mediaType.StartsWith("text/html", StringComparison.OrdinalIgnoreCase) Then
                    Dim html = Await response.Content.ReadAsStringAsync(cancellationToken)
                    Dim redirectedUrl = TryExtractRedirectUrl(html)
                    If Not String.IsNullOrWhiteSpace(redirectedUrl) Then
                        Return Await DownloadInternalAsync(redirectedUrl, destinationPath, progress, cancellationToken, depth + 1)
                    End If

                    Throw New InvalidOperationException("O link retornou pagina HTML sem URL de destino.")
                End If

                Dim total = If(response.Content.Headers.ContentLength, -1L)
                Dim canReport = total > 0 AndAlso progress IsNot Nothing

                Dim finalPath = ResolveDestinationPath(response, destinationPath)
                Dim finalDirPath = Path.GetDirectoryName(finalPath)
                If Not String.IsNullOrWhiteSpace(finalDirPath) Then
                    IO.Directory.CreateDirectory(finalDirPath)
                End If

                ' Troca de conexao lenta (SharePoint): a conexao nova so substitui a atual depois de
                ' confirmar 206 a partir do byte seguinte; senao a atual continua.
                Dim fileUri = response.RequestMessage?.RequestUri
                Dim monitor = If(total > 0 AndAlso SlowConnectionPolicy.AppliesTo(fileUri), New SlowConnectionPolicy(New SlowSwapCoordinator()), Nothing)
                Dim swapResponse As HttpResponseMessage = Nothing
                Dim contentStream = Await response.Content.ReadAsStreamAsync(cancellationToken)
                Try
                    Using fileStream As New FileStream(finalPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, True)
                        Dim buffer(81919) As Byte
                        Dim totalRead As Long = 0
                        Dim bytesRead As Integer

                        Do
                            bytesRead = Await ReadWithIdleTimeoutAsync(contentStream, buffer.AsMemory(0, buffer.Length), cancellationToken)
                            If bytesRead = 0 Then
                                Exit Do
                            End If

                            Await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken)
                            totalRead += bytesRead

                            If canReport Then
                                Dim percent = CInt(Math.Min(100, (totalRead * 100L) \ total))
                                progress.Report(percent)
                            End If

                            If monitor IsNot Nothing AndAlso totalRead < total AndAlso monitor.IsSlow(bytesRead, total - totalRead) Then
                                Dim reopened = Await TryReopenFromAsync(fileUri, totalRead, Nothing, cancellationToken)
                                If reopened.Response IsNot Nothing Then
                                    contentStream.Dispose()
                                    swapResponse?.Dispose()
                                    swapResponse = reopened.Response
                                    contentStream = Await swapResponse.Content.ReadAsStreamAsync(cancellationToken)
                                    monitor.RecordSwap()
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
                    LastRunSlowSwaps = If(monitor?.Swaps, 0)
                End Try

                Return finalPath
            End Using
        End Function

        ' Friend: reaproveitado por SegmentedDownloadService para resolver o mesmo nome final de arquivo.
        Friend Function ResolveDestinationPath(response As HttpResponseMessage, destinationPath As String) As String
            Dim fileName = GetFileNameFromResponse(response)
            If String.IsNullOrWhiteSpace(fileName) Then
                fileName = "download.bin"
            End If

            If Directory.Exists(destinationPath) Then
                Return CombineInside(destinationPath, fileName)
            End If

            Dim extension = Path.GetExtension(destinationPath)
            If String.IsNullOrWhiteSpace(extension) Then
                Dim dirPath = Path.GetDirectoryName(destinationPath)
                If String.IsNullOrWhiteSpace(dirPath) Then
                    Return fileName
                End If
                Return CombineInside(dirPath, fileName)
            End If

            Return destinationPath
        End Function

        ''' <summary>
        ''' Nome vindo do servidor (Content-Disposition ou URL), ja sanitizado: sem pastas,
        ''' sem caracteres invalidos e sem "." / "..". Nunca usar o valor bruto do header.
        ''' </summary>
        Friend Function GetFileNameFromResponse(response As HttpResponseMessage) As String
            Dim disposition = response.Content.Headers.ContentDisposition
            Dim fileName = disposition?.FileNameStar
            If String.IsNullOrWhiteSpace(fileName) Then
                fileName = disposition?.FileName
            End If
            fileName = fileName?.Trim(""""c)

            If String.IsNullOrWhiteSpace(fileName) Then
                Dim requestUri = response.RequestMessage?.RequestUri
                If requestUri IsNot Nothing Then
                    fileName = Path.GetFileName(requestUri.AbsolutePath)
                End If
            End If

            Return SanitizeFileName(fileName)
        End Function

        ''' <summary>Reduz um nome recebido de fora a um nome de arquivo simples e seguro.</summary>
        Public Shared Function SanitizeFileName(value As String) As String
            If String.IsNullOrWhiteSpace(value) Then
                Return String.Empty
            End If

            ' Descarta qualquer componente de pasta ("..\..\x.exe", "C:\x", "/etc/x").
            Dim name = value.Replace("/"c, "\"c)
            Dim lastSeparator = name.LastIndexOf("\"c)
            If lastSeparator >= 0 Then
                name = name.Substring(lastSeparator + 1)
            End If

            Dim invalid = Path.GetInvalidFileNameChars()
            Dim builder As New System.Text.StringBuilder(name.Length)
            For Each ch In name
                builder.Append(If(Array.IndexOf(invalid, ch) >= 0, "_"c, ch))
            Next

            ' O Windows descarta pontos e espacos no fim do nome.
            name = builder.ToString().Trim().TrimEnd("."c, " "c)
            If name.Length = 0 OrElse name = "." OrElse name = ".." Then
                Return String.Empty
            End If
            Return name
        End Function

        ''' <summary>Combina pasta e nome e garante que o resultado fica dentro da pasta.</summary>
        Private Shared Function CombineInside(dirPath As String, fileName As String) As String
            Dim root = Path.GetFullPath(dirPath).TrimEnd(Path.DirectorySeparatorChar) & Path.DirectorySeparatorChar
            Dim combined = Path.GetFullPath(Path.Combine(root, fileName))
            If Not combined.StartsWith(root, StringComparison.OrdinalIgnoreCase) Then
                Throw New InvalidOperationException($"Nome de arquivo recusado por sair da pasta de destino: {fileName}")
            End If
            Return combined
        End Function

        ' Friend: reaproveitado por SegmentedDownloadService para resolver páginas HTML intermediárias.
        Friend Function TryExtractRedirectUrl(html As String) As String
            If String.IsNullOrWhiteSpace(html) Then
                Return String.Empty
            End If

            Dim patterns = New String() {
                "window\.location\.replace\(['""](?<url>[^'""]+)['""]\)",
                "redirect\(['""](?<url>[^'""]+)['""]\)",
                "href=['""](?<url>https?://[^'""]+\.(?:iso|img|zip|exe|tibx|bin)[^'""]*)['""]"
            }

            For Each pattern In patterns
                Dim match = Regex.Match(html, pattern, RegexOptions.IgnoreCase)
                If match.Success Then
                    Dim value = match.Groups("url").Value
                    If Not String.IsNullOrWhiteSpace(value) Then
                        Dim normalizedUrl = NormalizeRedirectUrl(value)
                        If Not String.IsNullOrWhiteSpace(normalizedUrl) Then
                            Return normalizedUrl
                        End If
                    End If
                End If
            Next

            Return String.Empty
        End Function

        Friend Function NormalizeRedirectUrl(value As String) As String
            If String.IsNullOrWhiteSpace(value) Then
                Return String.Empty
            End If

            Dim candidates As New List(Of String) From {value}
            Try
                Dim decoded = JsonSerializer.Deserialize(Of String)("""" & value & """")
                If Not String.IsNullOrWhiteSpace(decoded) Then
                    candidates.Insert(0, decoded)
                End If
            Catch
            End Try

            For Each candidate In candidates
                Dim normalized = candidate.Trim()
                Dim redirectUri As Uri = Nothing
                If Uri.TryCreate(normalized, UriKind.Absolute, redirectUri) AndAlso
                   redirectUri.Scheme = Uri.UriSchemeHttps Then
                    Return normalized
                End If
            Next

            Return String.Empty
        End Function
    End Class
End Namespace


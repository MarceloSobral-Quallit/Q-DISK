' ─────────────────────────────────────────────────────────────────────────────
' qdisk  |  Core / Services  |  RawFileManifestVerifier.vb
' Identifica um USB gravado em RAW/DD pelos arquivos: le o sistema de arquivos da ISO direto do disco (UDF ou
' ISO9660, como o helper do manifesto) e confere cada arquivo com o manifesto logico do catalogo. Funciona sem a ISO
' local e mesmo depois de o Windows alterar o pendrive (assinatura do MBR, escrita no volume FAT/EFI montado): so
' arquivos inteiramente dentro da particao FAT/EFI podem divergir, com aviso.
'
' © Quallit — Licenciado sob GPL-3.0-or-later.
' ─────────────────────────────────────────────────────────────────────────────
Imports System.IO
Imports System.Security.Cryptography
Imports System.Threading
Imports DiscUtils
Imports DiscUtils.Iso9660
Imports DiscUtils.Streams
Imports DiscUtils.Udf
Imports qdisk.Core.Models

Namespace Services
    ''' <summary>
    ''' Leitura de disco fisico (\\.\PhysicalDriveN) so aceita offset e tamanho multiplos do setor; a DiscUtils le em
    ''' posicoes arbitrarias. Este stream le blocos alinhados e entrega o trecho pedido. Somente leitura.
    ''' </summary>
    Public NotInheritable Class AlignedReadStream
        Inherits Stream

        Private ReadOnly _inner As Stream
        Private ReadOnly _length As Long
        Private ReadOnly _block As Integer
        Private ReadOnly _buffer As Byte()
        Private _bufferStart As Long = -1
        Private _bufferCount As Integer
        Private _position As Long

        Public Sub New(inner As Stream, length As Long, Optional blockSize As Integer = 64 * 1024)
            _inner = inner
            _length = length
            _block = blockSize
            _buffer = New Byte(blockSize - 1) {}
        End Sub

        Public Overrides ReadOnly Property CanRead As Boolean = True
        Public Overrides ReadOnly Property CanSeek As Boolean = True
        Public Overrides ReadOnly Property CanWrite As Boolean = False
        Public Overrides ReadOnly Property Length As Long
            Get
                Return _length
            End Get
        End Property

        Public Overrides Property Position As Long
            Get
                Return _position
            End Get
            Set(value As Long)
                _position = value
            End Set
        End Property

        Public Overrides Function Read(buffer As Byte(), offset As Integer, count As Integer) As Integer
            Dim total = 0
            While count > 0 AndAlso _position < _length
                Dim blockStart = (_position \ _block) * _block
                If blockStart <> _bufferStart Then
                    _inner.Seek(blockStart, SeekOrigin.Begin)
                    Dim want = CInt(Math.Min(_block, _length - blockStart))
                    ' Le sempre o bloco inteiro (alinhado); o fim do disco/imagem pode ser menor.
                    Dim got = 0
                    While got < _block
                        Dim n = _inner.Read(_buffer, got, _block - got)
                        If n = 0 Then Exit While
                        got += n
                    End While
                    _bufferStart = blockStart
                    _bufferCount = Math.Min(got, want)
                End If
                Dim inBlock = CInt(_position - _bufferStart)
                If inBlock >= _bufferCount Then Exit While
                Dim take = Math.Min(count, _bufferCount - inBlock)
                Array.Copy(_buffer, inBlock, buffer, offset, take)
                offset += take
                count -= take
                total += take
                _position += take
            End While
            Return total
        End Function

        Public Overrides Function Seek(offset As Long, origin As SeekOrigin) As Long
            Select Case origin
                Case SeekOrigin.Begin : _position = offset
                Case SeekOrigin.Current : _position += offset
                Case SeekOrigin.End : _position = _length + offset
            End Select
            Return _position
        End Function

        Public Overrides Sub Flush()
        End Sub

        Public Overrides Sub SetLength(value As Long)
            Throw New NotSupportedException()
        End Sub

        Public Overrides Sub Write(buffer As Byte(), offset As Integer, count As Integer)
            Throw New NotSupportedException()
        End Sub
    End Class

    Public NotInheritable Class RawFileCheckResult
        Public Property Reader As String
        Public Property Checked As Integer
        Public Property FilesVerified As Integer
        Public Property SizeOnlyVerified As Integer
        Public Property MissingCount As Integer
        Public Property MismatchCount As Integer
        Public Property UnreadableCount As Integer
        ''' <summary>Arquivos diferentes, mas inteiramente dentro da particao FAT/EFI que o Windows monta.</summary>
        Public Property ToleratedFiles As New List(Of String)()
        Public Property Details As New List(Of String)()

        ''' <summary>Todos os arquivos do manifesto presentes e iguais (salvo os tolerados).</summary>
        Public ReadOnly Property Matches As Boolean
            Get
                Return Checked > 0 AndAlso MissingCount = 0 AndAlso MismatchCount = 0 AndAlso UnreadableCount = 0
            End Get
        End Property
    End Class

    Public NotInheritable Class RawFileManifestVerifier
        Public Const MaxDetailsPerKind As Integer = 20

        Private Sub New()
        End Sub

        ''' <summary>
        ''' Sistemas de arquivos de ISO encontrados no disco (UDF e ISO9660/Joliet), cada um com o mapa
        ''' "caminho do manifesto em minusculas" -> caminho na DiscUtils. Quem chama descarta os leitores.
        ''' </summary>
        Public Shared Function OpenIsoFileSystems(disk As Stream) As IReadOnlyList(Of (Name As String, FileSystem As DiscFileSystem, Paths As Dictionary(Of String, String)))
            Dim result As New List(Of (String, DiscFileSystem, Dictionary(Of String, String)))()
            Try
                disk.Seek(0, SeekOrigin.Begin)
                If UdfReader.Detect(disk) Then
                    disk.Seek(0, SeekOrigin.Begin)
                    Dim udf As DiscFileSystem = New UdfReader(disk)
                    result.Add(("UDF", udf, MapPaths(udf)))
                End If
            Catch
            End Try
            Try
                disk.Seek(0, SeekOrigin.Begin)
                If CDReader.Detect(disk) Then
                    disk.Seek(0, SeekOrigin.Begin)
                    Dim iso As DiscFileSystem = New CDReader(disk, True)
                    result.Add(("ISO9660", iso, MapPaths(iso)))
                End If
            Catch
            End Try
            Return result
        End Function

        ''' <summary>Como o helper do manifesto: listagem direta e percurso a partir da raiz (a que achar mais arquivos).</summary>
        Private Shared Function MapPaths(fileSystem As DiscFileSystem) As Dictionary(Of String, String)
            Dim map As New Dictionary(Of String, String)(StringComparer.Ordinal)
            Try
                For Each filePath In fileSystem.GetFiles(String.Empty, "*", SearchOption.AllDirectories)
                    Dim key = NormalizeKey(filePath)
                    If Not map.ContainsKey(key) Then map(key) = filePath
                Next
            Catch
            End Try
            Dim walked As New Dictionary(Of String, String)(StringComparer.Ordinal)
            Try
                WalkDirectory(fileSystem.Root, walked)
            Catch
            End Try
            Return If(walked.Count > map.Count, walked, map)
        End Function

        Private Shared Sub WalkDirectory(directory As DiscDirectoryInfo, map As Dictionary(Of String, String))
            For Each file In directory.GetFiles()
                Dim key = NormalizeKey(file.FullName)
                If Not map.ContainsKey(key) Then map(key) = file.FullName
            Next
            For Each child In directory.GetDirectories()
                WalkDirectory(child, map)
            Next
        End Sub

        ''' <summary>Caminho do manifesto ("a/b.efi") ou da DiscUtils ("a\b.efi;1") como chave de busca.</summary>
        Public Shared Function NormalizeKey(path As String) As String
            Dim key = If(path, String.Empty).Replace("\"c, "/"c).Trim("/"c)
            Dim version = key.LastIndexOf(";"c)
            If version > 0 AndAlso key.Substring(version + 1).All(AddressOf Char.IsDigit) Then key = key.Substring(0, version)
            ' ISO9660 grava arquivo sem extensao com ponto final ("BOOTMGR.;1").
            If key.EndsWith("."c) Then key = key.TrimEnd("."c)
            Return key.ToLowerInvariant()
        End Function

        ''' <summary>True se todas as sentinelas existem no sistema de arquivos com o tamanho e o SHA-256 esperados.</summary>
        Public Shared Function SentinelsMatch(fileSystem As DiscFileSystem, paths As Dictionary(Of String, String), sentinels As IEnumerable(Of UsbManifestFile)) As Boolean
            Dim list = If(sentinels, Enumerable.Empty(Of UsbManifestFile)()).ToList()
            If list.Count = 0 Then Return False
            For Each sentinel In list
                If sentinel Is Nothing OrElse String.IsNullOrWhiteSpace(sentinel.Path) OrElse String.IsNullOrWhiteSpace(sentinel.Sha256) Then Return False
                Dim fsPath As String = Nothing
                If Not paths.TryGetValue(NormalizeKey(sentinel.Path), fsPath) Then Return False
                If sentinel.Size.HasValue AndAlso fileSystem.GetFileLength(fsPath) <> sentinel.Size.Value Then Return False
                If Not String.Equals(HashFile(fileSystem, fsPath), sentinel.Sha256.Trim(), StringComparison.OrdinalIgnoreCase) Then Return False
            Next
            Return True
        End Function

        ''' <summary>Confere cada arquivo do manifesto. Divergencia so e tolerada se o arquivo estiver todo dentro de fatRanges.</summary>
        Public Shared Function Verify(fileSystem As DiscFileSystem, paths As Dictionary(Of String, String), readerName As String,
                                      entries As IReadOnlyList(Of UsbManifestEntry),
                                      fatRanges As IReadOnlyList(Of (StartByte As Long, EndByte As Long)),
                                      Optional onProgress As Action(Of RawFileCheckResult) = Nothing,
                                      Optional cancellationToken As CancellationToken = Nothing) As RawFileCheckResult
            Dim result As New RawFileCheckResult With {.Reader = readerName}
            For Each entry In entries
                cancellationToken.ThrowIfCancellationRequested()
                result.Checked += 1
                Dim fsPath As String = Nothing
                If Not paths.TryGetValue(NormalizeKey(entry.RelativePath), fsPath) Then
                    result.MissingCount += 1
                    If result.MissingCount <= MaxDetailsPerKind Then result.Details.Add($"Ausente: {entry.RelativePath}")
                    onProgress?.Invoke(result)
                    Continue For
                End If
                Try
                    If fileSystem.GetFileLength(fsPath) <> entry.SizeBytes Then
                        result.MismatchCount += 1
                        If result.MismatchCount <= MaxDetailsPerKind Then result.Details.Add($"Tamanho divergente: {entry.RelativePath}")
                    ElseIf String.IsNullOrWhiteSpace(entry.Sha256) Then
                        result.SizeOnlyVerified += 1
                    ElseIf String.Equals(HashFile(fileSystem, fsPath), entry.Sha256.Trim(), StringComparison.OrdinalIgnoreCase) Then
                        result.FilesVerified += 1
                    ElseIf IsInsideRanges(fileSystem, fsPath, fatRanges) Then
                        result.ToleratedFiles.Add(entry.RelativePath)
                    Else
                        result.MismatchCount += 1
                        If result.MismatchCount <= MaxDetailsPerKind Then result.Details.Add($"Hash divergente: {entry.RelativePath}")
                    End If
                Catch ex As OperationCanceledException
                    Throw
                Catch ex As Exception
                    result.UnreadableCount += 1
                    If result.UnreadableCount <= MaxDetailsPerKind Then result.Details.Add($"Ilegivel: {entry.RelativePath} ({ex.Message})")
                End Try
                onProgress?.Invoke(result)
            Next
            Return result
        End Function

        ''' <summary>True se todos os trechos do arquivo no disco estao dentro de alguma faixa (particao FAT/EFI).</summary>
        Public Shared Function IsInsideRanges(fileSystem As DiscFileSystem, fsPath As String,
                                              ranges As IReadOnlyList(Of (StartByte As Long, EndByte As Long))) As Boolean
            If ranges Is Nothing OrElse ranges.Count = 0 Then Return False
            Dim clusters = TryCast(fileSystem, IClusterBasedFileSystem)
            If clusters Is Nothing Then Return False
            Dim extents = clusters.PathToExtents(fsPath)
            If extents Is Nothing OrElse Not extents.Any() Then Return False
            Return extents.All(Function(extent) ranges.Any(Function(range) extent.Start >= range.StartByte AndAlso extent.Start + extent.Length <= range.EndByte))
        End Function

        Private Shared Function HashFile(fileSystem As DiscFileSystem, fsPath As String) As String
            Using stream = fileSystem.OpenFile(fsPath, FileMode.Open, FileAccess.Read)
                Return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant()
            End Using
        End Function
    End Class
End Namespace

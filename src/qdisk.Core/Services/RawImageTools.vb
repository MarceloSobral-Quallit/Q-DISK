' ─────────────────────────────────────────────────────────────────────────────
' qdisk  |  Core / Services  |  RawImageTools.vb
' Gravacao RAW/DD e particao auxiliar UEFI:NTFS sem dependencia do Windows (P10.1, etapa 2 da separacao da
' UsbBootView): ordem da gravacao, comparacao imagem x disco e escrita/verificacao da regiao UEFI:NTFS sobre Stream.
' Abrir o disco fisico, travar volumes e repetir escritas continua na tela.
'
' © Quallit — Licenciado sob GPL-3.0-or-later.
' ─────────────────────────────────────────────────────────────────────────────
Imports System.IO
Imports System.Threading

Namespace Services
    Public NotInheritable Class RawWriteRange
        Public Property StartOffset As Long
        Public Property Length As Long
        Public Property Name As String
    End Class

    Public NotInheritable Class RawWritePlan
        Public Const HeaderBytes As Integer = 1024 * 1024

        Private Sub New()
        End Sub

        ''' <summary>
        ''' Corpo primeiro e o primeiro 1 MiB (MBR/GPT e tabela de particoes) por ultimo: o Windows so reconhece as
        ''' particoes da imagem quando a gravacao ja terminou.
        ''' </summary>
        Public Shared Function Ranges(imageSize As Long) As IReadOnlyList(Of RawWriteRange)
            Dim result As New List(Of RawWriteRange)()
            Dim headerLength = Math.Min(CLng(HeaderBytes), Math.Max(0L, imageSize))
            If imageSize > headerLength Then
                result.Add(New RawWriteRange With {.StartOffset = headerLength, .Length = imageSize - headerLength, .Name = "body"})
            End If
            If headerLength > 0 Then
                result.Add(New RawWriteRange With {.StartOffset = 0L, .Length = headerLength, .Name = "header"})
            End If
            Return result
        End Function
    End Class

    Public NotInheritable Class RawImageVerifier
        Public Const BufferSize As Integer = 1024 * 1024

        Private Sub New()
        End Sub

        ''' <summary>
        ''' Compara os primeiros <paramref name="length"/> bytes da imagem com o disco e classifica cada diferenca
        ''' (assinatura do MBR, volume FAT/EFI da imagem, divergencia real). Leitura curta do disco conta como divergencia.
        ''' </summary>
        Public Shared Async Function CompareAsync(image As Stream, device As Stream, length As Long,
                                                  Optional onDivergence As Action(Of Long, Byte, Byte) = Nothing,
                                                  Optional onProgress As Action(Of Long) = Nothing,
                                                  Optional cancellationToken As CancellationToken = Nothing) As Task(Of RawCompareResult)
            Dim result As New RawCompareResult()
            Dim imageBuffer(BufferSize - 1) As Byte
            Dim deviceBuffer(BufferSize - 1) As Byte
            Dim fatRanges As IReadOnlyList(Of (StartByte As Long, EndByte As Long)) = Nothing

            While result.BytesCompared < length
                cancellationToken.ThrowIfCancellationRequested()
                Dim toRead = CInt(Math.Min(BufferSize, length - result.BytesCompared))
                Dim imageRead = Await ReadFullAsync(image, imageBuffer, toRead, cancellationToken)
                If imageRead = 0 Then Exit While
                Dim deviceRead = Await ReadFullAsync(device, deviceBuffer, imageRead, cancellationToken)
                If deviceRead <> imageRead Then
                    result.OtherDifferences += 1
                    Return result
                End If
                If fatRanges Is Nothing Then
                    fatRanges = RawImageCompare.FatPartitionRanges(imageBuffer.Take(512).ToArray())
                End If
                For i = 0 To imageRead - 1
                    If imageBuffer(i) <> deviceBuffer(i) Then
                        RawImageCompare.Classify(result, result.BytesCompared + i, imageBuffer(i), fatRanges)
                        onDivergence?.Invoke(result.BytesCompared + i, imageBuffer(i), deviceBuffer(i))
                    End If
                Next
                result.BytesCompared += imageRead
                onProgress?.Invoke(result.BytesCompared)
            End While
            Return result
        End Function

        Friend Shared Async Function ReadFullAsync(stream As Stream, buffer As Byte(), count As Integer, cancellationToken As CancellationToken) As Task(Of Integer)
            Dim total = 0
            While total < count
                Dim read = Await stream.ReadAsync(buffer.AsMemory(total, count - total), cancellationToken)
                If read = 0 Then Exit While
                total += read
            End While
            Return total
        End Function
    End Class

    ''' <summary>Particao auxiliar do UEFI:NTFS (2a entrada do MBR, tipo 0xEF) com a imagem UEFI:NTFS do Rufus.</summary>
    Public NotInheritable Class UefiNtfsAuxPartition
        Public Const EntryIndex As Integer = 1
        Private Const TableOffset As Integer = 446
        Private Const EntrySize As Integer = 16

        Private Sub New()
        End Sub

        ''' <summary>A entrada 2 do MBR, se existe e cabe a imagem; senao IOException com o motivo.</summary>
        Public Shared Function ValidateTarget(mbr As Byte(), imageLength As Long) As MbrPartitionEntry
            If Not MbrLayout.HasBootSignature(mbr) Then
                Throw New IOException("MBR do USB nao encontrado depois de particionar.")
            End If
            Dim entry = MbrLayout.ReadPartitions(mbr)(EntryIndex)
            Dim length = CLng(entry.SectorCount) * 512L
            If entry.StartLba = 0UI OrElse length < imageLength Then
                Throw New IOException($"Particao auxiliar UEFI:NTFS ausente ou menor que a imagem ({length} bytes).")
            End If
            Return entry
        End Function

        ''' <summary>Garante o tipo 0xEF na entrada 2 (alguns diskpart ignoram o "set id"). True se alterou o MBR.</summary>
        Public Shared Function EnsureEfiType(mbr As Byte()) As Boolean
            Dim typeOffset = TableOffset + EntryIndex * EntrySize + 4
            If mbr(typeOffset) = MbrLayout.EfiSystemPartitionType Then Return False
            mbr(typeOffset) = MbrLayout.EfiSystemPartitionType
            Return True
        End Function

        ''' <summary>Grava a imagem no inicio da particao e, se preciso, o MBR com o tipo 0xEF corrigido.</summary>
        Public Shared Async Function WriteAsync(device As Stream, mbr As Byte(), image As Byte(),
                                                Optional cancellationToken As CancellationToken = Nothing) As Task(Of (Offset As Long, TypeFixed As Boolean))
            Dim entry = ValidateTarget(mbr, image.Length)
            device.Seek(entry.StartOffsetBytes, SeekOrigin.Begin)
            Await device.WriteAsync(image.AsMemory(0, image.Length), cancellationToken)
            Dim typeFixed = EnsureEfiType(mbr)
            If typeFixed Then
                device.Seek(0, SeekOrigin.Begin)
                Await device.WriteAsync(mbr.AsMemory(0, 512), cancellationToken)
            End If
            device.Flush()
            Return (entry.StartOffsetBytes, typeFixed)
        End Function

        ''' <summary>Particoes 0xEF do MBR que podem ter a imagem UEFI:NTFS.</summary>
        Public Shared Function Candidates(mbr As Byte()) As IEnumerable(Of MbrPartitionEntry)
            Return MbrLayout.ReadPartitions(mbr).Where(Function(entry) entry.PartitionType = MbrLayout.EfiSystemPartitionType AndAlso entry.StartLba > 0UI)
        End Function

        ''' <summary>True se o disco tem, a partir de offset, exatamente os bytes da imagem.</summary>
        Public Shared Function RegionMatches(device As Stream, offset As Long, image As Byte()) As Boolean
            Dim buffer(image.Length - 1) As Byte
            device.Seek(offset, SeekOrigin.Begin)
            Dim total = 0
            While total < buffer.Length
                Dim read = device.Read(buffer, total, buffer.Length - total)
                If read = 0 Then Exit While
                total += read
            End While
            Return total = buffer.Length AndAlso buffer.AsSpan().SequenceEqual(image)
        End Function
    End Class
End Namespace

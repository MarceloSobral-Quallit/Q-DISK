' ─────────────────────────────────────────────────────────────────────────────
' qdisk  |  Core / Services  |  UsbDiskLayout.vb
' Logica pura de disco USB, sem tela nem E/S (P10.1, etapa 1 da separacao da UsbBootView):
' leitura do MBR, scripts do diskpart e decisao de formatar antes de copiar TIBX.
'
' © Quallit — Licenciado sob GPL-3.0-or-later.
' ─────────────────────────────────────────────────────────────────────────────

Namespace Services
    Public NotInheritable Class MbrPartitionEntry
        Public Property Index As Integer
        Public Property IsActive As Boolean
        Public Property PartitionType As Byte
        Public Property StartLba As UInteger
        Public Property SectorCount As UInteger

        Public ReadOnly Property StartOffsetBytes As Long
            Get
                Return CLng(StartLba) * 512L
            End Get
        End Property
    End Class

    ''' <summary>Leitura do MBR (setor 0) e de setores de boot.</summary>
    Public NotInheritable Class MbrLayout
        Private Const TableOffset As Integer = 446
        Private Const EntrySize As Integer = 16
        Public Const EfiSystemPartitionType As Byte = &HEF

        Private Sub New()
        End Sub

        Public Shared Function HasBootSignature(sector As Byte()) As Boolean
            Return sector IsNot Nothing AndAlso sector.Length >= 512 AndAlso sector(510) = &H55 AndAlso sector(511) = &HAA
        End Function

        ''' <summary>As 4 entradas da tabela de particoes (vazia se o setor nao tiver assinatura 55 AA).</summary>
        Public Shared Function ReadPartitions(mbr As Byte()) As IReadOnlyList(Of MbrPartitionEntry)
            Dim entries As New List(Of MbrPartitionEntry)()
            If Not HasBootSignature(mbr) Then Return entries
            For index = 0 To 3
                Dim offset = TableOffset + index * EntrySize
                entries.Add(New MbrPartitionEntry With {
                    .Index = index,
                    .IsActive = mbr(offset) = &H80,
                    .PartitionType = mbr(offset + 4),
                    .StartLba = BitConverter.ToUInt32(mbr, offset + 8),
                    .SectorCount = BitConverter.ToUInt32(mbr, offset + 12)
                })
            Next
            Return entries
        End Function

        Public Shared Function GetActivePartitionIndex(mbr As Byte()) As Integer
            Dim active = ReadPartitions(mbr).FirstOrDefault(Function(entry) entry.IsActive)
            Return If(active Is Nothing, -1, active.Index)
        End Function

        Public Shared Function GetPartitionType(mbr As Byte(), index As Integer) As Integer
            Dim entry = ReadPartitions(mbr).FirstOrDefault(Function(item) item.Index = index)
            Return If(entry Is Nothing, 0, CInt(entry.PartitionType))
        End Function

        Public Shared Function GetPartitionStartLba(mbr As Byte(), index As Integer) As UInteger
            Dim entry = ReadPartitions(mbr).FirstOrDefault(Function(item) item.Index = index)
            Return If(entry Is Nothing, 0UI, entry.StartLba)
        End Function

        Public Shared Function SectorContainsAscii(sector As Byte(), value As String) As Boolean
            If sector Is Nothing OrElse String.IsNullOrWhiteSpace(value) Then Return False
            Return System.Text.Encoding.ASCII.GetString(sector).IndexOf(value, StringComparison.OrdinalIgnoreCase) >= 0
        End Function

        ''' <summary>True se algum dos primeiros 440 bytes (codigo de boot do MBR) nao for zero.</summary>
        Public Shared Function HasBootCode(mbr As Byte()) As Boolean
            Return mbr IsNot Nothing AndAlso mbr.Length >= 440 AndAlso mbr.Take(440).Any(Function(value) value <> 0)
        End Function
    End Class

    ''' <summary>Scripts do diskpart usados pelo app (o texto exato, para poder ser testado).</summary>
    Public NotInheritable Class DiskpartScripts
        Private Sub New()
        End Sub

        ''' <summary>Zerar USB / preparar para TIBX: MBR, uma particao primaria formatada, com letra.</summary>
        Public Shared Function ZeroUsb(diskNumber As Integer, fileSystem As String, label As String) As String
            Return Join(
                $"select disk {diskNumber}",
                "online disk noerr",
                "attributes disk clear readonly",
                "clean",
                "convert mbr",
                "create partition primary",
                $"format fs={fileSystem} quick label={label}",
                "assign",
                "exit")
        End Function

        ''' <summary>
        ''' UEFI:NTFS, fase 1: as duas particoes sem formatar (1 = principal, ativa so como precaucao de firmware;
        ''' 2 = auxiliar 0xEF no fim). A imagem UEFI:NTFS e gravada na 2 antes de formatar a 1.
        ''' </summary>
        Public Shared Function UefiNtfsPartitions(diskNumber As Integer, ntfsMegabytes As Long) As String
            Return Join(
                $"select disk {diskNumber}",
                "online disk noerr",
                "attributes disk clear readonly",
                "clean",
                "convert mbr",
                $"create partition primary size={ntfsMegabytes}",
                "active",
                "create partition primary",
                "set id=ef override",
                "exit")
        End Function

        ''' <summary>UEFI:NTFS, fase 2: formata a particao 1 em NTFS e atribui letra.</summary>
        Public Shared Function UefiNtfsFormatMain(diskNumber As Integer, label As String) As String
            Return Join(
                $"select disk {diskNumber}",
                "select partition 1",
                $"format fs=ntfs quick label={label}",
                "assign",
                "exit")
        End Function

        ''' <summary>Tamanho da particao NTFS: ate ~3 MiB do fim, sobrando >= 1 MiB para a imagem UEFI:NTFS.</summary>
        Public Shared Function UefiNtfsMainPartitionMegabytes(diskSizeBytes As Long) As Long
            Return diskSizeBytes \ (1024L * 1024L) - 3L
        End Function

        Private Shared Function Join(ParamArray lines As String()) As String
            Return String.Join(Environment.NewLine, lines) & Environment.NewLine
        End Function
    End Class

    ''' <summary>
    ''' Rotulos dos volumes formatados pelo app: "QDisk_TIBX" para backup TIBX; na copia logica, o rotulo da propria ISO
    ''' (sem rotulo na ISO: "QDisk_" + nome do arquivo). FAT32 aceita 11 caracteres (maiusculas); NTFS aceita 32.
    ''' </summary>
    Public NotInheritable Class UsbVolumeLabels
        Public Const Prefix As String = "QDisk_"
        Public Const Tibx As String = "QDisk_TIBX"
        Public Const Fat32MaxLength As Integer = 11
        Public Const NtfsMaxLength As Integer = 32

        Private Sub New()
        End Sub

        ''' <summary>
        ''' Rotulo da copia logica: o do catalogo (usbWritePolicy.volumeLabel); senao o da ISO; senao prefixo + nome do
        ''' arquivo. So letras, digitos, "_" e "-".
        ''' </summary>
        Public Shared Function ForIsoCopy(catalogLabel As String, isoLabel As String, isoPath As String, fileSystem As String) As String
            Dim label = Clean(catalogLabel)
            If label.Length = 0 Then label = Clean(isoLabel)
            If label.Length = 0 Then
                Dim name = If(String.IsNullOrWhiteSpace(isoPath), String.Empty, System.IO.Path.GetFileNameWithoutExtension(isoPath))
                label = Prefix & Clean(name)
            End If
            If String.Equals(fileSystem, "FAT32", StringComparison.OrdinalIgnoreCase) Then
                label = label.ToUpperInvariant()
                Return If(label.Length > Fat32MaxLength, label.Substring(0, Fat32MaxLength), label)
            End If
            Return If(label.Length > NtfsMaxLength, label.Substring(0, NtfsMaxLength), label)
        End Function

        Private Shared Function Clean(value As String) As String
            Dim text = If(value, String.Empty).Trim()
            Return New String(text.Select(Function(ch) If(Char.IsAsciiLetterOrDigit(ch) OrElse ch = "-"c, ch, "_"c)).ToArray()).Trim("_"c)
        End Function

        ''' <summary>Entrada de rotulo (atributo 0x08) para a raiz do FAT32: e dela que o Windows le o nome do volume.</summary>
        Public Shared Function Fat32RootLabelEntry(label As String) As Byte()
            Dim entry(31) As Byte
            Dim text = If(label, String.Empty).ToUpperInvariant()
            If text.Length > Fat32MaxLength Then text = text.Substring(0, Fat32MaxLength)
            System.Text.Encoding.ASCII.GetBytes(text.PadRight(Fat32MaxLength, " "c)).CopyTo(entry, 0)
            entry(11) = &H8
            Return entry
        End Function
    End Class

    ''' <summary>Resultado da comparacao byte a byte entre a imagem e o USB gravado em RAW/DD.</summary>
    Public NotInheritable Class RawCompareResult
        Public Property BytesCompared As Long
        ''' <summary>Bytes 440-443 do MBR (assinatura do disco): zerados na imagem, o Windows grava uma ao reconhecer o disco.</summary>
        Public Property SignatureDifferences As Integer
        ''' <summary>Dentro de particao FAT/EFI da imagem: o Windows escreve no volume que monta (System Volume Information).</summary>
        Public Property VolumeDifferences As Integer
        Public Property OtherDifferences As Integer

        ''' <summary>Igual a imagem, salvo a assinatura do disco.</summary>
        Public ReadOnly Property MatchesImage As Boolean
            Get
                Return VolumeDifferences = 0 AndAlso OtherDifferences = 0
            End Get
        End Property

        ''' <summary>Igual a imagem fora do volume FAT/EFI, com poucas alteracoes dentro dele (USB montado pelo Windows).</summary>
        Public ReadOnly Property MatchesWithVolumeChanges As Boolean
            Get
                Return OtherDifferences = 0 AndAlso VolumeDifferences <= RawImageCompare.MaxVolumeDifferences
            End Get
        End Property
    End Class

    ''' <summary>Classifica as diferencas entre imagem e USB RAW/DD (o que o Windows altera ao montar x divergencia real).</summary>
    Public NotInheritable Class RawImageCompare
        Public Const MaxVolumeDifferences As Integer = 64 * 1024
        Private Shared ReadOnly FatPartitionTypes As Byte() = {&H1, &H4, &H6, &HB, &HC, &HE, &HEF}

        Private Sub New()
        End Sub

        ''' <summary>Extensoes (inicio, fim exclusivo, em bytes) das particoes FAT/EFI declaradas no MBR da imagem.</summary>
        Public Shared Function FatPartitionRanges(imageMbr As Byte()) As IReadOnlyList(Of (StartByte As Long, EndByte As Long))
            Return MbrLayout.ReadPartitions(imageMbr).
                Where(Function(entry) entry.StartLba > 0UI AndAlso entry.SectorCount > 0UI AndAlso FatPartitionTypes.Contains(entry.PartitionType)).
                Select(Function(entry) (entry.StartOffsetBytes, entry.StartOffsetBytes + CLng(entry.SectorCount) * 512L)).
                ToList()
        End Function

        Public Shared Sub Classify(result As RawCompareResult, offset As Long, imageByte As Byte,
                                   fatRanges As IReadOnlyList(Of (StartByte As Long, EndByte As Long)))
            If offset >= 440 AndAlso offset <= 443 AndAlso imageByte = 0 Then
                result.SignatureDifferences += 1
            ElseIf fatRanges IsNot Nothing AndAlso fatRanges.Any(Function(range) offset >= range.StartByte AndAlso offset < range.EndByte) Then
                result.VolumeDifferences += 1
            Else
                result.OtherDifferences += 1
            End If
        End Sub
    End Class

    ''' <summary>Decisao de formatar o USB (que ja tem letra) antes de copiar um TIBX.</summary>
    Public NotInheritable Class TibxVolumeCheck
        Public Const RequiredFileSystem As String = "NTFS"

        Private Sub New()
        End Sub

        ''' <summary>Motivo para formatar, ou Nothing se o volume serve para receber o TIBX.</summary>
        Public Shared Function FormatReason(isReady As Boolean, fileSystem As String, availableFreeBytes As Long,
                                            volumeTotalBytes As Long, diskSizeBytes As Long, tibxBytes As Long) As String
            If Not isReady Then
                Return "O volume atual do USB nao esta pronto."
            End If
            Dim format = If(fileSystem, String.Empty)
            If format.Equals("CDFS", StringComparison.OrdinalIgnoreCase) OrElse format.Equals("UDF", StringComparison.OrdinalIgnoreCase) Then
                Return $"O USB esta com {format} (imagem gravada em RAW/DD, somente leitura)."
            End If
            If Not format.Equals(RequiredFileSystem, StringComparison.OrdinalIgnoreCase) Then
                Return $"O USB esta em {format}. Para backup TIBX o app usa NTFS: a midia de boot Acronis restaura de NTFS, " &
                       "nao restaura de exFAT, e FAT32 nao aceita arquivo acima de 4 GB."
            End If
            If availableFreeBytes < tibxBytes AndAlso diskSizeBytes > tibxBytes AndAlso volumeTotalBytes < diskSizeBytes * 9L \ 10L Then
                Return "A particao atual ocupa so parte do USB e nao tem espaco para o TIBX (ex.: imagem gravada em RAW/DD)."
            End If
            Return Nothing
        End Function
    End Class
End Namespace

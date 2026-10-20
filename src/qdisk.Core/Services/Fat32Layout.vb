' ─────────────────────────────────────────────────────────────────────────────
' qdisk  |  Core / Services  |  Fat32Layout.vb
' Formatacao FAT32 nativa sem E/S (P10.1, etapa 2 da separacao da UsbBootView): geometria do disco e os setores
' gravados (MBR, boot sector, FSInfo, FAT e diretorio raiz). Portado de DiskFormatter::FormatDrive do rpi-imager;
' a gravacao no disco continua na tela.
'
' © Quallit — Licenciado sob GPL-3.0-or-later.
' ─────────────────────────────────────────────────────────────────────────────

Namespace Services
    ''' <summary>Posicoes e tamanhos da particao FAT32 (em setores de 512 bytes, absolutos no disco).</summary>
    Public NotInheritable Class Fat32Geometry
        Public Property TotalDiskSectors As UInteger
        Public Property PartitionStartSector As UInteger
        Public Property PartitionSizeSectors As UInteger
        Public Property SectorsPerCluster As Integer
        Public Property SectorsPerFat As UInteger
        Public Property TotalClusters As UInteger
        Public Property RootCluster As UInteger

        Public ReadOnly Property Fat1StartSector As Long
            Get
                Return CLng(PartitionStartSector) + Fat32Layout.ReservedSectors
            End Get
        End Property

        Public ReadOnly Property Fat2StartSector As Long
            Get
                Return Fat1StartSector + CLng(SectorsPerFat)
            End Get
        End Property

        Public ReadOnly Property DataAreaStartSector As Long
            Get
                Return Fat1StartSector + Fat32Layout.NumberOfFats * CLng(SectorsPerFat)
            End Get
        End Property

        Public ReadOnly Property RootDirStartSector As Long
            Get
                Return DataAreaStartSector + CLng(RootCluster - 2UI) * SectorsPerCluster
            End Get
        End Property
    End Class

    Public NotInheritable Class Fat32Layout
        Public Const SectorSize As Integer = 512
        ''' <summary>Particao a partir de 4 MiB.</summary>
        Public Const PartitionStartSector As UInteger = 8192UI
        Public Const ReservedSectors As UInteger = 32UI
        Public Const NumberOfFats As UInteger = 2UI
        Public Const FsInfoSector As UShort = 1US
        Public Const BackupBootSector As UShort = 6US
        Private Const Fat32MaxBytesForClusterSize As Long = 32L * 1024 * 1024 * 1024

        Private Sub New()
        End Sub

        ''' <summary>Geometria para um disco do tamanho dado. MBR com setores de 512 bytes enderecam ate 2 TiB.</summary>
        Public Shared Function Plan(diskSizeBytes As Long) As Fat32Geometry
            If diskSizeBytes \ SectorSize > UInteger.MaxValue Then
                Throw New System.IO.IOException($"O disco tem {diskSizeBytes:N0} bytes. A formatacao FAT32 nativa suporta discos de ate 2 TiB.")
            End If
            Dim totalDiskSectors = CUInt(diskSizeBytes \ SectorSize)
            Dim partitionSizeSectors As UInteger = If(totalDiskSectors > PartitionStartSector, totalDiskSectors - PartitionStartSector, 0UI)
            Dim partBytes As Long = Math.Min(CLng(partitionSizeSectors) * SectorSize, Fat32MaxBytesForClusterSize)
            Dim sectorsPerCluster = ClusterSizeBytes(partBytes) \ SectorSize

            Dim geometry As New Fat32Geometry With {
                .TotalDiskSectors = totalDiskSectors,
                .PartitionStartSector = PartitionStartSector,
                .PartitionSizeSectors = partitionSizeSectors,
                .SectorsPerCluster = sectorsPerCluster,
                .RootCluster = 2UI
            }
            ' Iteracao convergente (identica ao rpi-imager).
            Dim dataSectors As UInteger = If(partitionSizeSectors > ReservedSectors, partitionSizeSectors - ReservedSectors, 0UI)
            Dim spf As UInteger = CUInt(Math.Ceiling(CDbl(dataSectors) / (CDbl(sectorsPerCluster) + 4.0 / 128.0 * CDbl(NumberOfFats))))
            For i = 0 To 9
                Dim dataStart = ReservedSectors + NumberOfFats * spf
                If dataStart > partitionSizeSectors Then
                    If spf > 0UI Then spf -= 1UI
                Else
                    Dim clusters As UInteger = (partitionSizeSectors - dataStart) \ CUInt(sectorsPerCluster)
                    spf = CUInt(Math.Ceiling((CDbl(clusters) + 2.0) * 4.0 / 512.0))
                End If
            Next
            geometry.SectorsPerFat = spf
            Dim dataAreaStart = ReservedSectors + NumberOfFats * spf
            geometry.TotalClusters = If(partitionSizeSectors > dataAreaStart, (partitionSizeSectors - dataAreaStart) \ CUInt(sectorsPerCluster), 0UI)
            Return geometry
        End Function

        ''' <summary>Tamanho do cluster pelo tamanho da particao (tabela padrao do Windows para FAT32).</summary>
        Public Shared Function ClusterSizeBytes(partitionBytes As Long) As Integer
            Const MB As Long = 1024L * 1024
            Const GB As Long = 1024L * MB
            If partitionBytes < 32L * MB Then Return 512
            If partitionBytes < 260L * MB Then Return 1024
            If partitionBytes < 8L * GB Then Return 4096
            If partitionBytes < 16L * GB Then Return 8192
            Return 16384
        End Function

        ''' <summary>MBR com uma particao FAT32 LBA (0x0C) ativa a partir de PartitionStartSector.</summary>
        Public Shared Function BuildMbrSector(totalDiskSectors As UInteger) As Byte()
            Dim mbr(SectorSize - 1) As Byte
            Const entry As Integer = 446
            mbr(entry) = &H80
            mbr(entry + 4) = &HC
            Array.Copy(BitConverter.GetBytes(PartitionStartSector), 0, mbr, entry + 8, 4)
            Dim numSectors As UInteger = If(totalDiskSectors > PartitionStartSector, totalDiskSectors - PartitionStartSector, 0UI)
            Array.Copy(BitConverter.GetBytes(numSectors), 0, mbr, entry + 12, 4)
            mbr(510) = &H55
            mbr(511) = &HAA
            Return mbr
        End Function

        ''' <summary>Boot sector FAT32 (BPB + BPB estendido). O rotulo vai em maiusculas, ate 11 caracteres.</summary>
        Public Shared Function BuildBootSector(geometry As Fat32Geometry, volumeId As UInteger, label As String) As Byte()
            Dim buf(SectorSize - 1) As Byte
            buf(0) = &HEB : buf(1) = &H58 : buf(2) = &H90
            Array.Copy(System.Text.Encoding.ASCII.GetBytes("mkfs.fat"), 0, buf, 3, 8)
            Array.Copy(BitConverter.GetBytes(CUShort(SectorSize)), 0, buf, 11, 2)
            buf(13) = CByte(geometry.SectorsPerCluster)
            Array.Copy(BitConverter.GetBytes(CUShort(ReservedSectors)), 0, buf, 14, 2)
            buf(16) = CByte(NumberOfFats)
            buf(21) = &HF8
            Array.Copy(BitConverter.GetBytes(CUShort(63)), 0, buf, 24, 2)
            Array.Copy(BitConverter.GetBytes(CUShort(255)), 0, buf, 26, 2)
            Array.Copy(BitConverter.GetBytes(geometry.PartitionStartSector), 0, buf, 28, 4)
            Array.Copy(BitConverter.GetBytes(geometry.PartitionSizeSectors), 0, buf, 32, 4)
            Array.Copy(BitConverter.GetBytes(geometry.SectorsPerFat), 0, buf, 36, 4)
            Array.Copy(BitConverter.GetBytes(geometry.RootCluster), 0, buf, 44, 4)
            Array.Copy(BitConverter.GetBytes(FsInfoSector), 0, buf, 48, 2)
            Array.Copy(BitConverter.GetBytes(BackupBootSector), 0, buf, 50, 2)
            buf(64) = &H80
            buf(66) = &H29
            Array.Copy(BitConverter.GetBytes(volumeId), 0, buf, 67, 4)
            Dim lbl = If(label, "QUALLIT").ToUpperInvariant()
            If lbl.Length > 11 Then lbl = lbl.Substring(0, 11)
            Array.Copy(System.Text.Encoding.ASCII.GetBytes(lbl.PadRight(11, " "c)), 0, buf, 71, 11)
            Array.Copy(System.Text.Encoding.ASCII.GetBytes("FAT32   "), 0, buf, 82, 8)
            buf(510) = &H55 : buf(511) = &HAA
            Return buf
        End Function

        ''' <summary>Setor FSInfo: clusters livres (todos menos o da raiz) e proximo livre (3).</summary>
        Public Shared Function BuildFsInfo(totalClusters As UInteger) As Byte()
            Dim buf(SectorSize - 1) As Byte
            Array.Copy(BitConverter.GetBytes(&H41615252UI), 0, buf, 0, 4)
            Array.Copy(BitConverter.GetBytes(&H61417272UI), 0, buf, 484, 4)
            Dim freeCount = If(totalClusters > 0UI, totalClusters - 1UI, 0UI)
            Array.Copy(BitConverter.GetBytes(freeCount), 0, buf, 488, 4)
            Array.Copy(BitConverter.GetBytes(3UI), 0, buf, 492, 4)
            Array.Copy(BitConverter.GetBytes(&HAA550000UI), 0, buf, 508, 4)
            Return buf
        End Function

        ''' <summary>Tabela FAT com as entradas 0 e 1 reservadas e a 2 (diretorio raiz) como fim de cadeia.</summary>
        Public Shared Function BuildFatTable(sectorsPerFat As UInteger) As Byte()
            Dim fat(CInt(sectorsPerFat) * SectorSize - 1) As Byte
            Array.Copy(BitConverter.GetBytes(&HFFFFFF8UI), 0, fat, 0, 4)
            Array.Copy(BitConverter.GetBytes(&HFFFFFFFUI), 0, fat, 4, 4)
            Array.Copy(BitConverter.GetBytes(&HFFFFFFFUI), 0, fat, 8, 4)
            Return fat
        End Function

        ''' <summary>Primeiro cluster do diretorio raiz, com a entrada de rotulo (e dela que o Windows le o nome).</summary>
        Public Shared Function BuildRootDirectory(geometry As Fat32Geometry, label As String) As Byte()
            Dim root(geometry.SectorsPerCluster * SectorSize - 1) As Byte
            UsbVolumeLabels.Fat32RootLabelEntry(label).CopyTo(root, 0)
            Return root
        End Function
    End Class
End Namespace

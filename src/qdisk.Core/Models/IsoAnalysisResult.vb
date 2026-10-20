Namespace Models
    Public Enum IsoBootRecommendation
        Unknown
        Bios
        Uefi
        BiosAndUefi
        RawRecommended
        Unsupported
        UefiNtfs
    End Enum

    Public NotInheritable Class ElToritoBootEntry
        Public Property PlatformId As Byte
        Public Property IsBootable As Boolean
        Public Property MediaType As Byte
        Public Property LoadSegment As UShort
        Public Property SystemType As Byte
        Public Property SectorCount As UShort
        Public Property LoadRba As UInteger

        Public ReadOnly Property PlatformName As String
            Get
                Select Case PlatformId
                    Case &H0
                        Return "BIOS/x86"
                    Case &H1
                        Return "PowerPC"
                    Case &H2
                        Return "Mac"
                    Case &HEF
                        Return "UEFI"
                    Case Else
                        Return $"Desconhecida 0x{PlatformId:X2}"
                End Select
            End Get
        End Property
    End Class

    Public NotInheritable Class IsoAnalysisResult
        Public Property ImagePath As String
        Public Property Label As String
        Public Property HasElTorito As Boolean
        Public Property HasBiosElTorito As Boolean
        Public Property HasUefiElTorito As Boolean
        Public Property HasBootMgr As Boolean
        Public Property HasBootSect As Boolean
        Public Property HasEfiBootX64 As Boolean
        Public Property HasEfiBootIa32 As Boolean
        Public Property HasSyslinux As Boolean
        Public Property HasGrub As Boolean
        Public Property HasAcronisBootwiz As Boolean
        Public Property HasFilesOver4Gb As Boolean
        Public Property FilesOver4Gb As New List(Of String)()
        ' Area de sistema da ISO (setores iniciais): indica ISO hibrida, que pode dar boot gravada como RAW/DD.
        Public Property HasMbrSignature As Boolean
        Public Property HasMbrBootCode As Boolean
        Public Property MbrPartitionTypes As New List(Of Byte)()
        Public Property HasGpt As Boolean
        Public Property LargestFilePath As String
        Public Property LargestFileSize As Long
        Public Property TotalFiles As Integer
        Public Property Recommendation As IsoBootRecommendation
        Public Property Reasons As New List(Of String)()
        Public Property ElToritoEntries As New List(Of ElToritoBootEntry)()

        Public ReadOnly Property IsHybridMbr As Boolean
            Get
                Return HasMbrSignature AndAlso HasMbrBootCode AndAlso MbrPartitionTypes.Count > 0
            End Get
        End Property

        Public ReadOnly Property RecommendationText As String
            Get
                Select Case Recommendation
                    Case IsoBootRecommendation.Bios
                        Return "Boot BIOS"
                    Case IsoBootRecommendation.Uefi
                        Return "Boot UEFI"
                    Case IsoBootRecommendation.BiosAndUefi
                        Return "Boot BIOS + UEFI"
                    Case IsoBootRecommendation.RawRecommended
                        Return "Raw/DD recomendado"
                    Case IsoBootRecommendation.Unsupported
                        Return "Nao suportado pelo metodo atual"
                    Case IsoBootRecommendation.UefiNtfs
                        Return "UEFI:NTFS recomendado"
                    Case Else
                        Return "Indefinido"
                End Select
            End Get
        End Property
    End Class
End Namespace
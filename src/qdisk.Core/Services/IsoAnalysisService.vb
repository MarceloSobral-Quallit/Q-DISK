Imports System.IO
Imports DiscUtils.Iso9660
Imports DiscUtils.Udf
Imports qdisk.Core.Models

Namespace Services
    Public NotInheritable Class IsoAnalysisService
        Private Const IsoSectorSize As Integer = 2048
        Private Const Fat32MaxFileBytes As Long = 4L * 1024 * 1024 * 1024 - 1L

        Public Function Analyze(isoPath As String) As IsoAnalysisResult
            If String.IsNullOrWhiteSpace(isoPath) Then
                Throw New ArgumentException("Caminho da ISO nao informado.", NameOf(isoPath))
            End If
            If Not File.Exists(isoPath) Then
                Throw New FileNotFoundException("ISO nao encontrada.", isoPath)
            End If

            Dim result As New IsoAnalysisResult With {.ImagePath = isoPath}

            AnalyzeElTorito(isoPath, result)
            AnalyzeSystemArea(isoPath, result)
            AnalyzeFileTree(isoPath, result)
            ApplyRecommendation(result)

            Return result
        End Function

        ''' <summary>
        ''' Analisa a ISO usando uma lista de arquivos ja lida por outro caminho (o helper de manifesto, que tem
        ''' fallback por montagem do Windows quando o DiscUtils devolve arvore incompleta).
        ''' </summary>
        Public Function Analyze(isoPath As String, files As IEnumerable(Of KeyValuePair(Of String, Long))) As IsoAnalysisResult
            If String.IsNullOrWhiteSpace(isoPath) Then
                Throw New ArgumentException("Caminho da ISO nao informado.", NameOf(isoPath))
            End If
            If Not File.Exists(isoPath) Then
                Throw New FileNotFoundException("ISO nao encontrada.", isoPath)
            End If
            If files Is Nothing Then
                Throw New ArgumentNullException(NameOf(files))
            End If

            Dim result As New IsoAnalysisResult With {.ImagePath = isoPath}

            AnalyzeElTorito(isoPath, result)
            AnalyzeSystemArea(isoPath, result)
            For Each entry In files
                AnalyzeFile(entry.Key, entry.Value, result)
            Next
            ApplyRecommendation(result)

            Return result
        End Function

        ' Le o primeiro setor e o cabecalho GPT. ISO comum tem a area de sistema zerada; ISO hibrida
        ' (isohybrid, xorriso) traz MBR com codigo de boot e tabela de particoes, e as vezes GPT.
        Private Shared Sub AnalyzeSystemArea(isoPath As String, result As IsoAnalysisResult)
            Dim buffer(1023) As Byte
            Using stream As New FileStream(isoPath, FileMode.Open, FileAccess.Read, FileShare.Read)
                Dim total = 0
                While total < buffer.Length
                    Dim read = stream.Read(buffer, total, buffer.Length - total)
                    If read = 0 Then
                        Return
                    End If
                    total += read
                End While
            End Using
            InspectSystemArea(buffer, result)
        End Sub

        Friend Shared Sub InspectSystemArea(buffer As Byte(), result As IsoAnalysisResult)
            If buffer Is Nothing OrElse buffer.Length < 520 Then
                Return
            End If

            result.HasMbrSignature = buffer(510) = &H55 AndAlso buffer(511) = &HAA
            If result.HasMbrSignature Then
                For index = 0 To 439
                    If buffer(index) <> 0 Then
                        result.HasMbrBootCode = True
                        Exit For
                    End If
                Next
                For entryIndex = 0 To 3
                    Dim partitionType = buffer(446 + entryIndex * 16 + 4)
                    If partitionType <> 0 Then
                        result.MbrPartitionTypes.Add(partitionType)
                    End If
                Next
            End If
            result.HasGpt = System.Text.Encoding.ASCII.GetString(buffer, 512, 8) = "EFI PART"
        End Sub

        Private Sub AnalyzeFileTree(isoPath As String, result As IsoAnalysisResult)
            Using isoStream As New FileStream(isoPath, FileMode.Open, FileAccess.Read, FileShare.Read, 4 * 1024 * 1024, useAsync:=False)
                Dim useUdf As Boolean = False
                Try
                    Using testUdf As New UdfReader(isoStream)
                        useUdf = True
                        result.Label = testUdf.VolumeLabel
                    End Using
                    isoStream.Seek(0, SeekOrigin.Begin)
                Catch
                    isoStream.Seek(0, SeekOrigin.Begin)
                End Try

                If useUdf Then
                    Using reader As New UdfReader(isoStream)
                        If String.IsNullOrWhiteSpace(result.Label) Then
                            result.Label = reader.VolumeLabel
                        End If
                        AnalyzeDirectory(reader.Root, String.Empty, result)
                    End Using
                Else
                    Using reader As New CDReader(isoStream, True)
                        result.Label = reader.VolumeLabel
                        AnalyzeDirectory(reader.Root, String.Empty, result)
                    End Using
                End If
            End Using
        End Sub

        Private Sub AnalyzeDirectory(directory As DiscUtils.DiscDirectoryInfo, basePath As String, result As IsoAnalysisResult)
            For Each file As DiscUtils.DiscFileInfo In directory.GetFiles()
                Dim relativePath = If(String.IsNullOrEmpty(basePath), file.Name, Path.Combine(basePath, file.Name))
                AnalyzeFile(relativePath, file.Length, result)
            Next

            For Each child As DiscUtils.DiscDirectoryInfo In directory.GetDirectories()
                Dim childPath = If(String.IsNullOrEmpty(basePath), child.Name, Path.Combine(basePath, child.Name))
                AnalyzeDirectory(child, childPath, result)
            Next
        End Sub

        Private Sub AnalyzeFile(relativePath As String, length As Long, result As IsoAnalysisResult)
            result.TotalFiles += 1
            If length > result.LargestFileSize Then
                result.LargestFileSize = length
                result.LargestFilePath = relativePath
            End If
            If length > Fat32MaxFileBytes Then
                result.HasFilesOver4Gb = True
                result.FilesOver4Gb.Add(relativePath)
            End If

            Dim normalized = NormalizePath(relativePath)
            Dim fileName = Path.GetFileName(normalized)
            Dim directoryName = Path.GetDirectoryName(normalized)
            If directoryName Is Nothing Then directoryName = String.Empty

            Select Case normalized
                Case "BOOTMGR"
                    result.HasBootMgr = True
                Case "BOOT\BOOTSECT.EXE"
                    result.HasBootSect = True
                Case "EFI\BOOT\BOOTX64.EFI"
                    result.HasEfiBootX64 = True
                Case "EFI\BOOT\BOOTIA32.EFI"
                    result.HasEfiBootIa32 = True
            End Select

            If fileName = "ISOLINUX.BIN" OrElse fileName = "LDLINUX.C32" OrElse fileName = "SYSLINUX.CFG" OrElse normalized.Contains("\ISOLINUX\") OrElse normalized.Contains("\SYSLINUX\") Then
                result.HasSyslinux = True
            End If

            If fileName = "GRUB.CFG" OrElse fileName = "GRLDR" OrElse normalized.Contains("\GRUB\") Then
                result.HasGrub = True
            End If

            If fileName.StartsWith("BOOTWIZ", StringComparison.OrdinalIgnoreCase) OrElse normalized.Contains("RECOVERY MANAGER\BOOTWIZ") Then
                result.HasAcronisBootwiz = True
            End If
        End Sub

        Private Shared Function NormalizePath(pathValue As String) As String
            Return If(pathValue, String.Empty).Replace("/"c, "\"c).TrimStart("\"c).ToUpperInvariant()
        End Function

        Private Sub AnalyzeElTorito(isoPath As String, result As IsoAnalysisResult)
            Using stream As New FileStream(isoPath, FileMode.Open, FileAccess.Read, FileShare.Read)
                Dim descriptorIndex As Long = 16
                Dim descriptor(IsoSectorSize - 1) As Byte

                While descriptorIndex < 512
                    ReadSector(stream, descriptorIndex, descriptor)
                    Dim descriptorType = descriptor(0)
                    Dim signature = TextAt(descriptor, 1, 5)
                    If signature <> "CD001" Then
                        Exit While
                    End If

                    If descriptorType = 0 AndAlso TextAt(descriptor, 7, 23).StartsWith("EL TORITO SPECIFICATION", StringComparison.Ordinal) Then
                        Dim catalogLba = ReadUInt32Le(descriptor, 71)
                        If catalogLba > 0UI Then
                            result.HasElTorito = True
                            ReadBootCatalog(stream, catalogLba, result)
                        End If
                        Exit While
                    End If

                    If descriptorType = &HFF Then
                        Exit While
                    End If
                    descriptorIndex += 1
                End While
            End Using
        End Sub

        Private Sub ReadBootCatalog(stream As Stream, catalogLba As UInteger, result As IsoAnalysisResult)
            Dim catalog(IsoSectorSize - 1) As Byte
            ReadSector(stream, catalogLba, catalog)

            If catalog(0) <> &H1 OrElse catalog(30) <> &H55 OrElse catalog(31) <> &HAA Then
                result.Reasons.Add("Catalogo El Torito encontrado, mas Validation Entry invalida.")
                Return
            End If

            Dim currentPlatform As Byte = catalog(1)
            Dim offset = 32
            While offset + 31 < catalog.Length
                Dim entryType = catalog(offset)
                If entryType = 0 Then
                    Exit While
                End If

                If entryType = &H90 OrElse entryType = &H91 Then
                    currentPlatform = catalog(offset + 1)
                    offset += 32
                    Continue While
                End If

                If entryType = &H88 OrElse entryType = &H0 Then
                    Dim entry = ParseBootEntry(catalog, offset, currentPlatform)
                    result.ElToritoEntries.Add(entry)
                    If entry.IsBootable Then
                        If entry.PlatformId = &HEF Then
                            result.HasUefiElTorito = True
                        ElseIf entry.PlatformId = &H0 Then
                            result.HasBiosElTorito = True
                        End If
                    End If
                End If

                offset += 32
            End While
        End Sub

        Private Shared Function ParseBootEntry(buffer As Byte(), offset As Integer, platformId As Byte) As ElToritoBootEntry
            Return New ElToritoBootEntry With {
                .PlatformId = platformId,
                .IsBootable = buffer(offset) = &H88,
                .MediaType = buffer(offset + 1),
                .LoadSegment = ReadUInt16Le(buffer, offset + 2),
                .SystemType = buffer(offset + 4),
                .SectorCount = ReadUInt16Le(buffer, offset + 6),
                .LoadRba = ReadUInt32Le(buffer, offset + 8)
            }
        End Function

        Private Shared Sub ReadSector(stream As Stream, lba As Long, buffer As Byte())
            stream.Seek(lba * IsoSectorSize, SeekOrigin.Begin)
            Dim total = 0
            While total < buffer.Length
                Dim read = stream.Read(buffer, total, buffer.Length - total)
                If read = 0 Then
                    Throw New EndOfStreamException("Fim inesperado ao ler setor da ISO.")
                End If
                total += read
            End While
        End Sub

        Private Shared Function TextAt(buffer As Byte(), offset As Integer, length As Integer) As String
            Return System.Text.Encoding.ASCII.GetString(buffer, offset, length).TrimEnd(ChrW(0), " "c)
        End Function

        ' P-02: em VB.NET, deslocar um Byte mascara o deslocamento em 3 bits (b << 8 = b), entao a
        ' versao antiga ignorava os bytes altos. BinaryPrimitives le os valores corretamente.
        Public Shared Function ReadUInt16Le(buffer As Byte(), offset As Integer) As UShort
            Return System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(buffer.AsSpan(offset, 2))
        End Function

        Public Shared Function ReadUInt32Le(buffer As Byte(), offset As Integer) As UInteger
            Return System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(buffer.AsSpan(offset, 4))
        End Function

        Private Sub ApplyRecommendation(result As IsoAnalysisResult)
            If result.HasElTorito Then
                result.Reasons.Add($"El Torito: BIOS={result.HasBiosElTorito}, UEFI={result.HasUefiElTorito}, entradas={result.ElToritoEntries.Count}.")
            Else
                result.Reasons.Add("El Torito: nao detectado.")
            End If

            If result.HasFilesOver4Gb Then
                result.Reasons.Add($"Arquivo maior que 4 GB: {result.LargestFilePath} ({result.LargestFileSize:N0} bytes).")
            End If
            If result.HasBootMgr Then result.Reasons.Add("BOOTMGR detectado na raiz.")
            If result.HasBootSect Then result.Reasons.Add("boot\\bootsect.exe detectado.")
            If result.HasEfiBootX64 Then result.Reasons.Add("EFI\\BOOT\\BOOTX64.EFI detectado.")
            If result.HasSyslinux Then result.Reasons.Add("Artefatos Syslinux/Isolinux detectados.")
            If result.HasGrub Then result.Reasons.Add("Artefatos GRUB detectados.")
            If result.HasAcronisBootwiz Then result.Reasons.Add("Artefatos Acronis Bootwiz detectados.")

            ' Recomendacao derivada da regra unica de metodo (UsbWriteMethodAdvisor), a mesma do GUI_ADM e da janela de
            ' metodo do app; este enum segue alimentando rotulo da lista, confirmacoes e evidencias.
            Dim assessment = New UsbWriteMethodAdvisor().Assess(result)
            Select Case assessment.RecommendedMethod
                Case UsbWriteMethods.LogicalFat32
                    Dim targets = assessment.FindMethod(UsbWriteMethods.LogicalFat32).BootTargets
                    If targets.Contains("bios") AndAlso targets.Contains("uefi") Then
                        result.Recommendation = IsoBootRecommendation.BiosAndUefi
                        result.Reasons.Add("Recomendacao: criar USB BIOS + UEFI com FAT32 e MBR/PBR BootMgr proprio.")
                    ElseIf targets.Contains("bios") Then
                        result.Recommendation = IsoBootRecommendation.Bios
                        result.Reasons.Add("Recomendacao: criar USB BIOS com FAT32 e MBR/PBR BootMgr proprio.")
                    Else
                        result.Recommendation = IsoBootRecommendation.Uefi
                        result.Reasons.Add("Recomendacao: criar USB UEFI em FAT32.")
                    End If
                Case UsbWriteMethods.Raw
                    result.Recommendation = IsoBootRecommendation.RawRecommended
                    result.Reasons.Add("Recomendacao: Raw/DD (ISO hibrida) para preservar o boot desta ISO.")
                Case UsbWriteMethods.UefiNtfs
                    result.Recommendation = IsoBootRecommendation.UefiNtfs
                    result.Reasons.Add("Recomendacao: UEFI:NTFS (arquivo maior que 4 GB, sem dividir a imagem).")
                Case Else
                    result.Recommendation = IsoBootRecommendation.Unsupported
                    result.Reasons.Add("Recomendacao: ISO nao suportada pelos metodos atuais.")
            End Select
        End Sub
    End Class
End Namespace
Imports qdisk.Core.Models

Namespace Services
    ''' <summary>
    ''' Regra unica de metodo de gravacao USB. Usada pelo helper de manifesto (decisao do QDisk_Adm) e pelo app
    ''' (ISOs fora dos catalogos). Trabalha so com o IsoAnalysisResult; nao le a imagem.
    ''' </summary>
    Public NotInheritable Class UsbWriteMethodAdvisor
        Private Const Fat32MaxFileBytes As Long = 4L * 1024 * 1024 * 1024 - 1L
        Private Const Fat32TightMarginBytes As Long = 64L * 1024 * 1024
        Private Const EfiSystemPartitionType As Byte = &HEF

        Public Function Assess(analysis As IsoAnalysisResult) As UsbWriteAssessment
            If analysis Is Nothing Then
                Throw New ArgumentNullException(NameOf(analysis))
            End If

            Dim assessment As New UsbWriteAssessment()
            assessment.ImageBootTargets.AddRange(GetImageBootTargets(analysis))

            ' Copia logica: o app grava o proprio MBR/PBR BootMgr (BIOS) e usa EFI\BOOT\BOOTX64.EFI (UEFI).
            ' Syslinux, GRUB e Bootwiz no BIOS nao sao instalados pela copia logica.
            Dim logicalTargets As New List(Of String)()
            If analysis.HasBootMgr Then logicalTargets.Add("bios")
            If analysis.HasEfiBootX64 Then logicalTargets.Add("uefi")

            Dim rawTargets = GetRawBootTargets(analysis)

            If Not analysis.HasFilesOver4Gb Then
                assessment.Fat32MarginBytes = Fat32MaxFileBytes - analysis.LargestFileSize
                AddOption(assessment, UsbWriteMethods.LogicalFat32, logicalTargets, False)
            Else
                assessment.Reasons.Add($"Arquivo maior que 4 GB: {String.Join(", ", analysis.FilesOver4Gb)}; FAT32 nao comporta a copia sem transformar a imagem.")
                If analysis.HasEfiBootX64 Then
                    AddOption(assessment, UsbWriteMethods.UefiNtfs, New List(Of String) From {"uefi"}, False)
                End If
                If OnlyWimFilesOver4Gb(analysis) Then
                    AddOption(assessment, UsbWriteMethods.Fat32SplitWim, logicalTargets, True)
                End If
            End If
            AddOption(assessment, UsbWriteMethods.Raw, rawTargets, False)

            If rawTargets.Count = 0 Then
                assessment.Reasons.Add("ISO nao hibrida (area de sistema sem MBR de boot): gravada como RAW/DD nao deve dar boot.")
            Else
                assessment.Reasons.Add($"ISO hibrida: RAW/DD com indicio de boot {FormatTargets(rawTargets)} (confirmar com teste de boot).")
            End If
            If analysis.HasSyslinux OrElse analysis.HasGrub OrElse analysis.HasAcronisBootwiz Then
                If Not analysis.HasBootMgr AndAlso assessment.ImageBootTargets.Contains("bios") Then
                    assessment.Reasons.Add("Boot BIOS por carregador proprio (Syslinux/GRUB/Bootwiz): a copia logica nao preserva o boot BIOS.")
                End If
            End If
            If assessment.Fat32MarginBytes.HasValue AndAlso assessment.Fat32MarginBytes.Value < Fat32TightMarginBytes Then
                assessment.Reasons.Add($"Maior arquivo ({analysis.LargestFilePath}) a {assessment.Fat32MarginBytes.Value:N0} bytes do limite de 4 GB do FAT32.")
            End If

            ChooseRecommendation(assessment)
            Return assessment
        End Function

        Private Shared Sub ChooseRecommendation(assessment As UsbWriteAssessment)
            Dim imageTargets = assessment.ImageBootTargets
            Dim covers = Function(item As UsbWriteMethodOption) imageTargets.All(Function(target) item.BootTargets.Contains(target))

            ' Ordem de preferencia quando atendem todos os alvos da imagem: copia logica (verificavel por manifesto),
            ' UEFI:NTFS (preserva a imagem), RAW/DD. Transformar o conteudo fica so como alternativa.
            Dim preference = {UsbWriteMethods.LogicalFat32, UsbWriteMethods.UefiNtfs, UsbWriteMethods.Raw}
            Dim chosen As UsbWriteMethodOption = Nothing
            If imageTargets.Count > 0 Then
                chosen = preference.Select(Function(method) assessment.FindMethod(method)).FirstOrDefault(Function(item) item IsNot Nothing AndAlso covers(item))
            End If
            If chosen Is Nothing Then
                ' Nenhum atende tudo: o que atende mais alvos, na mesma ordem de preferencia.
                chosen = preference.Select(Function(method) assessment.FindMethod(method)).
                    Where(Function(item) item IsNot Nothing).
                    OrderByDescending(Function(item) item.BootTargets.Count).
                    FirstOrDefault()
                If chosen IsNot Nothing AndAlso imageTargets.Count > 0 Then
                    Dim missing = imageTargets.Where(Function(target) Not chosen.BootTargets.Contains(target)).ToList()
                    assessment.Reasons.Add($"Nenhum metodo sem transformacao atende {FormatTargets(imageTargets)}; {chosen.Method} atende {FormatTargets(chosen.BootTargets)} e perde {FormatTargets(missing)}.")
                End If
            End If

            If chosen Is Nothing Then
                assessment.RecommendedMethod = UsbWriteMethods.None
                assessment.Reasons.Add("Nenhum metodo de gravacao suportado para esta imagem.")
            Else
                assessment.RecommendedMethod = chosen.Method
            End If
            assessment.AlternativeMethods.AddRange(
                assessment.Methods.Select(Function(item) item.Method).
                    Where(Function(method) Not String.Equals(method, assessment.RecommendedMethod, StringComparison.OrdinalIgnoreCase)))
        End Sub

        Private Shared Sub AddOption(assessment As UsbWriteAssessment, method As String, targets As List(Of String), transformsContent As Boolean)
            If targets Is Nothing OrElse targets.Count = 0 Then
                Return
            End If
            assessment.Methods.Add(New UsbWriteMethodOption With {
                .Method = method,
                .BootTargets = New List(Of String)(targets),
                .TransformsContent = transformsContent
            })
        End Sub

        Private Shared Function GetImageBootTargets(analysis As IsoAnalysisResult) As List(Of String)
            Dim targets As New List(Of String)()
            If analysis.HasElTorito Then
                If analysis.HasBiosElTorito Then targets.Add("bios")
                If analysis.HasUefiElTorito Then targets.Add("uefi")
            Else
                If analysis.HasBootMgr OrElse analysis.HasSyslinux OrElse analysis.HasGrub OrElse analysis.HasAcronisBootwiz Then targets.Add("bios")
                If analysis.HasEfiBootX64 Then targets.Add("uefi")
            End If
            Return targets
        End Function

        ' Indicio de boot por RAW/DD: MBR hibrido com codigo de boot (BIOS); GPT ou particao EFI na tabela (UEFI).
        Private Shared Function GetRawBootTargets(analysis As IsoAnalysisResult) As List(Of String)
            Dim targets As New List(Of String)()
            If Not analysis.IsHybridMbr Then
                Return targets
            End If
            targets.Add("bios")
            Dim hasEfiPartition = analysis.HasGpt OrElse analysis.MbrPartitionTypes.Contains(EfiSystemPartitionType)
            If hasEfiPartition AndAlso (analysis.HasUefiElTorito OrElse analysis.HasEfiBootX64) Then
                targets.Add("uefi")
            End If
            Return targets
        End Function

        Private Shared Function OnlyWimFilesOver4Gb(analysis As IsoAnalysisResult) As Boolean
            Return analysis.FilesOver4Gb.Count > 0 AndAlso analysis.FilesOver4Gb.All(
                Function(path)
                    Dim normalized = path.Replace("\"c, "/"c).TrimStart("/"c)
                    Return normalized.StartsWith("sources/", StringComparison.OrdinalIgnoreCase) AndAlso
                        normalized.EndsWith(".wim", StringComparison.OrdinalIgnoreCase)
                End Function)
        End Function

        Private Shared Function FormatTargets(targets As IEnumerable(Of String)) As String
            Dim values = targets.Select(Function(target) target.ToUpperInvariant()).ToList()
            Return If(values.Count = 0, "nenhum", String.Join(" + ", values))
        End Function
    End Class
End Namespace

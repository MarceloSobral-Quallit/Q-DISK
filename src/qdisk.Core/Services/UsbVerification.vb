' ─────────────────────────────────────────────────────────────────────────────
' qdisk  |  Core / Services  |  UsbVerification.vb
' Verificacao de USB sem dependencia do Windows (P10.1, etapa 3 da separacao da UsbBootView): leitura do manifesto
' USB, conferencia de arquivos contra o manifesto, regra do resultado da verificacao logica e avaliacao do modo de
' boot (BIOS/UEFI) a partir dos fatos coletados pela tela (arquivos, sistema de arquivos, MBR e setor de boot).
'
' © Quallit — Licenciado sob GPL-3.0-or-later.
' ─────────────────────────────────────────────────────────────────────────────
Imports System.IO
Imports System.Text.Json
Imports System.Threading

Namespace Services
    Public Enum UsbCheckStatus
        Success
        Failed
        Inconclusive
    End Enum

    Public NotInheritable Class UsbManifestEntry
        Public Property RelativePath As String
        Public Property SizeBytes As Long
        Public Property Sha256 As String
    End Class

    Public NotInheritable Class UsbManifestData
        Public Property Source As String
        Public Property Entries As New List(Of UsbManifestEntry)()
    End Class

    Public NotInheritable Class UsbManifest
        Private Sub New()
        End Sub

        ''' <summary>Arquivos do manifesto USB (path, size e sha256 opcional). Entradas sem path ou size sao ignoradas.</summary>
        Public Shared Function Parse(json As String, source As String, Optional origin As String = Nothing) As UsbManifestData
            Using document = JsonDocument.Parse(json)
                Dim filesElement As JsonElement
                If Not document.RootElement.TryGetProperty("files", filesElement) OrElse filesElement.ValueKind <> JsonValueKind.Array Then
                    Throw New InvalidDataException($"Manifesto sem array files: {If(origin, source)}")
                End If
                Dim result As New UsbManifestData With {.Source = source}
                For Each fileElement In filesElement.EnumerateArray()
                    Dim pathElement As JsonElement
                    Dim sizeElement As JsonElement
                    Dim shaElement As JsonElement
                    If Not fileElement.TryGetProperty("path", pathElement) OrElse Not fileElement.TryGetProperty("size", sizeElement) Then Continue For
                    Dim entrySha256 As String = Nothing
                    If fileElement.TryGetProperty("sha256", shaElement) AndAlso shaElement.ValueKind = JsonValueKind.String Then
                        entrySha256 = shaElement.GetString()
                    End If
                    result.Entries.Add(New UsbManifestEntry With {
                        .RelativePath = pathElement.GetString(),
                        .SizeBytes = sizeElement.GetInt64(),
                        .Sha256 = entrySha256
                    })
                Next
                Return result
            End Using
        End Function

        ''' <summary>Caminho do manifesto ("a/b.txt") como caminho relativo do Windows ("a\b.txt").</summary>
        Public Shared Function NormalizeRelativePath(value As String) As String
            Return If(value, String.Empty).Replace("/"c, Path.DirectorySeparatorChar).TrimStart(Path.DirectorySeparatorChar, "\"c, "/"c)
        End Function
    End Class

    Public NotInheritable Class ManifestCheckResult
        Public Property Checked As Integer
        Public Property FilesVerified As Integer
        Public Property SizeOnlyVerified As Integer
        Public Property MissingCount As Integer
        Public Property MismatchCount As Integer
        Public Property InaccessibleCount As Integer
        Public Property Details As New List(Of String)()
        Public Property Status As UsbCheckStatus
        Public Property Confidence As String
    End Class

    Public NotInheritable Class ManifestVerifier
        Public Const MaxDetailsPerKind As Integer = 20

        Private Sub New()
        End Sub

        ''' <summary>
        ''' Confere cada arquivo do manifesto no USB: ausente, tamanho diferente, hash diferente, inacessivel; sem sha256 no
        ''' manifesto, so o tamanho. computeSha256 calcula o hash de um arquivo (a tela usa leitura assincrona).
        ''' </summary>
        Public Shared Async Function VerifyAsync(rootPath As String, entries As IReadOnlyList(Of UsbManifestEntry),
                                                 computeSha256 As Func(Of String, CancellationToken, Task(Of String)),
                                                 Optional onProgress As Func(Of ManifestCheckResult, Task) = Nothing,
                                                 Optional cancellationToken As CancellationToken = Nothing) As Task(Of ManifestCheckResult)
            Dim result As New ManifestCheckResult()
            For Each entry In entries
                cancellationToken.ThrowIfCancellationRequested()
                result.Checked += 1
                Dim usbPath = Path.Combine(rootPath, UsbManifest.NormalizeRelativePath(entry.RelativePath))
                If Not File.Exists(usbPath) Then
                    result.MissingCount += 1
                    If result.MissingCount <= MaxDetailsPerKind Then result.Details.Add($"Ausente: {entry.RelativePath}")
                Else
                    Await CheckPresentFileAsync(result, entry, usbPath, computeSha256, cancellationToken)
                End If
                If onProgress IsNot Nothing Then Await onProgress(result)
            Next
            Classify(result)
            Return result
        End Function

        Private Shared Async Function CheckPresentFileAsync(result As ManifestCheckResult, entry As UsbManifestEntry, usbPath As String,
                                                            computeSha256 As Func(Of String, CancellationToken, Task(Of String)),
                                                            cancellationToken As CancellationToken) As Task(Of Boolean)
            Try
                If New FileInfo(usbPath).Length <> entry.SizeBytes Then
                    result.MismatchCount += 1
                    If result.MismatchCount <= MaxDetailsPerKind Then result.Details.Add($"Tamanho divergente: {entry.RelativePath}")
                    Return False
                End If
                If String.IsNullOrWhiteSpace(entry.Sha256) Then
                    result.SizeOnlyVerified += 1
                    Return True
                End If
                Dim actual = Await computeSha256(usbPath, cancellationToken)
                If String.Equals(actual, entry.Sha256, StringComparison.OrdinalIgnoreCase) Then
                    result.FilesVerified += 1
                    Return True
                End If
                result.MismatchCount += 1
                If result.MismatchCount <= MaxDetailsPerKind Then result.Details.Add($"Hash divergente: {entry.RelativePath}")
                Return False
            Catch ex As OperationCanceledException
                Throw
            Catch ex As Exception
                result.InaccessibleCount += 1
                If result.InaccessibleCount <= MaxDetailsPerKind Then result.Details.Add($"Inacessivel: {entry.RelativePath} ({ex.Message})")
                Return False
            End Try
        End Function

        ''' <summary>
        ''' Sucesso (alta) so com todos os arquivos conferidos por hash; sem ausente nem divergente, mas com arquivo
        ''' inacessivel ou conferido so pelo tamanho, inconclusivo (parcial); senao falha (parcial).
        ''' </summary>
        Public Shared Sub Classify(result As ManifestCheckResult)
            If result.MissingCount = 0 AndAlso result.MismatchCount = 0 AndAlso result.InaccessibleCount = 0 AndAlso result.SizeOnlyVerified = 0 Then
                result.Status = UsbCheckStatus.Success
                result.Confidence = "alta"
            ElseIf result.MissingCount = 0 AndAlso result.MismatchCount = 0 Then
                result.Status = UsbCheckStatus.Inconclusive
                result.Confidence = "parcial"
            Else
                result.Status = UsbCheckStatus.Failed
                result.Confidence = "parcial"
            End If
        End Sub
    End Class

    Public NotInheritable Class SizeCheckResult
        Public Property Checked As Integer
        Public Property MissingPaths As New List(Of String)()
        Public Property SizeMismatches As New List(Of (RelativePath As String, Expected As Long, Actual As Long))()
    End Class

    ''' <summary>Verificacao da copia logica contra a lista de arquivos da ISO.</summary>
    Public NotInheritable Class LogicalCopyCheck
        Private Sub New()
        End Sub

        ''' <summary>Confere presenca e tamanho de cada arquivo da ISO no USB (verificacao rapida pos-copia).</summary>
        Public Shared Function CheckSizes(rootPath As String, expected As IEnumerable(Of (RelativePath As String, Size As Long)),
                                          Optional cancellationToken As CancellationToken = Nothing) As SizeCheckResult
            Dim result As New SizeCheckResult()
            For Each entry In expected
                cancellationToken.ThrowIfCancellationRequested()
                Dim usbPath = Path.Combine(rootPath, entry.RelativePath)
                If Not File.Exists(usbPath) Then
                    result.MissingPaths.Add(entry.RelativePath)
                    Continue For
                End If
                Dim actual = New FileInfo(usbPath).Length
                If actual <> entry.Size Then
                    result.SizeMismatches.Add((entry.RelativePath, entry.Size, actual))
                    Continue For
                End If
                result.Checked += 1
            Next
            Return result
        End Function

        ''' <summary>
        ''' Verificacao por hash ISO x USB: sucesso sem nenhuma pendencia; sem ausente nem divergente, mas com arquivo
        ''' bloqueado pelo antivirus ou erro de leitura, inconclusiva; senao falha.
        ''' </summary>
        Public Shared Function ClassifyHashCheck(missing As Integer, mismatch As Long, blockedByAntivirus As Integer, readErrors As Integer) As UsbCheckStatus
            If missing = 0 AndAlso mismatch = 0 AndAlso readErrors = 0 AndAlso blockedByAntivirus = 0 Then Return UsbCheckStatus.Success
            If missing = 0 AndAlso mismatch = 0 Then Return UsbCheckStatus.Inconclusive
            Return UsbCheckStatus.Failed
        End Function
    End Class

    ''' <summary>Fatos sobre o USB que a tela coleta para avaliar o modo de boot.</summary>
    Public NotInheritable Class UsbBootFacts
        Public Property RootAccessible As Boolean
        Public Property HasBootMgr As Boolean
        Public Property HasUefiBootx64 As Boolean
        Public Property DriveFormat As String = String.Empty
        ''' <summary>Erro ao consultar o sistema de arquivos (Nothing se nao houve).</summary>
        Public Property DriveFormatError As String
        Public Property Mbr As Byte()
        ''' <summary>Setor de boot do volume (pela letra ou pelo inicio da particao ativa).</summary>
        Public Property VolumeBootSector As Byte()
        ''' <summary>Layout UEFI:NTFS (BOOTX64.EFI fora de FAT + particao 0xEF com a imagem); so consultado se preciso.</summary>
        Public Property IsUefiNtfsLayout As Func(Of Boolean)
    End Class

    Public NotInheritable Class UsbBootDetection
        Public Property HasBiosBoot As Boolean
        Public Property HasUefiBoot As Boolean
        Public Property Summary As String
        Public Property Details As New List(Of String)()

        ''' <summary>Nome do modo para a identificacao logica ("Boot BIOS + UEFI", "Boot BIOS", "Boot UEFI" ou "logical").</summary>
        Public ReadOnly Property ModeName As String
            Get
                If HasBiosBoot AndAlso HasUefiBoot Then Return "Boot BIOS + UEFI"
                If HasBiosBoot Then Return "Boot BIOS"
                If HasUefiBoot Then Return "Boot UEFI"
                Return "logical"
            End Get
        End Property

        ''' <summary>
        ''' UEFI: EFI\BOOT\BOOTX64.EFI em volume FAT, ou no layout UEFI:NTFS. BIOS: BOOTMGR na raiz, MBR com assinatura e
        ''' particao ativa, e setor de boot do volume valido referenciando BOOTMGR.
        ''' </summary>
        Public Shared Function Evaluate(facts As UsbBootFacts) As UsbBootDetection
            Dim result As New UsbBootDetection()
            If Not facts.RootAccessible Then
                result.Details.Add("Volume USB: sem letra montada ou raiz inacessivel; a verificacao por arquivos ficou limitada.")
            ElseIf facts.DriveFormatError IsNot Nothing Then
                result.Details.Add($"Sistema de arquivos: nao foi possivel consultar ({facts.DriveFormatError}).")
            End If

            Dim driveFormat = If(facts.DriveFormat, String.Empty)
            Dim isFatForUefi = driveFormat.StartsWith("FAT", StringComparison.OrdinalIgnoreCase)
            If facts.HasUefiBootx64 AndAlso isFatForUefi Then
                result.HasUefiBoot = True
                result.Details.Add("UEFI: detectado EFI\BOOT\BOOTX64.EFI em volume FAT/FAT32.")
            ElseIf facts.HasUefiBootx64 AndAlso facts.IsUefiNtfsLayout IsNot Nothing AndAlso facts.IsUefiNtfsLayout.Invoke() Then
                result.HasUefiBoot = True
                result.Details.Add($"UEFI: layout UEFI:NTFS detectado (EFI\BOOT\BOOTX64.EFI em {driveFormat} + particao auxiliar 0xEF identica a imagem UEFI:NTFS embarcada).")
            ElseIf facts.HasUefiBootx64 Then
                result.Details.Add($"UEFI: EFI\BOOT\BOOTX64.EFI existe, mas o sistema de arquivos nao foi confirmado como FAT/FAT32 ({If(String.IsNullOrWhiteSpace(driveFormat), "desconhecido", driveFormat)}).")
            Else
                result.Details.Add("UEFI: EFI\BOOT\BOOTX64.EFI nao encontrado.")
            End If

            If Not String.IsNullOrWhiteSpace(driveFormat) Then
                result.Details.Add($"Sistema de arquivos: {driveFormat}.")
            End If

            Dim hasMbrSignature = MbrLayout.HasBootSignature(facts.Mbr)
            Dim activePartitionIndex = MbrLayout.GetActivePartitionIndex(facts.Mbr)
            Dim activePartitionType = MbrLayout.GetPartitionType(facts.Mbr, activePartitionIndex)
            Dim hasVbrSignature = MbrLayout.HasBootSignature(facts.VolumeBootSector)
            Dim vbrReferencesBootMgr = MbrLayout.SectorContainsAscii(facts.VolumeBootSector, "BOOTMGR")

            result.Details.Add(If(hasMbrSignature, "BIOS: MBR com assinatura 55 AA detectada.", "BIOS: MBR nao lido ou sem assinatura 55 AA."))
            If activePartitionIndex >= 0 Then
                result.Details.Add($"BIOS: particao ativa detectada no MBR (indice {activePartitionIndex + 1}, tipo 0x{activePartitionType:X2}).")
            Else
                result.Details.Add("BIOS: nenhuma particao ativa detectada no MBR.")
            End If
            result.Details.Add(If(facts.HasBootMgr, "BIOS: BOOTMGR encontrado na raiz do USB.", "BIOS: BOOTMGR nao encontrado na raiz do USB."))
            If hasVbrSignature AndAlso vbrReferencesBootMgr Then
                result.Details.Add("BIOS: setor de boot do volume referencia BOOTMGR.")
            ElseIf hasVbrSignature Then
                result.Details.Add("BIOS: setor de boot do volume foi lido, mas nao referencia BOOTMGR.")
            Else
                result.Details.Add("BIOS: setor de boot do volume nao foi lido ou nao tem assinatura valida.")
            End If

            result.HasBiosBoot = facts.HasBootMgr AndAlso hasMbrSignature AndAlso activePartitionIndex >= 0 AndAlso hasVbrSignature AndAlso vbrReferencesBootMgr
            If result.HasBiosBoot AndAlso result.HasUefiBoot Then
                result.Summary = "USB bootavel detectado: BIOS + UEFI."
            ElseIf result.HasBiosBoot Then
                result.Summary = "USB bootavel detectado: BIOS legado."
            ElseIf result.HasUefiBoot Then
                result.Summary = "USB bootavel detectado: UEFI."
            Else
                result.Summary = "Boot USB nao confirmado para BIOS nem UEFI."
            End If
            Return result
        End Function
    End Class
End Namespace

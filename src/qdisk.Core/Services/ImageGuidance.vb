Imports qdisk.Core.Models

Namespace Services
    ''' <summary>
    ''' Texto de orientacao de uma imagem do catalogo para o tecnico: a descricao funcional (campo description, editado
    ''' no QDisk_Adm) e o "Como gravar", gerado a partir da usbWritePolicy (ISO) ou fixo para TIBX. Gerar o "Como gravar"
    ''' em vez de escreve-lo a mao mantem a orientacao igual a politica quando ela muda.
    ''' </summary>
    Public NotInheritable Class ImageGuidance
        Private Sub New()
        End Sub

        Public Const TibxGuidance As String = "Copiar o arquivo para o USB (NTFS)"

        Public Shared Function HowToWrite(item As CatalogItem) As String
            If item Is Nothing Then Return String.Empty
            ' Itens sem fileName (ex.: devteam2) so tem a extensao na URL.
            Dim fileName = If(String.IsNullOrWhiteSpace(item.FileName), If(item.FileUrl, String.Empty).Split("?"c)(0), item.FileName)
            If fileName.EndsWith(".tibx", StringComparison.OrdinalIgnoreCase) Then
                Return TibxGuidance
            End If
            Dim policy = item.UsbWritePolicy
            If policy Is Nothing OrElse String.IsNullOrWhiteSpace(policy.PreferredMethod) Then Return String.Empty

            Dim method = MethodLabel(policy.PreferredMethod)
            Dim targets = TargetsLabel(policy.PreferredBootTargets)
            Return If(targets.Length = 0, method, $"{method} — {targets}")
        End Function

        ''' <summary>Descricao + "Como gravar", em linhas; vazio se o item nao tiver nenhum dos dois.</summary>
        Public Shared Function Describe(item As CatalogItem) As String
            If item Is Nothing Then Return String.Empty
            Dim lines As New List(Of String)()
            If Not String.IsNullOrWhiteSpace(item.Description) Then lines.Add(item.Description.Trim())
            Dim how = HowToWrite(item)
            If how.Length > 0 Then lines.Add($"Como gravar: {how}")
            Return String.Join(Environment.NewLine, lines)
        End Function

        Public Shared Function MethodLabel(method As String) As String
            Select Case If(method, String.Empty).Trim().ToLowerInvariant()
                Case UsbWriteMethods.LogicalFat32
                    Return "Cópia lógica FAT32"
                Case UsbWriteMethods.Raw
                    Return "RAW/DD"
                Case UsbWriteMethods.UefiNtfs
                    Return "UEFI:NTFS"
                Case UsbWriteMethods.Fat32SplitWim
                    Return "FAT32 com split do WIM"
                Case Else
                    Return method
            End Select
        End Function

        Private Shared Function TargetsLabel(targets As IEnumerable(Of String)) As String
            Dim targetSet = New HashSet(Of String)(If(targets, Enumerable.Empty(Of String)()).Select(Function(target) target.Trim().ToLowerInvariant()))
            Dim bios = targetSet.Contains("bios")
            Dim uefi = targetSet.Contains("uefi")
            If bios AndAlso uefi Then Return "BIOS e UEFI"
            If uefi Then Return "somente UEFI"
            If bios Then Return "BIOS"
            Return String.Empty
        End Function
    End Class
End Namespace

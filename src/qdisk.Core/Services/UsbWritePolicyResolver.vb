Imports qdisk.Core.Models

Namespace Services
    Public NotInheritable Class UsbWritePolicyDecision
        ''' <summary>True quando a politica do catalogo vale para a ISO local.</summary>
        Public Property IsApplied As Boolean
        Public Property CatalogName As String
        Public Property PreferredMethod As String
        Public Property AllowedMethods As New List(Of String)()
        Public Property Note As String
        Public Property VolumeLabel As String
        ''' <summary>Por que a politica nao foi usada (o app cai na analise local).</summary>
        Public Property IgnoredReason As String
    End Class

    ''' <summary>
    ''' Decide se a usbWritePolicy do catalogo vale para a ISO selecionada no app (P2). Politica ausente,
    ''' incoerente ou de outra imagem nunca bloqueia: devolve o motivo e o app usa a analise local.
    ''' </summary>
    Public NotInheritable Class UsbWritePolicyResolver
        Private Shared ReadOnly TransformingMethods As String() = {UsbWriteMethods.Fat32SplitWim}

        ''' <summary>
        ''' Acha o item do catalogo da ISO: pelo nome registrado no download; senao por fileName + tamanho.
        ''' Itens com o mesmo arquivo (pares -sharepoint/-dropbox) so valem se tiverem a mesma politica.
        ''' </summary>
        Public Function FindCatalogItem(catalog As IEnumerable(Of CatalogItem), downloadedCatalogName As String,
                                        isoFileName As String, isoSizeBytes As Long,
                                        <System.Runtime.InteropServices.Out> ByRef reason As String) As CatalogItem
            reason = Nothing
            Dim items = If(catalog, Enumerable.Empty(Of CatalogItem)()).Where(Function(item) item IsNot Nothing).ToList()

            If Not String.IsNullOrWhiteSpace(downloadedCatalogName) Then
                Dim byName = items.FirstOrDefault(Function(item) String.Equals(item.Name, downloadedCatalogName, StringComparison.OrdinalIgnoreCase))
                If byName IsNot Nothing Then
                    Return byName
                End If
            End If

            Dim candidates = items.Where(Function(item) String.Equals(item.FileName, isoFileName, StringComparison.OrdinalIgnoreCase) AndAlso
                                                       item.ImageSizeBytes = isoSizeBytes).ToList()
            If candidates.Count = 0 Then
                reason = "ISO fora dos catalogos (nome e tamanho nao conferem com nenhum item)."
                Return Nothing
            End If
            Dim first = candidates(0)
            Dim firstKey = PolicyKey(first.UsbWritePolicy)
            If candidates.Any(Function(item) PolicyKey(item.UsbWritePolicy) <> firstKey) Then
                reason = $"Itens do catalogo com o mesmo arquivo e politicas diferentes: {String.Join(", ", candidates.Select(Function(item) item.Name))}."
                Return Nothing
            End If
            Return first
        End Function

        Public Function Resolve(item As CatalogItem, isoSizeBytes As Long) As UsbWritePolicyDecision
            Dim decision As New UsbWritePolicyDecision With {.CatalogName = item?.Name}
            Dim policy = item?.UsbWritePolicy
            If item Is Nothing Then
                Return Ignore(decision, "ISO fora dos catalogos.")
            End If
            If policy Is Nothing Then
                Return Ignore(decision, "Item do catalogo sem usbWritePolicy.")
            End If

            Dim allowed = If(policy.AllowedMethods, New List(Of String)()).
                Where(Function(method) Not String.IsNullOrWhiteSpace(method)).
                Select(Function(method) method.Trim().ToLowerInvariant()).
                Distinct().ToList()
            If allowed.Count = 0 Then
                Return Ignore(decision, "usbWritePolicy sem allowedMethods.")
            End If
            Dim unknown = allowed.Where(Function(method) Not UsbWriteMethods.All.Contains(method)).ToList()
            If unknown.Count > 0 Then
                Return Ignore(decision, $"usbWritePolicy com metodo desconhecido: {String.Join(", ", unknown)}.")
            End If
            Dim preferred = If(policy.PreferredMethod, String.Empty).Trim().ToLowerInvariant()
            If Not allowed.Contains(preferred) Then
                Return Ignore(decision, $"usbWritePolicy com preferredMethod ({preferred}) fora de allowedMethods.")
            End If
            If Not policy.AllowContentTransformations.GetValueOrDefault() AndAlso allowed.Any(Function(method) TransformingMethods.Contains(method)) Then
                Return Ignore(decision, "usbWritePolicy permite metodo que altera a imagem sem allowContentTransformations=true.")
            End If
            If Not String.IsNullOrWhiteSpace(item.Sha256) AndAlso
               Not String.Equals(policy.ImageSha256?.Trim(), item.Sha256.Trim(), StringComparison.OrdinalIgnoreCase) Then
                Return Ignore(decision, "usbWritePolicy se refere a outra imagem (imageSha256 diferente do sha256 do item).")
            End If
            If item.ImageSizeBytes <= 0 OrElse item.ImageSizeBytes <> isoSizeBytes Then
                Return Ignore(decision, $"Tamanho da ISO local ({isoSizeBytes:N0} bytes) diferente do catalogo ({item.ImageSizeBytes:N0} bytes).")
            End If

            decision.IsApplied = True
            decision.PreferredMethod = preferred
            decision.AllowedMethods = allowed
            decision.Note = policy.Note
            decision.VolumeLabel = policy.VolumeLabel
            Return decision
        End Function

        Private Shared Function Ignore(decision As UsbWritePolicyDecision, reason As String) As UsbWritePolicyDecision
            decision.IsApplied = False
            decision.IgnoredReason = reason
            Return decision
        End Function

        Private Shared Function PolicyKey(policy As UsbWritePolicyInfo) As String
            If policy Is Nothing Then
                Return String.Empty
            End If
            Dim allowed = If(policy.AllowedMethods, New List(Of String)()).Select(Function(method) method?.Trim().ToLowerInvariant()).OrderBy(Function(method) method)
            Return $"{policy.PreferredMethod?.Trim().ToLowerInvariant()}|{String.Join(",", allowed)}|{policy.AllowContentTransformations}|{policy.ImageSha256?.Trim().ToLowerInvariant()}|{policy.VolumeLabel?.Trim()}"
        End Function
    End Class
End Namespace

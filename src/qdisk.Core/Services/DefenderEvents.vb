Imports System.Xml.Linq

Namespace Services
    Public NotInheritable Class DefenderEventInfo
        Public Property EventId As Integer
        Public Property Paths As New List(Of String)()
        Public Property Description As String
    End Class

    ''' <summary>
    ''' Le eventos do Microsoft Defender (log Microsoft-Windows-Windows Defender/Operational, exportado pelo
    ''' wevtutil em XML) e cruza com arquivos que faltaram no USB depois da copia. Usado para dizer ao tecnico
    ''' que um arquivo foi removido ou barrado pelo Defender, em vez de so "arquivo ausente".
    ''' </summary>
    Public NotInheritable Class DefenderEvents
        Public Const LogName As String = "Microsoft-Windows-Windows Defender/Operational"

        ' 1116/1117: deteccao e acao do antivirus; 1121: bloqueio por regra ASR.
        Public Shared ReadOnly EventIds As Integer() = {1116, 1117, 1121}

        Private Shared ReadOnly AsrRuleNames As New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase) From {
            {"c0033c00-d16d-4114-a5a0-dc9b3a7d2ceb", "regra ASR: ferramenta do sistema copiada"},
            {"56a863a9-875e-4185-98a7-b882c64b5ce5", "regra ASR: driver assinado vulneravel"}
        }

        ''' <summary>Argumentos do wevtutil para os eventos relevantes dos ultimos minutos.</summary>
        Public Shared Function BuildWevtutilArguments(lastMinutes As Integer, maxEvents As Integer) As String
            Dim ids = String.Join(" or ", EventIds.Select(Function(id) $"EventID={id}"))
            Dim query = $"*[System[({ids}) and TimeCreated[timediff(@SystemTime) <= {CLng(lastMinutes) * 60000L}]]]"
            Return $"qe ""{LogName}"" /q:""{query}"" /f:xml /rd:true /c:{maxEvents}"
        End Function

        ''' <summary>Interpreta a saida XML do wevtutil (varios &lt;Event&gt; sem raiz).</summary>
        Public Shared Function Parse(xmlText As String) As List(Of DefenderEventInfo)
            Dim result As New List(Of DefenderEventInfo)()
            If String.IsNullOrWhiteSpace(xmlText) Then
                Return result
            End If

            Dim root = XElement.Parse("<Events>" & xmlText & "</Events>")
            For Each evt In root.Elements().Where(Function(element) element.Name.LocalName = "Event")
                Dim system = evt.Elements().FirstOrDefault(Function(element) element.Name.LocalName = "System")
                Dim eventIdText = system?.Elements().FirstOrDefault(Function(element) element.Name.LocalName = "EventID")?.Value
                Dim eventId As Integer
                If Not Integer.TryParse(eventIdText, eventId) Then Continue For

                Dim data As New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase)
                Dim eventData = evt.Elements().FirstOrDefault(Function(element) element.Name.LocalName = "EventData")
                If eventData IsNot Nothing Then
                    For Each item In eventData.Elements().Where(Function(element) element.Name.LocalName = "Data")
                        Dim name = item.Attribute("Name")?.Value
                        If Not String.IsNullOrEmpty(name) Then data(name) = item.Value
                    Next
                End If

                Dim info As New DefenderEventInfo With {.EventId = eventId}
                Dim rawPath As String = Nothing
                data.TryGetValue("Path", rawPath)
                For Each part In If(rawPath, String.Empty).Split(";"c)
                    Dim cleaned = part.Trim()
                    If cleaned.StartsWith("file:_", StringComparison.OrdinalIgnoreCase) Then cleaned = cleaned.Substring(6)
                    If cleaned.Length > 0 Then info.Paths.Add(cleaned)
                Next

                If eventId = 1121 Then
                    Dim ruleId As String = Nothing
                    data.TryGetValue("ID", ruleId)
                    Dim ruleName As String = Nothing
                    info.Description = If(ruleId IsNot Nothing AndAlso AsrRuleNames.TryGetValue(ruleId.Trim(), ruleName), ruleName, $"regra ASR {ruleId}")
                Else
                    Dim threat As String = Nothing
                    data.TryGetValue("Threat Name", threat)
                    info.Description = $"antivirus: {threat}"
                End If

                If info.Paths.Count > 0 Then result.Add(info)
            Next

            Return result
        End Function

        ''' <summary>
        ''' Para cada arquivo ausente (caminho relativo a <paramref name="rootPath"/>), a primeira causa encontrada
        ''' nos eventos. Arquivos sem evento correspondente ficam de fora.
        ''' </summary>
        Public Shared Function MatchMissingFiles(events As IEnumerable(Of DefenderEventInfo), rootPath As String,
                                                 missingRelativePaths As IEnumerable(Of String)) As Dictionary(Of String, String)
            Dim matches As New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase)
            Dim root = If(rootPath, String.Empty).Replace("/"c, "\"c).TrimEnd("\"c)
            For Each relative In If(missingRelativePaths, Enumerable.Empty(Of String)())
                Dim full = root & "\" & relative.Replace("/"c, "\"c).TrimStart("\"c)
                For Each evt In If(events, Enumerable.Empty(Of DefenderEventInfo)())
                    If evt.Paths.Any(Function(path) String.Equals(path, full, StringComparison.OrdinalIgnoreCase)) Then
                        If Not matches.ContainsKey(relative) Then matches(relative) = evt.Description
                        Exit For
                    End If
                Next
            Next
            Return matches
        End Function
    End Class
End Namespace

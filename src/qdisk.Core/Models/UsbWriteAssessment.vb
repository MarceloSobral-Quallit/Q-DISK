Namespace Models
    ''' <summary>Nomes dos metodos de gravacao usados nos manifestos, nos catalogos (usbWritePolicy) e no app.</summary>
    Public NotInheritable Class UsbWriteMethods
        Public Const LogicalFat32 As String = "logical-fat32"
        Public Const UefiNtfs As String = "uefi-ntfs"
        Public Const Raw As String = "raw"
        Public Const Fat32SplitWim As String = "fat32-split-wim"
        Public Const None As String = "none"

        Public Shared ReadOnly All As IReadOnlyList(Of String) = {LogicalFat32, UefiNtfs, Raw, Fat32SplitWim}

        Private Sub New()
        End Sub
    End Class

    Public NotInheritable Class UsbWriteMethodOption
        Public Property Method As String
        ''' <summary>Alvos de boot que o metodo atende para esta imagem: "bios", "uefi".</summary>
        Public Property BootTargets As New List(Of String)()
        ''' <summary>True quando o metodo altera o conteudo da imagem (ex.: dividir install.wim).</summary>
        Public Property TransformsContent As Boolean
    End Class

    Public NotInheritable Class UsbWriteAssessment
        ''' <summary>Alvos de boot que a propria imagem declara (El Torito, ou artefatos quando nao ha El Torito).</summary>
        Public Property ImageBootTargets As New List(Of String)()
        Public Property RecommendedMethod As String = UsbWriteMethods.None
        Public Property AlternativeMethods As New List(Of String)()
        ''' <summary>Todos os metodos viaveis, com os alvos de boot de cada um.</summary>
        Public Property Methods As New List(Of UsbWriteMethodOption)()
        Public Property Fat32MarginBytes As Long?
        Public Property Reasons As New List(Of String)()

        Public Function FindMethod(method As String) As UsbWriteMethodOption
            Return Methods.FirstOrDefault(Function(item) String.Equals(item.Method, method, StringComparison.OrdinalIgnoreCase))
        End Function
    End Class
End Namespace

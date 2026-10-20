' ─────────────────────────────────────────────────────────────────────────────
' qdisk  |  Core / Services  |  DiskSafety.vb
' Identidade de disco fisico e travas contra apagar o disco errado.
'
' © Quallit — Licenciado sob GPL-3.0-or-later.
' ─────────────────────────────────────────────────────────────────────────────

Imports System.IO
Imports System.Runtime.InteropServices
Imports System.Text
Imports Microsoft.Win32.SafeHandles

Namespace Services
    ''' <summary>
    ''' Identidade de um disco fisico, lida do proprio dispositivo. O numero do disco
    ''' (PhysicalDriveN) pode mudar quando dispositivos sao conectados ou removidos;
    ''' os demais campos identificam o aparelho.
    ''' </summary>
    Public NotInheritable Class DiskIdentity
        Public Property DiskNumber As Integer
        Public Property VendorId As String = String.Empty
        Public Property ProductId As String = String.Empty
        Public Property SerialNumber As String = String.Empty
        Public Property BusType As Integer
        Public Property RemovableMedia As Boolean
        Public Property SizeBytes As Long

        ''' <summary>Mesmo aparelho, ignorando o numero do disco.</summary>
        Public Function SameDeviceAs(other As DiskIdentity) As Boolean
            If other Is Nothing Then Return False
            Return SizeBytes = other.SizeBytes AndAlso
                   BusType = other.BusType AndAlso
                   String.Equals(VendorId, other.VendorId, StringComparison.OrdinalIgnoreCase) AndAlso
                   String.Equals(ProductId, other.ProductId, StringComparison.OrdinalIgnoreCase) AndAlso
                   String.Equals(SerialNumber, other.SerialNumber, StringComparison.OrdinalIgnoreCase)
        End Function

        Public Function Describe() As String
            Dim name = $"{VendorId} {ProductId}".Trim()
            If name.Length = 0 Then name = "dispositivo sem nome"
            Dim serial = If(String.IsNullOrWhiteSpace(SerialNumber), "sem serie", $"serie {SerialNumber}")
            Return $"Disk {DiskNumber}: {name}, {SizeBytes:N0} bytes, {serial}"
        End Function
    End Class

    Public NotInheritable Class DiskSafetyException
        Inherits InvalidOperationException

        Public Sub New(message As String)
            MyBase.New(message)
        End Sub
    End Class

    Public NotInheritable Class DiskSafety
        ''' <summary>Acima deste tamanho, apagar exige confirmacao reforcada.</summary>
        Public Const ReinforcedConfirmationSizeBytes As Long = 256L * 1000L * 1000L * 1000L

        Public Const BusTypeUsb As Integer = 7

        Private Sub New()
        End Sub

        ''' <summary>
        ''' Le a identidade do disco. Usa so IOCTLs de consulta (acesso 0), entao funciona
        ''' sem privilegio de administrador. Devolve Nothing se o disco nao existir.
        ''' </summary>
        Public Shared Function TryQuery(diskNumber As Integer) As DiskIdentity
            If diskNumber < 0 Then Return Nothing

            Using handle = OpenForQuery($"\\.\PhysicalDrive{diskNumber}")
                If handle.IsInvalid Then Return Nothing

                Dim identity As New DiskIdentity With {.DiskNumber = diskNumber}
                If Not ReadDeviceDescriptor(handle, identity) Then Return Nothing

                Dim size As Long
                If Not TryGetDiskSize(handle, size) Then Return Nothing
                identity.SizeBytes = size
                Return identity
            End Using
        End Function

        ''' <summary>
        ''' Discos que hospedam o Windows: unidade do sistema, pasta do Windows e pasta System32.
        ''' Nenhuma operacao destrutiva pode atingir esses discos.
        ''' </summary>
        Public Shared Function GetSystemDiskNumbers() As HashSet(Of Integer)
            Dim result As New HashSet(Of Integer)()
            Dim roots As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
            For Each folder In {Environment.GetEnvironmentVariable("SystemDrive"),
                                Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                                Environment.GetFolderPath(Environment.SpecialFolder.System)}
                If String.IsNullOrWhiteSpace(folder) Then Continue For
                Dim root = Path.GetPathRoot(folder)
                If Not String.IsNullOrWhiteSpace(root) AndAlso root.Length >= 2 AndAlso root(1) = ":"c Then
                    roots.Add(root.Substring(0, 2))
                End If
            Next

            For Each letter In roots
                For Each disk In GetVolumeDiskNumbers(letter)
                    result.Add(disk)
                Next
            Next
            Return result
        End Function

        ''' <summary>
        ''' Confere, imediatamente antes de apagar ou gravar, que o disco no numero esperado
        ''' ainda e o mesmo aparelho escolhido pelo usuario e que nao hospeda o Windows.
        ''' Lanca <see cref="DiskSafetyException"/> se qualquer checagem falhar.
        ''' </summary>
        Public Shared Sub EnsureSameDisk(expected As DiskIdentity)
            If expected Is Nothing Then
                Throw New DiskSafetyException("Disco de destino sem identificacao confirmada. Nada foi gravado.")
            End If

            If GetSystemDiskNumbers().Contains(expected.DiskNumber) Then
                Throw New DiskSafetyException($"O Disk {expected.DiskNumber} hospeda o Windows e nao pode ser apagado. Nada foi gravado.")
            End If

            Dim current = TryQuery(expected.DiskNumber)
            If current Is Nothing Then
                Throw New DiskSafetyException($"O Disk {expected.DiskNumber} nao foi encontrado (dispositivo removido?). Nada foi gravado.")
            End If

            If Not current.SameDeviceAs(expected) Then
                Throw New DiskSafetyException(
                    $"O dispositivo no Disk {expected.DiskNumber} mudou desde a selecao. Nada foi gravado.{Environment.NewLine}" &
                    $"Selecionado: {expected.Describe()}{Environment.NewLine}" &
                    $"Encontrado agora: {current.Describe()}")
            End If
        End Sub

        ''' <summary>Disco nao removivel ou grande demais para um pendrive comum.</summary>
        Public Shared Function NeedsReinforcedConfirmation(identity As DiskIdentity) As Boolean
            If identity Is Nothing Then Return True
            Return Not identity.RemovableMedia OrElse identity.SizeBytes > ReinforcedConfirmationSizeBytes
        End Function

        ' ── Win32 ──────────────────────────────────────────────────────────────

        Private Const IOCTL_STORAGE_QUERY_PROPERTY As UInteger = &H2D1400UI
        Private Const IOCTL_DISK_GET_DRIVE_GEOMETRY_EX As UInteger = &H700A0UI
        Private Const IOCTL_VOLUME_GET_VOLUME_DISK_EXTENTS As UInteger = &H560000UI
        Private Const FILE_SHARE_READ As UInteger = 1
        Private Const FILE_SHARE_WRITE As UInteger = 2
        Private Const OPEN_EXISTING As UInteger = 3

        Private Shared Function OpenForQuery(path As String) As SafeFileHandle
            Return CreateFile(path, 0UI, FILE_SHARE_READ Or FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, 0UI, IntPtr.Zero)
        End Function

        Private Shared Function ReadDeviceDescriptor(handle As SafeFileHandle, identity As DiskIdentity) As Boolean
            ' STORAGE_PROPERTY_QUERY { PropertyId = StorageDeviceProperty (0), QueryType = PropertyStandardQuery (0) }
            Dim query(11) As Byte
            Dim output(1023) As Byte
            Dim returned As UInteger
            If Not DeviceIoControl(handle, IOCTL_STORAGE_QUERY_PROPERTY, query, CUInt(query.Length), output, CUInt(output.Length), returned, IntPtr.Zero) Then
                Return False
            End If
            If returned < 32 Then Return False

            ' STORAGE_DEVICE_DESCRIPTOR
            identity.RemovableMedia = output(10) <> 0
            identity.VendorId = ReadAnsiAt(output, BitConverter.ToInt32(output, 12), CInt(returned))
            identity.ProductId = ReadAnsiAt(output, BitConverter.ToInt32(output, 16), CInt(returned))
            identity.SerialNumber = ReadAnsiAt(output, BitConverter.ToInt32(output, 24), CInt(returned))
            identity.BusType = BitConverter.ToInt32(output, 28)
            Return True
        End Function

        Private Shared Function TryGetDiskSize(handle As SafeFileHandle, ByRef size As Long) As Boolean
            ' DISK_GEOMETRY_EX: DISK_GEOMETRY (24 bytes) seguido de DiskSize (Int64).
            Dim output(255) As Byte
            Dim returned As UInteger
            If Not DeviceIoControl(handle, IOCTL_DISK_GET_DRIVE_GEOMETRY_EX, Nothing, 0UI, output, CUInt(output.Length), returned, IntPtr.Zero) OrElse returned < 32 Then
                Return False
            End If
            size = BitConverter.ToInt64(output, 24)
            Return size > 0
        End Function

        ''' <summary>Discos fisicos que contem o volume da letra informada (ex.: "E:").</summary>
        Public Shared Function GetVolumeDiskNumbers(driveLetter As String) As List(Of Integer)
            Dim disks As New List(Of Integer)()
            Using handle = OpenForQuery($"\\.\{driveLetter}")
                If handle.IsInvalid Then Return disks
                Dim output(4095) As Byte
                Dim returned As UInteger
                If Not DeviceIoControl(handle, IOCTL_VOLUME_GET_VOLUME_DISK_EXTENTS, Nothing, 0UI, output, CUInt(output.Length), returned, IntPtr.Zero) OrElse returned < 8 Then
                    Return disks
                End If
                ' VOLUME_DISK_EXTENTS: NumberOfDiskExtents (UInt32) + padding; cada DISK_EXTENT tem 24 bytes.
                Dim count = BitConverter.ToInt32(output, 0)
                For i = 0 To count - 1
                    Dim offset = 8 + i * 24
                    If offset + 4 > returned Then Exit For
                    disks.Add(BitConverter.ToInt32(output, offset))
                Next
            End Using
            Return disks
        End Function

        Private Shared Function ReadAnsiAt(buffer As Byte(), offset As Integer, limit As Integer) As String
            If offset <= 0 OrElse offset >= limit Then Return String.Empty
            Dim finish = offset
            While finish < limit AndAlso buffer(finish) <> 0
                finish += 1
            End While
            Return Encoding.ASCII.GetString(buffer, offset, finish - offset).Trim()
        End Function

        <DllImport("kernel32.dll", SetLastError:=True, CharSet:=CharSet.Unicode)>
        Private Shared Function CreateFile(lpFileName As String, dwDesiredAccess As UInteger, dwShareMode As UInteger,
                                           lpSecurityAttributes As IntPtr, dwCreationDisposition As UInteger,
                                           dwFlagsAndAttributes As UInteger, hTemplateFile As IntPtr) As SafeFileHandle
        End Function

        <DllImport("kernel32.dll", SetLastError:=True)>
        Private Shared Function DeviceIoControl(hDevice As SafeFileHandle, dwIoControlCode As UInteger,
                                                lpInBuffer As Byte(), nInBufferSize As UInteger,
                                                lpOutBuffer As Byte(), nOutBufferSize As UInteger,
                                                ByRef lpBytesReturned As UInteger, lpOverlapped As IntPtr) As <MarshalAs(UnmanagedType.Bool)> Boolean
        End Function
    End Class
End Namespace

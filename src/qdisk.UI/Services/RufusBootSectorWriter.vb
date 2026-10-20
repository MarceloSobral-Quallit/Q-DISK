' -----------------------------------------------------------------------------
' qdisk | UI / Services | RufusBootSectorWriter.vb
' Derived from Rufus/ms-sys GPLv3 boot-sector artifacts.
'
' Copyright (c) 2026 Quallit.
' Portions derived from Rufus: The Reliable USB Formatting Utility and ms-sys,
' licensed under GPLv3-or-later. See LICENSE and THIRD_PARTY_NOTICES.md.
' -----------------------------------------------------------------------------

Imports System.IO
Imports System.Runtime.InteropServices
Imports System.Text
Imports Microsoft.Win32.SafeHandles

Namespace Services
    Friend NotInheritable Class RufusBootSectorWriter
        Private Sub New()
        End Sub

        Private Shared ReadOnly MbrWin7 As Byte() = Convert.FromBase64String("M8CO0LwAfI7Ajti+AHy/AAa5AAL886RQaBwGy/u5BAC9vgeAfgAAfAsPhQ4Bg8UQ4vHNGIhWAFXGRhEFxkYQALRBu6pVzRNdcg+B+1WqdQn3wQEAdAP+RhBmYIB+EAB0JmZoAAAAAGb/dghoAABoAHxoAQBoEAC0QopWAIv0zROfg8QQnusUuAECuwB8ilYAinYBik4Cim4DzRNmYXMc/k4RdQyAfgCAD4SKALKA64RVMuSKVgDNE13rnoE+/n1VqnVu/3YA6I0AdRf6sNHmZOiDALDf5mDofACw/+Zk6HUA+7gAu80aZiPAdTtmgftUQ1BBdTKB+QIBcixmaAe7AABmaAACAABmaAgAAABmU2ZTZlVmaAAAAABmaAB8AABmYWgAAAfNGloy9uoAfAAAzRigtwfrCKC2B+sDoLUHMuQFAAeL8Kw8AHQJuwcAtA7NEOvy9Ov9K8nkZOsAJALg+CQCw0ludmFsaWQgcGFydGl0aW9uIHRhYmxlAEVycm9yIGxvYWRpbmcgb3BlcmF0aW5nIHN5c3RlbQBNaXNzaW5nIG9wZXJhdGluZyBzeXN0ZW0AAABje5o=")
        Private Shared ReadOnly Fat32JumpAndOem As Byte() = Convert.FromBase64String("61iQTVNXSU40LjE=")
        Private Shared ReadOnly Fat32BootMgr0x52 As Byte() = Convert.FromBase64String("RkFUMzIgICAzyY7RvPR7jsGO2b0AfIhOAopWQLRBu6pVzRNyEIH7Vap1CvbBAXQF/kYC6y2KVkC0CM0TcwW5//+K8WYPtsZAZg+20YDiP/fihs3A7QZBZg+3yWb34WaJRviDfhYAdTiDfioAdzJmi0YcZoPADLsAgLkBAOgrAOksA6D6fbR9i/CshMB0Fzz/dAm0DrsHAM0Q6+6g+33r5aD5fevgmM0WzRlmYIB+AgAPhCAAZmoAZlAGU2ZoEAABALRCilZAi/TNE2ZYZlhmWGZY6zNmO0b4cgP56ypmM9JmD7dOGGb38f7Cispmi9BmweoQ93YahtaKVkCK6MDkBgrMuAECzRNmYQ+Cdf+BwwACZkBJdZTDQk9PVE1HUiAgICAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA0KQk9PVE1HUiBpcyBtaXNzaW5n/w0KRGlzayBlcnJvcv8NClByZXNzIGFueSBrZXkgdG8gcmVzdGFydA0KAAAAAAAAAAAAAAAAAAAArMHOAABVqlJSYUEAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAByckFhAAAAAAI=")
        Private Shared ReadOnly Fat32BootMgr0x3f0 As Byte() = Convert.FromBase64String("AAAAAAAAAAAAAAAAAABVqvpmD7ZGEGaLTiRm9+FmA0YcZg+3Vg5mA8IzyWaJRvxmx0b4//////pmi0YsZoP4Ag+Cz/xmPfj//w8Pg8X8Zg+kwhD7UlD6ZsHgEGYPrNAQZoPoAmYPtl4Ni/Nm9+NmA0b8Zg+kwhD7uwAHi/u5AQDovvwPgqr8OC10HrELVr7YffOmXnQZA/mDxxU7+3LoTnXWWFroZgByq4PEBOlk/IPEBIt1CYt9D4vG+mbB4BCLx2aD+AJyO2Y9+P//D3MzZkhmSGYPtk4NZvfhZgNG/GYPpMIQ+7sAB1O5BADoUvxbD4I9/IE/TVp1CIG/AAJCSnQGvoB96Q786gACcAADwBPSA8AT0ugYAPomZosBZiX///8PZg+kwhBmPfj//w/7w78AfvpmweAQZg+s0BBmD7dOC2Yz0mb38WY7Rvh0RGaJRvhmA0YcZg+3Tg5mA8FmD7deKIPjD3QWOl4QD4Ok+1Jmi8hmi0YkZvfjZgPBWlJmD6TCEPuL37kBAOi0+1oPgp/7+4vawwAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAFWq")
        Private Shared ReadOnly Fat32BootMgr0x1800 As Byte() = Convert.FromBase64String("Zg+2RhBmi04kZvfhZgNGHGYPt1YOZgPCZolG/GbHRvT/////ZotGLGaD+AIPgsL8Zj34//8PD4O4/GZQZoPoAmYPtl4Ni/Nm9+NmA0b8uwCCi/u5AQDoo/w4LXQesQtWvml986ZedBsD+YPHFTv7cuhOddpmWOhlAHK/g8QE6XH8ACCDxASLdQmLfQ+LxmbB4BCLx2aD+AIPglb8Zj34//8PD4NM/GZQZoPoAmYPtk4NZvfhZgNG/LsAAAaOBoGA6Dn8B2ZYwesEAR6BgOgOAA+DAgBy0IpWQOoAAAAgZsHgAugRACZmiwFmJf///w9mPfj//w/DvwB+Zg+3TgtmM9Jm9/FmO0b0dDpmiUb0ZgNGHGYPt04OZgPBZg+3XiiD4w90FjpeEA+Dx/tSZovIZotGJGb342YDwVpSi9+5AQDouftai9rDAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAVao=")

        Private Const GenericRead As UInteger = &H80000000UI
        Private Const GenericWrite As UInteger = &H40000000UI
        Private Const FileShareRead As UInteger = &H1UI
        Private Const FileShareWrite As UInteger = &H2UI
        Private Const OpenExisting As UInteger = 3UI
        Private Const FileBegin As UInteger = 0UI

        <DllImport("kernel32.dll", SetLastError:=True, CharSet:=CharSet.Auto)>
        Private Shared Function CreateFile(
            lpFileName As String,
            dwDesiredAccess As UInteger,
            dwShareMode As UInteger,
            lpSecurityAttributes As IntPtr,
            dwCreationDisposition As UInteger,
            dwFlagsAndAttributes As UInteger,
            hTemplateFile As IntPtr
        ) As SafeFileHandle
        End Function

        <DllImport("kernel32.dll", SetLastError:=True)>
        Private Shared Function ReadFile(
            hFile As SafeFileHandle,
            lpBuffer As Byte(),
            nNumberOfBytesToRead As UInteger,
            ByRef lpNumberOfBytesRead As UInteger,
            lpOverlapped As IntPtr
        ) As Boolean
        End Function

        <DllImport("kernel32.dll", SetLastError:=True)>
        Private Shared Function WriteFile(
            hFile As SafeFileHandle,
            lpBuffer As Byte(),
            nNumberOfBytesToWrite As UInteger,
            ByRef lpNumberOfBytesWritten As UInteger,
            lpOverlapped As IntPtr
        ) As Boolean
        End Function

        <DllImport("kernel32.dll", SetLastError:=True)>
        Private Shared Function SetFilePointerEx(
            hFile As SafeFileHandle,
            liDistanceToMove As Long,
            lpNewFilePointer As IntPtr,
            dwMoveMethod As UInteger
        ) As Boolean
        End Function

        Private Const FsctlLockVolume As UInteger = &H90018UI
        Private Const FsctlUnlockVolume As UInteger = &H9001CUI
        Private Const FsctlDismountVolume As UInteger = &H90020UI

        <DllImport("kernel32.dll", SetLastError:=True)>
        Private Shared Function DeviceIoControl(
            hDevice As SafeFileHandle,
            dwIoControlCode As UInteger,
            lpInBuffer As IntPtr,
            nInBufferSize As UInteger,
            lpOutBuffer As IntPtr,
            nOutBufferSize As UInteger,
            ByRef lpBytesReturned As UInteger,
            lpOverlapped As IntPtr
        ) As Boolean
        End Function

        Public Shared Sub ApplyBootMgrFat32(diskNumber As Integer, driveLetter As String)
            Dim letter = driveLetter.TrimEnd("\"c).TrimEnd(":"c).Trim().ToUpperInvariant()
            If letter.Length <> 1 Then
                Throw New ArgumentException($"Letra de unidade invalida: {driveLetter}", NameOf(driveLetter))
            End If

            ' P-07: MBR e setor de boot do volume precisam ir para o mesmo dispositivo.
            ' Confere antes de gravar qualquer coisa.
            Dim volumeDisks = qdisk.Core.Services.DiskSafety.GetVolumeDiskNumbers($"{letter}:")
            If Not volumeDisks.Contains(diskNumber) Then
                Throw New IOException($"A unidade {letter}: nao pertence ao Disk {diskNumber} (discos do volume: {If(volumeDisks.Count = 0, "nenhum", String.Join(",", volumeDisks))}). Nada foi gravado.")
            End If

            WriteWindows7Mbr(diskNumber)
            WriteFat32BootMgrPbr(letter)
        End Sub

        Private Shared Sub WriteWindows7Mbr(diskNumber As Integer)
            Using stream As New FileStream($"\\.\PhysicalDrive{diskNumber}", FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite)
                Dim sector(511) As Byte
                ReadExactly(stream, sector, 0)
                If Not HasBootSignature(sector) Then
                    Throw New IOException($"Disk {diskNumber} nao contem assinatura MBR 55AA valida.")
                End If

                Array.Copy(MbrWin7, 0, sector, 0, MbrWin7.Length)
                sector(&H1BE) = &H80
                sector(&H1C2) = &H0C
                sector(510) = &H55
                sector(511) = &HAA
                WriteAt(stream, 0, sector)
            End Using
        End Sub

        Private Shared Sub WriteFat32BootMgrPbr(letter As String)
            Using handle = OpenRawHandle($"\\.\{letter}:")
                Dim sector(511) As Byte
                ReadExactly(handle, sector, 0)
                If Encoding.ASCII.GetString(sector, &H52, 8) <> "FAT32   " Then
                    Throw New IOException($"Volume {letter}: nao contem boot sector FAT32 valido.")
                End If

                ' P-07: trava o volume, como o Rufus, para nenhum processo usar o sistema de arquivos
                ' durante a gravacao. Sem trava, segue como antes (comportamento ja validado em campo).
                Dim locked = TryLockVolume(handle)

                WriteFat32BootMgrRecord(handle, 0)
                WriteFat32BootMgrRecord(handle, 6L * 512L)

                If locked Then
                    ' Desmonta para o Windows recarregar o FAT32 com o setor de boot novo, sem cache antigo.
                    Dim bytesReturned As UInteger = 0UI
                    DeviceIoControl(handle, FsctlDismountVolume, IntPtr.Zero, 0UI, IntPtr.Zero, 0UI, bytesReturned, IntPtr.Zero)
                    DeviceIoControl(handle, FsctlUnlockVolume, IntPtr.Zero, 0UI, IntPtr.Zero, 0UI, bytesReturned, IntPtr.Zero)
                End If
            End Using
        End Sub

        Private Shared Function TryLockVolume(handle As SafeFileHandle) As Boolean
            For attempt = 1 To 10
                Dim bytesReturned As UInteger = 0UI
                If DeviceIoControl(handle, FsctlLockVolume, IntPtr.Zero, 0UI, IntPtr.Zero, 0UI, bytesReturned, IntPtr.Zero) Then
                    Return True
                End If
                Threading.Thread.Sleep(100)
            Next
            Return False
        End Function

        Private Shared Sub WriteFat32BootMgrRecord(handle As SafeFileHandle, baseOffset As Long)
            Dim bootRecord((13 * 512) - 1) As Byte
            ReadExactly(handle, bootRecord, baseOffset)

            PatchBytes(bootRecord, &H0, Fat32JumpAndOem)
            PatchBytes(bootRecord, &H52, Fat32BootMgr0x52)
            PatchBytes(bootRecord, &H3F0, Fat32BootMgr0x3f0)
            PatchBytes(bootRecord, &H1800, Fat32BootMgr0x1800)

            WriteAt(handle, baseOffset, bootRecord)
        End Sub

        Private Shared Sub ReadExactly(stream As FileStream, buffer As Byte(), offset As Long)
            stream.Seek(offset, SeekOrigin.Begin)
            Dim total = 0
            While total < buffer.Length
                Dim read = stream.Read(buffer, total, buffer.Length - total)
                If read <= 0 Then
                    Throw New EndOfStreamException("Fim inesperado lendo setor de boot.")
                End If
                total += read
            End While
        End Sub

        Private Shared Sub WriteAt(stream As FileStream, offset As Long, data As Byte())
            stream.Seek(offset, SeekOrigin.Begin)
            stream.Write(data, 0, data.Length)
        End Sub

        Private Shared Sub PatchBytes(target As Byte(), offset As Integer, source As Byte())
            If offset < 0 OrElse offset + source.Length > target.Length Then
                Throw New ArgumentOutOfRangeException(NameOf(offset), "Patch excede o tamanho do boot record FAT32.")
            End If

            Array.Copy(source, 0, target, offset, source.Length)
        End Sub

        Private Shared Function OpenRawHandle(path As String) As SafeFileHandle
            Dim handle = CreateFile(path, GenericRead Or GenericWrite, FileShareRead Or FileShareWrite, IntPtr.Zero, OpenExisting, 0UI, IntPtr.Zero)
            If handle Is Nothing OrElse handle.IsInvalid Then
                Throw New IOException($"Falha ao abrir {path}. Erro Win32: {Marshal.GetLastWin32Error()}")
            End If

            Return handle
        End Function

        Private Shared Sub ReadExactly(handle As SafeFileHandle, buffer As Byte(), offset As Long)
            Seek(handle, offset)
            Dim total As UInteger = 0UI
            While total < buffer.Length
                Dim chunk(buffer.Length - CInt(total) - 1) As Byte
                Dim read As UInteger = 0UI
                If Not ReadFile(handle, chunk, CUInt(chunk.Length), read, IntPtr.Zero) Then
                    Throw New IOException($"ReadFile falhou no offset {offset + total}. Erro Win32: {Marshal.GetLastWin32Error()}")
                End If
                If read = 0UI Then
                    Throw New EndOfStreamException("Fim inesperado lendo setor de boot.")
                End If
                Array.Copy(chunk, 0, buffer, CInt(total), CInt(read))
                total += read
            End While
        End Sub

        Private Shared Sub WriteAt(handle As SafeFileHandle, offset As Long, data As Byte())
            Seek(handle, offset)
            Dim written As UInteger = 0UI
            If Not WriteFile(handle, data, CUInt(data.Length), written, IntPtr.Zero) OrElse written <> data.Length Then
                Throw New IOException($"WriteFile falhou no offset {offset}. Erro Win32: {Marshal.GetLastWin32Error()}. Written={written}/{data.Length}")
            End If
        End Sub

        Private Shared Sub Seek(handle As SafeFileHandle, offset As Long)
            If Not SetFilePointerEx(handle, offset, IntPtr.Zero, FileBegin) Then
                Throw New IOException($"SetFilePointerEx falhou no offset {offset}. Erro Win32: {Marshal.GetLastWin32Error()}")
            End If
        End Sub

        Private Shared Function HasBootSignature(sector As Byte()) As Boolean
            Return sector IsNot Nothing AndAlso sector.Length >= 512 AndAlso sector(510) = &H55 AndAlso sector(511) = &HAA
        End Function
    End Class
End Namespace

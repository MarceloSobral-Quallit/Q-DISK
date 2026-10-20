Imports System.Text.Json.Serialization

' ─────────────────────────────────────────────────────────────────────────────
' qdisk  |  Core / Models  |  CatalogItem.vb
' Item do catálogo de imagens ISO (nome, grupo, URL do arquivo e do MD5).
'
' © Quallit — Licenciado sob GPL-3.0-or-later.
' ─────────────────────────────────────────────────────────────────────────────

Namespace Models
    Public Class CatalogItem
        Public Property Enabled As Boolean = True
        Public Property Name As String
        Public Property Group As String
        Public Property FileUrl As String
        Public Property FileName As String
        Public Property Md5 As String
        Public Property Md5Url As String
        Public Property Sha256 As String
        Public Property Sha256Url As String
        Public Property ImageSizeBytes As Long
        Public Property UsbIdentification As UsbIdentificationInfo
        ''' <summary>Metodo de gravacao USB decidido no QDisk_Adm para esta ISO (P2).</summary>
        Public Property UsbWritePolicy As UsbWritePolicyInfo
        ''' <summary>Descricao funcional da imagem (o que e e para que serve), editada no QDisk_Adm e exibida no app.</summary>
        Public Property Description As String
    End Class

    Public Class UsbWritePolicyInfo
        Public Property ImageSha256 As String
        Public Property PreferredMethod As String
        Public Property AllowedMethods As List(Of String)
        ''' <summary>Alvos de boot do metodo preferido ("bios", "uefi"), para o app orientar o tecnico.</summary>
        Public Property PreferredBootTargets As List(Of String)
        Public Property PreserveSourceImage As Boolean?
        Public Property AllowContentTransformations As Boolean?
        Public Property Note As String
        ' Rotulo do pendrive na copia logica / UEFI:NTFS (RAW/DD mantem o da ISO).
        Public Property VolumeLabel As String
    End Class

    Public Class UsbIdentificationInfo
        Public Property Schema As Integer?
        Public Property Mode As String
        Public Property Modes As List(Of UsbIdentificationMode)
        Public Property BootTargets As List(Of String)
        Public Property FileCount As Integer?
        Public Property TotalBytes As Long?
        Public Property TreeHash As String
        Public Property ManifestUrl As String
        Public Property ManifestSha256 As String
        Public Property SentinelFiles As List(Of UsbManifestFile)
        Public Property ImageSha256 As String
        Public Property SizeBytes As Long?

        <JsonIgnore>
        Public ReadOnly Property EffectiveModes As IReadOnlyList(Of UsbIdentificationMode)
            Get
                If Modes IsNot Nothing AndAlso Modes.Count > 0 Then
                    Return Modes
                End If

                If String.IsNullOrWhiteSpace(Mode) Then
                    Return Array.Empty(Of UsbIdentificationMode)()
                End If

                Return New List(Of UsbIdentificationMode) From {
                    New UsbIdentificationMode With {
                        .Schema = Schema,
                        .Mode = Mode,
                        .BootTargets = BootTargets,
                        .FileCount = FileCount,
                        .TotalBytes = TotalBytes,
                        .TreeHash = TreeHash,
                        .ManifestUrl = ManifestUrl,
                        .ManifestSha256 = ManifestSha256,
                        .SentinelFiles = SentinelFiles,
                        .ImageSha256 = ImageSha256,
                        .SizeBytes = SizeBytes
                    }
                }
            End Get
        End Property
    End Class

    Public Class UsbIdentificationMode
        Public Property Schema As Integer?
        Public Property Mode As String
        Public Property BootTargets As List(Of String)
        Public Property FileCount As Integer?
        Public Property TotalBytes As Long?
        Public Property TreeHash As String
        Public Property ManifestUrl As String
        Public Property ManifestSha256 As String
        Public Property SentinelFiles As List(Of UsbManifestFile)
        Public Property ImageSha256 As String
        Public Property SizeBytes As Long?
    End Class

    Public Class UsbManifestFile
        Public Property Path As String
        Public Property Size As Long?
        Public Property Sha256 As String
    End Class
End Namespace


' ─────────────────────────────────────────────────────────────────────────────
' qdisk  |  Core / Models  |  UsbDriveInfo.vb
' Dados de um pen drive USB detectado via WMI (número, letra, tamanho, modelo).
'
' © Quallit — Licenciado sob GPL-3.0-or-later.
' ─────────────────────────────────────────────────────────────────────────────

Namespace Models
    Public Class UsbDriveInfo
        Public Property DriveLetter As String
        Public Property Label As String
        Public Property SizeBytes As Long
        Public Property IsWritable As Boolean
    End Class
End Namespace


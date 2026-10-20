' ─────────────────────────────────────────────────────────────────────────────
' qdisk  |  Core / Models  |  AppState.vb
' Estado global compartilhado entre as views durante a sessão.
'
' © Quallit — Licenciado sob GPL-3.0-or-later.
' ─────────────────────────────────────────────────────────────────────────────

Namespace Models
    Public Class AppState
        Public Property AuthenticatedUser As String
        Public Property UserProfile As UserProfile
        Public Property SelectedUsbDrive As UsbDriveInfo
        Public Property CacheItemsCount As Integer
        Public Property UpdateStatus As String
    End Class
End Namespace


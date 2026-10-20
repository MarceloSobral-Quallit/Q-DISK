' ─────────────────────────────────────────────────────────────────────────────
' qdisk  |  Core / Models  |  AppSettings.vb
' Configurações persistíveis da aplicação com valores padrão definidos.
'
' © Quallit — Licenciado sob GPL-3.0-or-later.
' ─────────────────────────────────────────────────────────────────────────────

Imports System.IO

Namespace Models
    Public Class AppSettings
        Public Property AutoValidateSha256AfterTibxCopy As Boolean = True
        Public Property AutoVerifyExistingTibxBeforeCopy As Boolean = True
        Public Property AutoValidateMd5AfterCacheRefresh As Boolean
        ' "Verificar atualizacoes de imagens na inicializacao": ligada por padrao, como a verificacao do app.
        Public Property AutoValidateMd5OnStartup As Boolean = True
        Public Property AutoValidateSha256BeforeIsoBurn As Boolean
        Public Property AutoDisableAutoplayDuringUsbOps As Boolean
        Public Property AutoCalculateSha256AfterDownload As Boolean
        Public Property AutoVerifySha256AfterUsbBurn As Boolean = True
        Public Property AutoUpdateApp As Boolean = True
        Public Property ImageCacheDir As String = GetDefaultImageCacheDir()
        Public Property EnableMultiConnectionDownload As Boolean = True
        Public Property MaxDownloadConnections As Integer = 8
        Public Property ShowIdentifyUsbButton As Boolean = False
        Public Property ShowValidateBootButton As Boolean = False

        Public Shared Function CreateDefault() As AppSettings
            Return New AppSettings() With {
                .AutoValidateSha256AfterTibxCopy = True,
                .AutoVerifyExistingTibxBeforeCopy = True,
                .AutoValidateMd5AfterCacheRefresh = True,
                .AutoValidateMd5OnStartup = True,
                .AutoValidateSha256BeforeIsoBurn = True,
                .AutoDisableAutoplayDuringUsbOps = True,
                .AutoCalculateSha256AfterDownload = True,
                .AutoVerifySha256AfterUsbBurn = True,
                .AutoUpdateApp = True,
                .ImageCacheDir = GetDefaultImageCacheDir(),
                .EnableMultiConnectionDownload = True,
                .MaxDownloadConnections = 8,
                .ShowIdentifyUsbButton = False,
                .ShowValidateBootButton = False
            }
        End Function

        Public Shared Function GetDefaultImageCacheDir() As String
            Dim userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
            If Not String.IsNullOrWhiteSpace(userProfile) Then
                Return Path.Combine(userProfile, "Downloads", "qdisk")
            End If

            Return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                                "qdisk",
                                "cache")
        End Function
    End Class
End Namespace


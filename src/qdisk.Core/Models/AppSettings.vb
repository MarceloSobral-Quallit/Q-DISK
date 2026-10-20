' ─────────────────────────────────────────────────────────────────────────────
' qdisk  |  Core / Models  |  AppSettings.vb
' Configurações persistíveis da aplicação com valores padrão definidos.
'
' © Quallit — Licenciado sob GPL-3.0-or-later.
' ─────────────────────────────────────────────────────────────────────────────

Imports System.IO

Namespace Models
    Public Class AppSettings
        Public Property AutoValidateMd5AfterDownload As Boolean
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
        Public Property LegacyImageCacheMigrationPrompted As Boolean = False
        Public Property EnableMultiConnectionDownload As Boolean = True
        Public Property MaxDownloadConnections As Integer = 8
        Public Property ShowIdentifyUsbButton As Boolean = False
        Public Property ShowValidateBootButton As Boolean = False
        ' Marca do ajuste unico que ligou a verificacao de imagens para configuracoes ja salvas (antes ela nao fazia nada
        ' e todo settings.json gravava False).
        Public Property ImageUpdateCheckDefaultApplied As Boolean = False

        Public Shared Function CreateDefault() As AppSettings
            Return New AppSettings() With {
                .AutoValidateMd5AfterDownload = True,
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
                .LegacyImageCacheMigrationPrompted = False,
                .EnableMultiConnectionDownload = True,
                .MaxDownloadConnections = 8,
                .ShowIdentifyUsbButton = False,
                .ShowValidateBootButton = False,
                .ImageUpdateCheckDefaultApplied = True
            }
        End Function

        ''' <summary>
        ''' Ajustes de padrao aplicados uma vez a configuracoes ja salvas. True se mudou algo (a configuracao deve ser
        ''' gravada). Liga a verificacao de imagens na abertura: ate a 3.11 a opcao era gravada como False sem ter efeito.
        ''' </summary>
        Public Shared Function ApplyDefaultUpgrades(settings As AppSettings) As Boolean
            If settings Is Nothing OrElse settings.ImageUpdateCheckDefaultApplied Then Return False
            settings.AutoValidateMd5OnStartup = True
            settings.ImageUpdateCheckDefaultApplied = True
            Return True
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


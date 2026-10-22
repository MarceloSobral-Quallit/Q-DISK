' ─────────────────────────────────────────────────────────────────────────────
' qdisk  |  UI / Views  |  UsbBootView.vb
' View de gravação de imagem ISO em USB — modos Raw (P/Invoke) e Logical (DiscUtils).
'
' © Quallit — Licenciado sob GPL-3.0-or-later.
' ─────────────────────────────────────────────────────────────────────────────

Imports System.Drawing
Imports System.IO
Imports System.Windows.Forms
Imports qdisk.UI.Theme
Imports System.Net.Http
Imports System.Text.RegularExpressions
Imports qdisk.Core.Services
Imports qdisk.Core.Models
Imports qdisk.Core
Imports System.Threading
Imports System.Management
Imports System.Runtime.InteropServices
Imports System.Security.Principal
Imports Microsoft.Win32
Imports Microsoft.Win32.SafeHandles
Imports DiscUtils.Iso9660
Imports DiscUtils.Udf
Imports System.ComponentModel
Imports System.Text.Json

' Enum para tipo de ISO detectado
Public Enum IsoType
    Unknown
    Windows
    Linux
    Hybrid
End Enum

' P/Invoke declarations para acesso a dispositivos físicos como o rpi-imager
Public Class NativeMethods
    <DllImport("kernel32.dll", SetLastError:=True, CharSet:=CharSet.Auto)>
    Public Shared Function CreateFile(
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
    Public Shared Function GetLastError() As UInteger
    End Function

    <DllImport("kernel32.dll", SetLastError:=True)>
    Public Shared Function WriteFile(
        hFile As SafeFileHandle,
        lpBuffer As Byte(),
        nNumberOfBytesToWrite As UInteger,
        ByRef lpNumberOfBytesWritten As UInteger,
        lpOverlapped As IntPtr
    ) As Boolean
    End Function

    ' IOCTL constants para limpeza de disco (como o rpi-imager)
    Public Const FSCTL_DISMOUNT_VOLUME As UInteger = &H90020
    Public Const IOCTL_DISK_DELETE_DRIVE_LAYOUT As UInteger = &H7C100
    Public Const IOCTL_STORAGE_GET_DEVICE_NUMBER As UInteger = &H2D1080
    ' CTL_CODE(IOCTL_DISK_BASE=7, 0x50, METHOD_BUFFERED, FILE_ANY_ACCESS). O valor antigo (&H14000) era invalido
    ' e toda chamada falhava com ERROR_NOT_SUPPORTED (50), o que foi interpretado como limitacao do USB (P-01).
    Public Const IOCTL_DISK_UPDATE_PROPERTIES As UInteger = &H70140UI
    ' IOCTL para tamanho e geometria do disco (disk_formatter.cpp)
    Public Const IOCTL_DISK_GET_DRIVE_GEOMETRY_EX As UInteger = &H700A0UI
    ' Constantes de criação de arquivo
    Public Const CREATE_ALWAYS As UInteger = 2UI
    Public Const OPEN_ALWAYS As UInteger = 4UI
    
    <DllImport("kernel32.dll", SetLastError:=True)>
    Public Shared Function DeviceIoControl(
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
    
    <DllImport("kernel32.dll", SetLastError:=True, CharSet:=CharSet.Auto)>
    Public Shared Function DeleteVolumeMountPointW(
        lpszVolumeMountPoint As String
    ) As Boolean
    End Function

    ' FSCTL para operações de volume (sequência rpi-imager)
    Public Const FSCTL_LOCK_VOLUME As UInteger = &H90018UI
    Public Const FSCTL_UNLOCK_VOLUME As UInteger = &H9001CUI
    Public Const FSCTL_ALLOW_EXTENDED_DASD_IO As UInteger = &H90083UI

    ' SHChangeNotify — notifica o Explorer sobre remoção de mídia/drive
    Public Const SHCNE_MEDIAREMOVED As Integer = &H200
    Public Const SHCNE_DRIVEREMOVED As Integer = &H80
    Public Const SHCNF_PATH As UInteger = &H1UI
    Public Const SHCNF_FLUSH As UInteger = &H1000UI

    <DllImport("shell32.dll", CharSet:=CharSet.Auto)>
    Public Shared Sub SHChangeNotify(wEventId As Integer, uFlags As UInteger, dwItem1 As String, dwItem2 As IntPtr)
    End Sub

    ' SetThreadErrorMode — suprime popups "Inserir disco..." durante operações de formato
    Public Const SEM_FAILCRITICALERRORS As UInteger = &H1UI
    Public Const SEM_NOOPENFILEERRORBOX As UInteger = &H8000UI

    <DllImport("kernel32.dll", SetLastError:=True)>
    Public Shared Function SetThreadErrorMode(dwNewMode As UInteger, ByRef lpOldMode As UInteger) As Boolean
    End Function

    ' SetFilePointerEx — posicionamento de offset em dispositivo físico (overlapped I/O)
    <DllImport("kernel32.dll", SetLastError:=True)>
    Public Shared Function SetFilePointerEx(
        hFile As SafeFileHandle,
        liDistanceToMove As Long,
        lpNewFilePointer As IntPtr,
        dwMoveMethod As UInteger
    ) As Boolean
    End Function

    ' DISK_GEOMETRY_EX — tamanho real do disco físico (disk_formatter::GetSize)
    <StructLayout(LayoutKind.Sequential)>
    Public Structure DISK_GEOMETRY
        Public Cylinders As Long
        Public MediaType As Integer
        Public TracksPerCylinder As UInteger
        Public SectorsPerTrack As UInteger
        Public BytesPerSector As UInteger
    End Structure

    <StructLayout(LayoutKind.Sequential)>
    Public Structure DISK_GEOMETRY_EX
        Public Geometry As DISK_GEOMETRY
        Public DiskSize As Long     ' LARGE_INTEGER = tamanho total em bytes
        ' Seguido de dados extras — 4 bytes dummy para alinhar
        Public Data As Integer
    End Structure

    ' Constantes do Windows para acesso a dispositivos
    Public Const GENERIC_READ As UInteger = &H80000000UI
    Public Const GENERIC_WRITE As UInteger = &H40000000UI
    Public Const FILE_SHARE_READ As UInteger = &H1UI
    Public Const FILE_SHARE_WRITE As UInteger = &H2UI
    Public Const FILE_SHARE_DELETE As UInteger = &H4UI
    Public Const OPEN_EXISTING As UInteger = 3UI
    Public Const FILE_ATTRIBUTE_NORMAL As UInteger = &H80UI
    Public Const FILE_FLAG_SEQUENTIAL_SCAN As UInteger = &H8000000UI
    Public Const FILE_FLAG_WRITE_THROUGH As UInteger = &H80000000UI
    Public Const FILE_FLAG_OVERLAPPED As UInteger = &H40000000UI
    Public Const FILE_FLAG_NO_BUFFERING As UInteger = &H20000000UI

    ' Códigos de erro
    Public Const ERROR_ACCESS_DENIED As UInteger = 5UI
    Public Const ERROR_SHARING_VIOLATION As UInteger = 32UI

    ' FindFirstVolumeW / FindNextVolumeW / FindVolumeClose — enumerar volumes montados
    ' Usados em WaitForVolumeMountAsync para detectar nova letra após format nativo.
    <DllImport("kernel32.dll", SetLastError:=True, CharSet:=CharSet.Unicode)>
    Public Shared Function FindFirstVolumeW(lpszVolumeName As System.Text.StringBuilder, cchBufferLength As UInteger) As IntPtr
    End Function

    <DllImport("kernel32.dll", SetLastError:=True, CharSet:=CharSet.Unicode)>
    Public Shared Function FindNextVolumeW(hFindVolume As IntPtr, lpszVolumeName As System.Text.StringBuilder, cchBufferLength As UInteger) As Boolean
    End Function

    <DllImport("kernel32.dll", SetLastError:=True)>
    Public Shared Function FindVolumeClose(hFindVolume As IntPtr) As Boolean
    End Function

    ' GetVolumePathNamesForVolumeNameW — mapeia GUID de volume para letra(s) de unidade
    <DllImport("kernel32.dll", SetLastError:=True, CharSet:=CharSet.Unicode)>
    Public Shared Function GetVolumePathNamesForVolumeNameW(
        lpszVolumeName As String,
        lpszVolumePathNames As System.Text.StringBuilder,
        cchBufferLength As UInteger,
        ByRef lpcchReturnLength As UInteger
    ) As Boolean
    End Function

    ' GetVolumeNameForVolumeMountPointW — converte letra em GUID de volume
    <DllImport("kernel32.dll", SetLastError:=True, CharSet:=CharSet.Unicode)>
    Public Shared Function GetVolumeNameForVolumeMountPointW(
        lpszVolumeMountPoint As String,
        lpszVolumeName As System.Text.StringBuilder,
        cchBufferLength As UInteger
    ) As Boolean
    End Function

    ' DeviceIoControl — sobrecarga com buffer de saída Byte() (para IOCTL_DISK_GET_DRIVE_GEOMETRY_EX)
    <DllImport("kernel32.dll", SetLastError:=True)>
    Public Shared Function DeviceIoControl(
        hDevice As SafeFileHandle,
        dwIoControlCode As UInteger,
        lpInBuffer As IntPtr,
        nInBufferSize As UInteger,
        <Out> lpOutBuffer As Byte(),
        nOutBufferSize As UInteger,
        ByRef lpBytesReturned As UInteger,
        lpOverlapped As IntPtr
    ) As Boolean
    End Function

    ' Constante FILE_BEGIN para SetFilePointerEx
    Public Const FILE_BEGIN As UInteger = 0UI

    ' CM_Locate_DevNodeW / CM_Reenumerate_DevNode — forçar re-enumeração PnP após format USB.
    ' Reforco ao IOCTL_DISK_UPDATE_PROPERTIES para garantir a remontagem dos volumes.
    ' CM_Reenumerate_DevNode faz o kernel reescanear o disco e remontar os volumes.
    ' CR_SUCCESS = 0
    <DllImport("cfgmgr32.dll", SetLastError:=False, CharSet:=CharSet.Unicode)>
    Public Shared Function CM_Locate_DevNodeW(ByRef pdnDevInst As UInteger, pDeviceID As String, ulFlags As UInteger) As UInteger
    End Function

    <DllImport("cfgmgr32.dll", SetLastError:=False)>
    Public Shared Function CM_Reenumerate_DevNode(dnDevInst As UInteger, ulFlags As UInteger) As UInteger
    End Function
End Class

Namespace Views
    Partial Public Class UsbBootView
        Inherits UserControl

        Public Enum UsbOperationViewMode
            All
            CopyTibx
            BootIso
        End Enum

        Private Enum UsbVerificationStatus
            Success
            Failed
            Inconclusive
        End Enum

        Private Enum UsbBootTarget
            LegacyBios
            Uefi
        End Enum

        Private Enum UsbBootCreationMethod
            LegacyBios
            Uefi
            UefiNtfs
            RawDd
        End Enum

        Private _isoAnalysisRunning As Integer = 0

        Private NotInheritable Class UsbVerificationResult
            Public Property Status As UsbVerificationStatus
            Public Property Message As String
        End Class

        Private NotInheritable Class RawUsbIdentificationResult
            Public Property CatalogName As String
            Public Property ImagePath As String
            Public Property ImageSha256 As String
            Public Property ImageSizeBytes As Long
            Public Property CalculatedUsbSha256 As String
            Public Property Details As New List(Of String)()
        End Class

        Private NotInheritable Class LogicalUsbIdentificationResult
            Public Property CatalogName As String
            Public Property ModeName As String
            Public Property Confidence As String
            Public Property Status As UsbVerificationStatus
            Public Property ManifestSource As String
            Public Property FilesVerified As Integer
            Public Property SizeOnlyVerified As Integer
            Public Property MissingCount As Integer
            Public Property MismatchCount As Integer
            Public Property InaccessibleCount As Integer
            Public Property TreeHash As String
            Public Property ImageSha256 As String
            Public Property Details As New List(Of String)()
        End Class

        Private NotInheritable Class RawCatalogCandidate
            Public Property Item As CatalogItem
            Public Property ImageSha256 As String
            Public Property SizeBytes As Long
            Public Property Source As String
        End Class

        ' HttpClient compartilhado — evita esgotamento de sockets por instâncias descartadas.
        Private Shared ReadOnly _md5Client As HttpClient = CreateMd5Client()
        Private Shared Function CreateMd5Client() As HttpClient
            Dim c As New HttpClient() With {.Timeout = TimeSpan.FromSeconds(20)}
            HttpIdentity.Apply(c.DefaultRequestHeaders, "QuallitDownloader/1.0")
            Return c
        End Function
        Private ReadOnly _hashService As HashService
        Private ReadOnly _settingsService As SettingsService
        Private ReadOnly _catalogService As CatalogService
        Private ReadOnly _logService As LogService
        Private ReadOnly _isoAnalysisService As IsoAnalysisService
        Private ReadOnly _usbWriteMethodAdvisor As New UsbWriteMethodAdvisor()
        Private ReadOnly _usbWritePolicyResolver As New UsbWritePolicyResolver()
        Private ReadOnly _viewMode As UsbOperationViewMode
        Private _lastUsbSnapshot As HashSet(Of String)
        Private _usbOperationCts As CancellationTokenSource
        ' Identidade do disco confirmado para a operacao destrutiva em curso (D-01).
        Private _targetDisk As DiskIdentity
        ' P-06: volumes travados e desmontados mantidos durante a gravacao RAW.
        Private ReadOnly _heldVolumeLocks As New List(Of SafeFileHandle)()

        Public Sub New()
            Me.New(UsbOperationViewMode.All)
        End Sub

        Public Sub New(viewMode As UsbOperationViewMode)
            InitializeComponent()
            _viewMode = viewMode
            _hashService = New HashService()
            _settingsService = New SettingsService()
            _catalogService = New CatalogService()
            _logService = New LogService()
            _isoAnalysisService = New IsoAnalysisService()
            Dim gifPath = Path.Combine(AppPaths.AssetsDir, "run.gif")
            If File.Exists(gifPath) Then
                _busyGif.Image = Image.FromFile(gifPath)
            End If

            _statusLabel.Text = "Clique em Atualizar USB para carregar os dados."
            ApplyViewMode()
            AddHandler _tibxList.SelectedIndexChanged, AddressOf OnImageSelectionChanged
            AddHandler _isoList.SelectedIndexChanged, AddressOf OnImageSelectionChanged
            UpdateAdminUi()
            ApplyRedesign()
        End Sub

        ''' <summary>Redesenho das telas USB (HANDOFF 4.2/4.3). Reorganiza os mesmos controles; handlers e logica nao mudam.</summary>
        Private Sub ApplyRedesign()
            Dim isTibx = _viewMode = UsbOperationViewMode.CopyTibx
            Dim isAdmin = IsRunningAsAdmin()
            LayoutRoot.SuspendLayout()

            ' Linhas: 0 aviso | 1 USB | 2 info | 3 secao | 4 conteudo | 5 status | 6 progresso
            LayoutRoot.RowCount = 7
            LayoutRoot.RowStyles.Clear()
            LayoutRoot.RowStyles.Add(New RowStyle(SizeType.Absolute, If(isAdmin, 0.0F, 58.0F)))
            LayoutRoot.RowStyles.Add(New RowStyle(SizeType.Absolute, 40.0F))
            LayoutRoot.RowStyles.Add(New RowStyle(SizeType.Absolute, 26.0F))
            LayoutRoot.RowStyles.Add(New RowStyle(SizeType.Absolute, 30.0F))
            LayoutRoot.RowStyles.Add(New RowStyle(SizeType.Percent, 100.0F))
            LayoutRoot.RowStyles.Add(New RowStyle(SizeType.Absolute, 40.0F))
            LayoutRoot.RowStyles.Add(New RowStyle(SizeType.Absolute, 42.0F))

            ' 0: faixa de aviso do Modo Administrador
            UiTheme.StyleButton(_adminRestartButton, ButtonKind.Primary, UiTheme.Icon16(Me, "shield", "white"))
            _adminRestartButton.AutoSize = False
            _adminRestartButton.Width = TextRenderer.MeasureText(_adminRestartButton.Text, _adminRestartButton.Font).Width + 48
            Dim banner = UiTheme.CreateWarningBanner(_adminWarningLabel, _adminRestartButton, UiTheme.Icon16(Me, "alert", "warn"))
            banner.Margin = New Padding(0, 0, 0, 12)
            UiStatus.ReplaceInParent(AdminPanel, banner)
            banner.Visible = Not isAdmin

            ' 1: etapa 1 + "USB:" + combo + Atualizar USB
            Dim usbRow = New TableLayoutPanel() With {.Dock = DockStyle.Fill, .ColumnCount = 4, .RowCount = 1, .Margin = New Padding(0)}
            usbRow.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, 28.0F))
            usbRow.ColumnStyles.Add(New ColumnStyle(SizeType.AutoSize))
            usbRow.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100.0F))
            usbRow.ColumnStyles.Add(New ColumnStyle(SizeType.AutoSize))
            usbRow.RowStyles.Add(New RowStyle(SizeType.Percent, 100.0F))
            HeaderPanel.Controls.Clear()
            usbRow.Controls.Add(UiTheme.CreateStepBadge(1), 0, 0)
            UiTheme.StyleSectionLabel(DriveLabel)
            DriveLabel.AutoSize = True
            DriveLabel.Padding = New Padding(0)
            DriveLabel.Anchor = AnchorStyles.Left
            DriveLabel.Margin = New Padding(0, 0, 8, 0)
            usbRow.Controls.Add(DriveLabel, 1, 0)
            ' Combo e botao presos ao topo com a mesma margem: centralizado, o combo ficava 4 px abaixo do botao
            ' (o layout usa uma altura do combo menor que a desenhada pelo RoundedComboPainter).
            Dim usbRowTop = (40 - UiTheme.ButtonHeight) \ 2
            _driveCombo.Anchor = AnchorStyles.Left Or AnchorStyles.Right Or AnchorStyles.Top
            _driveCombo.Margin = New Padding(0, usbRowTop, 8, 0)
            RoundedComboPainter.Attach(_driveCombo)
            usbRow.Controls.Add(_driveCombo, 2, 0)
            UiStatus.StyleAutoButton(_refreshButton, ButtonKind.Secondary, UiTheme.Icon16(Me, "refresh", "text"))
            _refreshButton.Anchor = AnchorStyles.Right Or AnchorStyles.Top
            _refreshButton.Margin = New Padding(0, usbRowTop, 0, 0)
            usbRow.Controls.Add(_refreshButton, 3, 0)
            UiStatus.ReplaceInParent(HeaderPanel, usbRow)

            ' 2: info alinhada com "USB:"
            _infoLabel.Margin = New Padding(28, 0, 0, 0)
            _infoLabel.ForeColor = UiTheme.StatusText

            ' 3: etapa 2 + titulo da lista (texto do GroupBox)
            Dim sectionRow = New FlowLayoutPanel() With {.Dock = DockStyle.Fill, .WrapContents = False, .Margin = New Padding(0)}
            sectionRow.Controls.Add(UiTheme.CreateStepBadge(2))
            Dim sectionLabel = New Label() With {.Text = If(isTibx, TibxGroup.Text, IsoGroup.Text), .AutoSize = True, .Margin = New Padding(0, 2, 0, 0)}
            UiTheme.StyleSectionLabel(sectionLabel)
            sectionRow.Controls.Add(sectionLabel)
            LayoutRoot.Controls.Add(sectionRow, 0, 3)

            ' 4: lista (sem moldura de GroupBox) + coluna de acoes
            Dim list = If(isTibx, _tibxList, _isoList)
            Dim listInner As Panel = Nothing
            Dim listBox = UiTheme.CreateBorderedPanel(UiTheme.BorderLight, Color.White, listInner)
            listBox.Dock = DockStyle.Fill
            listBox.Margin = New Padding(0, 0, 16, 0)
            list.BorderStyle = BorderStyle.None
            list.Parent?.Controls.Remove(list)
            listInner.Padding = New Padding(4)
            listInner.Controls.Add(list)
            ' Descricao da imagem selecionada (catalogo) e "Como gravar", abaixo da lista.
            ' TextBox somente leitura (em vez de Label) para permitir rolagem vertical quando o texto
            ' passa da altura maxima.
            _imageDescriptionPanel = New Panel() With {
                .Dock = DockStyle.Bottom,
                .Height = 72,
                .Padding = New Padding(8, 6, 2, 6),
                .BackColor = Color.FromArgb(246, 248, 250)
            }
            _imageDescriptionBox = New TextBox() With {
                .Dock = DockStyle.Fill,
                .Multiline = True,
                .ReadOnly = True,
                .WordWrap = True,
                .BorderStyle = BorderStyle.None,
                .ScrollBars = ScrollBars.None,
                .TabStop = False,
                .BackColor = _imageDescriptionPanel.BackColor,
                .ForeColor = UiTheme.StatusText,
                .Text = String.Empty
            }
            _imageDescriptionPanel.Controls.Add(_imageDescriptionBox)
            listInner.Controls.Add(_imageDescriptionPanel)
            AddHandler listInner.Resize, Sub() FitImageDescriptionHeight()
            list.BringToFront()

            Dim mainButton = If(isTibx, _copyButton, _bootButton)
            Dim secondaries As Button() = If(isTibx, New Button() {_verifyTibxButton}, New Button() {_identifyUsbButton, _verifyBootButton})
            UiTheme.StyleButton(mainButton, ButtonKind.Primary, UiTheme.Icon16(Me, If(isTibx, "copy", "disc"), "white"))
            UiTheme.StyleButton(_verifyTibxButton, ButtonKind.Secondary, UiTheme.Icon16(Me, "shield", "text"))
            UiTheme.StyleButton(_identifyUsbButton, ButtonKind.Secondary, UiTheme.Icon16(Me, "usb", "text"))
            UiTheme.StyleButton(_verifyBootButton, ButtonKind.Secondary, UiTheme.Icon16(Me, "shield", "text"))
            UiTheme.StyleButton(_zeroUsbButton, ButtonKind.Danger, UiTheme.Icon16(Me, "eraser", "red"))

            Dim actions = New TableLayoutPanel() With {.Dock = DockStyle.Fill, .ColumnCount = 1, .Margin = New Padding(0), .Padding = New Padding(0)}
            actions.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100.0F))
            Dim ordered = New List(Of Button) From {mainButton}
            ordered.AddRange(secondaries)
            For Each b In ordered
                b.Parent?.Controls.Remove(b)
                b.Dock = DockStyle.Top
                b.Margin = New Padding(0, 0, 0, UiTheme.ButtonGap)
                actions.RowStyles.Add(New RowStyle(SizeType.AutoSize))
                actions.Controls.Add(b, 0, actions.RowStyles.Count - 1)
            Next
            actions.RowStyles.Add(New RowStyle(SizeType.Percent, 100.0F))
            Dim zeroDivider = UiTheme.CreateDivider()
            zeroDivider.Dock = DockStyle.Bottom
            Dim zeroHost = New Panel() With {.Dock = DockStyle.Fill, .Margin = New Padding(0), .Padding = New Padding(0, 0, 0, 10)}
            zeroHost.Controls.Add(zeroDivider)
            actions.Controls.Add(zeroHost, 0, actions.RowStyles.Count - 1)
            _zeroUsbButton.Parent?.Controls.Remove(_zeroUsbButton)
            _zeroUsbButton.Dock = DockStyle.Fill
            _zeroUsbButton.Margin = New Padding(0)
            actions.RowStyles.Add(New RowStyle(SizeType.Absolute, CSng(UiTheme.ButtonHeight)))
            actions.Controls.Add(_zeroUsbButton, 0, actions.RowStyles.Count - 1)
            actions.RowCount = actions.RowStyles.Count

            ' Zerar USB desabilitado: o ToolTip do WinForms nao aparece em controle desabilitado,
            ' entao o MouseMove e tratado no conteiner.
            Dim zeroTip = New ToolTip()
            Dim tipShown = False
            AddHandler actions.MouseMove,
                Sub(sender As Object, e As MouseEventArgs)
                    Dim child = actions.GetChildAtPoint(e.Location)
                    Dim over = child Is _zeroUsbButton AndAlso Not _zeroUsbButton.Enabled AndAlso Not IsRunningAsAdmin()
                    If over AndAlso Not tipShown Then
                        zeroTip.Show("Requer Modo Administrador.", actions, e.X + 12, e.Y + 18)
                        tipShown = True
                    ElseIf Not over AndAlso tipShown Then
                        zeroTip.Hide(actions)
                        tipShown = False
                    End If
                End Sub
            AddHandler actions.MouseLeave, Sub()
                                                zeroTip.Hide(actions)
                                                tipShown = False
                                            End Sub

            ContentLayout.Controls.Clear()
            ' Os paineis retirados do layout continuam na tela, ocultos: a lista da outra tela (TIBX ou ISO)
            ' e os controles ocultos precisam de pai, senao o ListBox fica sem BindingContext, o DataSource
            ' nao preenche os itens e LoadCacheFiles quebra em SelectedIndex = 0.
            For Each detached As Control In New Control() {ListsLayout, ActionsPanel}
                detached.Visible = False
                Controls.Add(detached)
            Next
            ContentLayout.ColumnStyles.Clear()
            ContentLayout.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100.0F))
            ContentLayout.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, 184.0F))
            ContentLayout.Margin = New Padding(0)
            ContentLayout.Controls.Add(listBox, 0, 0)
            ContentLayout.Controls.Add(actions, 1, 0)
            LayoutRoot.SetRow(ContentLayout, 4)

            ' 5: divisoria + linha de status com tipo
            Dim statusHost = New Panel() With {.Dock = DockStyle.Fill, .Margin = New Padding(0), .Padding = New Padding(0, 12, 0, 0)}
            LayoutRoot.SetRow(_statusLabel, 5)
            Dim line = UiStatus.AttachStatusLine(_statusLabel, Me)
            UiStatus.ReplaceInParent(line, statusHost)
            Dim statusDivider = UiTheme.CreateDivider()
            statusDivider.Dock = DockStyle.Top
            line.Dock = DockStyle.Fill
            statusHost.Controls.Add(line)
            statusHost.Controls.Add(statusDivider)
            Dim dividerGap = New Panel() With {.Dock = DockStyle.Top, .Height = 6}
            statusHost.Controls.Add(dividerGap)
            dividerGap.BringToFront()
            line.BringToFront()
            Controls.Add(_statusLabel)
            ' Texto longo quebra em mais linhas (sem "...") e a faixa da status cresce para caber.
            line.TextLabel.AutoEllipsis = False
            Dim lastStatusWidth = -1
            AddHandler _statusLabel.TextChanged, Sub() FitStatusRowHeight(line)
            AddHandler line.TextLabel.SizeChanged,
                Sub()
                    If line.TextLabel.Width = lastStatusWidth Then Return
                    lastStatusWidth = line.TextLabel.Width
                    FitStatusRowHeight(line)
                End Sub

            ' 6: run.gif | barra | Cancelar
            LayoutRoot.SetRow(ProgressPanel, 6)
            ProgressPanel.Margin = New Padding(0)
            ProgressPanel.ColumnStyles(2).Width = 112.0F
            _progressBar.Dock = DockStyle.None
            _progressBar.Anchor = AnchorStyles.Left Or AnchorStyles.Right
            _progressBar.Height = 22
            UiTheme.StyleButton(_cancelOperationButton, ButtonKind.Secondary, UiTheme.Icon16(Me, "x", "text"))
            _cancelOperationButton.Size = New Size(104, UiTheme.ButtonHeight)
            _cancelOperationButton.Anchor = AnchorStyles.Right
            _cancelOperationButton.Margin = New Padding(8, 0, 0, 0)

            LayoutRoot.ResumeLayout(True)
            UpdateImageActionButtons()
        End Sub

        Private Sub ApplyViewMode()
            Select Case _viewMode
                Case UsbOperationViewMode.CopyTibx
                    TibxGroup.Visible = True
                    IsoGroup.Visible = False
                    ListsLayout.RowStyles(0).SizeType = SizeType.Percent
                    ListsLayout.RowStyles(0).Height = 100.0!
                    ListsLayout.RowStyles(1).SizeType = SizeType.Percent
                    ListsLayout.RowStyles(1).Height = 0.0!

                    ActionsTopPanel.Visible = True
                    ActionsBottomPanel.Visible = False
                    ActionsPanel.RowStyles(0).SizeType = SizeType.Percent
                    ActionsPanel.RowStyles(0).Height = 100.0!
                    ActionsPanel.RowStyles(1).SizeType = SizeType.Percent
                    ActionsPanel.RowStyles(1).Height = 0.0!

                    _copyButton.Text = "Copiar TIBX para USB"
                    _copyButton.Width = 150
                    _verifyTibxButton.Text = "Verificar TIBX no USB"
                    _verifyTibxButton.Width = 150
                    _verifyTibxButton.Visible = True
                    _zeroUsbButton.Text = "Zerar USB"
                    _zeroUsbButton.Width = 150
                    ConfigureActionsWithBottomZero(ActionsTopPanel, _copyButton, _verifyTibxButton)
                    _adminWarningLabel.Text = "Aviso: Ative o Modo Administrador para formatar ou preparar o USB."

                Case UsbOperationViewMode.BootIso
                    TibxGroup.Visible = False
                    IsoGroup.Visible = True
                    ListsLayout.RowStyles(0).SizeType = SizeType.Percent
                    ListsLayout.RowStyles(0).Height = 0.0!
                    ListsLayout.RowStyles(1).SizeType = SizeType.Percent
                    ListsLayout.RowStyles(1).Height = 100.0!

                    ActionsTopPanel.Visible = False
                    ActionsBottomPanel.Visible = True
                    ActionsPanel.RowStyles(0).SizeType = SizeType.Percent
                    ActionsPanel.RowStyles(0).Height = 0.0!
                    ActionsPanel.RowStyles(1).SizeType = SizeType.Percent
                    ActionsPanel.RowStyles(1).Height = 100.0!

                    _bootButton.Text = "Gravar ISO Bootável"
                    _bootButton.Width = 150
                    _verifyBootButton.Text = "Validar Boot do USB"
                    _verifyBootButton.Width = 150
                    _zeroUsbButton.Text = "Zerar USB"
                    _zeroUsbButton.Width = 150
                    ConfigureActionsWithBottomZero(ActionsBottomPanel, _bootButton, _identifyUsbButton, _verifyBootButton)
                    Dim bootIsoSettings = _settingsService.Load()
                    _identifyUsbButton.Visible = bootIsoSettings.ShowIdentifyUsbButton
                    _verifyBootButton.Visible = bootIsoSettings.ShowValidateBootButton
                    UpdateBottomZeroSpacer(ActionsBottomPanel)
                    _adminWarningLabel.Text = "Aviso: Ative o Modo Administrador para gravar ou verificar USB bootável."
            End Select
        End Sub

        Private Sub ConfigureActionsWithBottomZero(container As FlowLayoutPanel, ParamArray topControls As Control())
            container.Controls.Clear()
            For Each control In topControls
                container.Controls.Add(control)
            Next

            Dim spacer = New Panel() With {
                .Name = "ZeroUsbBottomSpacer",
                .Width = 1,
                .Height = 0,
                .Margin = New Padding(0)
            }
            container.Controls.Add(spacer)

            _zeroUsbButton.Visible = True
            _zeroUsbButton.Width = 150
            _zeroUsbButton.Margin = New Padding(0)
            container.Controls.Add(_zeroUsbButton)

            AddHandler container.SizeChanged,
                Sub(sender As Object, e As EventArgs)
                    UpdateBottomZeroSpacer(container)
                End Sub

            UpdateBottomZeroSpacer(container)
        End Sub

        Private Sub UpdateBottomZeroSpacer(container As FlowLayoutPanel)
            Dim spacer = TryCast(container.Controls("ZeroUsbBottomSpacer"), Panel)
            If spacer Is Nothing Then
                Return
            End If

            Dim usedHeight = container.Padding.Top + container.Padding.Bottom
            For Each control As Control In container.Controls
                If control Is spacer OrElse Not control.Visible Then
                    Continue For
                End If
                usedHeight += control.Height + control.Margin.Vertical
            Next

            spacer.Height = Math.Max(0, container.ClientSize.Height - usedHeight)
        End Sub

        Private Sub OnRefreshClick(sender As Object, e As EventArgs)
            LoadUsbDrives()
            LoadCacheFiles()
            _logService.WriteAsync("USB | ATUALIZAR_LISTA").GetAwaiter().GetResult()
        End Sub

        ''' <summary>
        ''' Inicia uma operacao USB. Devolve False, sem lancar excecao, se outra ja estiver em curso (D-04).
        ''' Em operacao destrutiva, confere a identidade do disco (D-01) e pede confirmacao reforcada
        ''' para disco nao removivel ou grande (D-02). Trava todos os botoes de acao ate EndUsbOperation.
        ''' </summary>
        Private Function TryBeginUsbOperation(drive As UsbDriveOption, destructive As Boolean, ByRef token As CancellationToken) As Boolean
            If _usbOperationCts IsNot Nothing Then
                _statusLabel.Text = "Ja existe uma operacao USB em andamento."
                _logService.WriteAsync("USB | OPERACAO | IGNORADA | Outra operacao em andamento").GetAwaiter().GetResult()
                Return False
            End If

            If destructive Then
                Dim identity = drive?.Identity
                Try
                    DiskSafety.EnsureSameDisk(identity)
                Catch ex As DiskSafetyException
                    _logService.WriteAsync($"USB | DISK_GUARD | RECUSADO | {ex.Message}").GetAwaiter().GetResult()
                    MessageBox.Show(ex.Message, "USB Boot", MessageBoxButtons.OK, MessageBoxIcon.Error)
                    Return False
                End Try

                If DiskSafety.NeedsReinforcedConfirmation(identity) AndAlso Not ConfirmReinforced(identity) Then
                    _statusLabel.Text = "Operacao cancelada: confirmacao reforcada nao conferiu."
                    _logService.WriteAsync($"USB | DISK_GUARD | CONFIRMACAO_REFORCADA_RECUSADA | {identity.Describe()}").GetAwaiter().GetResult()
                    Return False
                End If
            End If

            _usbOperationCts = New CancellationTokenSource()
            _targetDisk = If(destructive, drive?.Identity, Nothing)
            SetUsbActionButtonsEnabled(False)
            _cancelOperationButton.Enabled = True
            token = _usbOperationCts.Token
            Return True
        End Function

        Private Function ConfirmReinforced(identity As DiskIdentity) As Boolean
            Dim reason = If(Not identity.RemovableMedia,
                            "nao e uma midia removivel (pode ser um HD ou SSD externo)",
                            "e maior que 256 GB")
            Dim typed = Microsoft.VisualBasic.Interaction.InputBox(
                $"Atencao: o disco de destino {reason}.{Environment.NewLine}{Environment.NewLine}" &
                $"{identity.Describe()}{Environment.NewLine}{Environment.NewLine}" &
                $"Todo o conteudo sera apagado. Para confirmar, digite o numero do disco ({identity.DiskNumber}):",
                "Confirmacao reforcada",
                String.Empty)
            Return String.Equals(If(typed, String.Empty).Trim(), identity.DiskNumber.ToString(), StringComparison.Ordinal)
        End Function

        Private Sub EndUsbOperation()
            _cancelOperationButton.Enabled = False
            ReleaseHeldVolumeLocks()
            _targetDisk = Nothing
            If _usbOperationCts IsNot Nothing Then
                _usbOperationCts.Dispose()
                _usbOperationCts = Nothing
            End If
            SetUsbActionButtonsEnabled(True)
        End Sub

        ''' <summary>
        ''' Trava final (D-01), chamada na entrada de toda funcao que apaga ou grava o disco fisico:
        ''' o disco precisa ser o confirmado no inicio da operacao e continuar sendo o mesmo aparelho.
        ''' </summary>
        Private Sub GuardTargetDisk(diskNumber As Integer)
            Dim expected = _targetDisk
            If expected Is Nothing OrElse expected.DiskNumber <> diskNumber Then
                Throw New DiskSafetyException($"Gravacao no Disk {diskNumber} recusada: nao e o disco confirmado para esta operacao. Nada foi gravado.")
            End If
            DiskSafety.EnsureSameDisk(expected)
        End Sub

        Private Async Sub OnCancelOperationClick(sender As Object, e As EventArgs)
            If _usbOperationCts Is Nothing OrElse _usbOperationCts.IsCancellationRequested Then
                Return
            End If

            _cancelOperationButton.Enabled = False
            _statusLabel.Text = "Cancelando operacao USB..."
            _usbOperationCts.Cancel()
            Await _logService.WriteAsync("USB | OPERACAO | CANCEL_REQUEST")
        End Sub

        Private Async Sub OnZeroUsbClick(sender As Object, e As EventArgs)
            Dim drive = TryCast(_driveCombo.SelectedItem, UsbDriveOption)
            If drive Is Nothing Then
                MessageBox.Show("Selecione um USB.", "USB Boot", MessageBoxButtons.OK, MessageBoxIcon.Information)
                Return
            End If
            If drive.DiskNumber < 0 Then
                MessageBox.Show("Nao foi possivel identificar o disco fisico do USB selecionado.", "USB Boot", MessageBoxButtons.OK, MessageBoxIcon.Error)
                Return
            End If
            If Not EnsureUsbUacIfNeeded() Then
                Return
            End If

            Dim sizeBytes = If(drive.SizeBytes > 0, drive.SizeBytes, GetDiskSizeBytes(drive.DiskNumber))
            Dim fileSystem = SelectZeroUsbFileSystem(sizeBytes)
            Dim confirm = MessageBox.Show($"O USB selecionado sera zerado.{Environment.NewLine}{Environment.NewLine}" &
                                          $"Destino: Disk {drive.DiskNumber} - {drive.Model} ({FormatBytes(sizeBytes)}){Environment.NewLine}" &
                                          $"Sistema de arquivos: {fileSystem}{Environment.NewLine}{Environment.NewLine}" &
                                          "Todo o conteudo, particoes e letras atuais do dispositivo serao apagados. Deseja continuar?",
                                          "Zerar USB",
                                          MessageBoxButtons.YesNo,
                                          MessageBoxIcon.Warning)
            If confirm <> DialogResult.Yes Then
                Return
            End If

            Dim cancellationToken As CancellationToken
            If Not TryBeginUsbOperation(drive, True, cancellationToken) Then
                Return
            End If
            Try
                SetUsbActionButtonsEnabled(False)
                SetBusyIndicator(True)
                SetProgressMarquee(True)
                _statusLabel.Text = "Zerando USB..."
                Await _logService.WriteAsync($"USB | ZERO | INICIO | Disk {drive.DiskNumber} | FS={fileSystem}")

                Dim mountedLetter = Await ZeroUsbAsync(drive, "QUALLIT_USB", cancellationToken)
                drive.SetDriveLetter(mountedLetter)
                _statusLabel.Text = $"USB zerado: {mountedLetter}: ({fileSystem})"
                Await _logService.WriteAsync($"USB | ZERO | OK | Disk {drive.DiskNumber} | Letter={mountedLetter} | FS={fileSystem}")
                MessageBox.Show($"USB zerado com sucesso em {mountedLetter}: ({fileSystem}).", "Zerar USB", MessageBoxButtons.OK, MessageBoxIcon.Information)
                LoadUsbDrives()
            Catch ex As OperationCanceledException
                _statusLabel.Text = "Zerar USB cancelado."
                _logService.WriteAsync($"USB | ZERO | CANCELADO | Disk {drive.DiskNumber}").GetAwaiter().GetResult()
            Catch ex As Exception
                _statusLabel.Text = "Falha ao zerar USB."
                _logService.WriteAsync($"USB | ZERO | ERRO | Disk {drive.DiskNumber} | {ex.Message}").GetAwaiter().GetResult()
                MessageBox.Show($"Falha ao zerar USB: {ex.Message}", "Zerar USB", MessageBoxButtons.OK, MessageBoxIcon.Error)
            Finally
                SetProgressMarquee(False)
                SetBusyIndicator(False)
                SetUsbActionButtonsEnabled(True)
                EndUsbOperation()
            End Try
        End Sub

        Public Sub OnViewShown()
            Dim settings = _settingsService.Load()

            If _viewMode = UsbOperationViewMode.BootIso Then
                _identifyUsbButton.Visible = settings.ShowIdentifyUsbButton
                _verifyBootButton.Visible = settings.ShowValidateBootButton
                UpdateBottomZeroSpacer(ActionsBottomPanel)
            End If
        End Sub


        Private Sub OnDriveChanged(sender As Object, e As EventArgs)
            UpdateDriveInfo()
        End Sub

        ''' <summary>Relista os USB (letra, sistema de arquivos, espaco) mantendo selecionado o mesmo disco.</summary>
        Private Sub RefreshUsbListKeepingDisk(diskNumber As Integer)
            Try
                LoadUsbDrives()
                For index = 0 To _driveCombo.Items.Count - 1
                    Dim item = TryCast(_driveCombo.Items(index), UsbDriveOption)
                    If item IsNot Nothing AndAlso item.DiskNumber = diskNumber Then
                        _driveCombo.SelectedIndex = index
                        Exit For
                    End If
                Next
                UpdateDriveInfo()
            Catch ex As Exception
                _logService.WriteAsync($"USB | REFRESH_APOS_OPERACAO | ERRO | Disk {diskNumber} | {ex.Message}").GetAwaiter().GetResult()
            End Try
        End Sub

        Private Sub SetUsbActionButtonsEnabled(enabled As Boolean)
            Dim adminEnabled = enabled AndAlso IsRunningAsAdmin()
            _copyButton.Enabled = enabled
            _verifyTibxButton.Enabled = enabled
            _bootButton.Enabled = adminEnabled
            _identifyUsbButton.Enabled = enabled
            _verifyBootButton.Enabled = enabled
            _zeroUsbButton.Enabled = adminEnabled
            _refreshButton.Enabled = enabled
            _driveCombo.Enabled = enabled
            _tibxList.Enabled = enabled
            _isoList.Enabled = enabled
            If enabled Then UpdateImageActionButtons()
        End Sub

        Private Function EnsureUsbUacIfNeeded() As Boolean
            If IsRunningAsAdmin() Then
                Return True
            End If
            _statusLabel.Text = "Esta operação requer Modo Administrador (formatar ou gravar ISO)."
            Return False
        End Function

        Private Function IsRunningAsAdmin() As Boolean
            Try
                Dim identity = WindowsIdentity.GetCurrent()
                Dim principal = New WindowsPrincipal(identity)
                Return principal.IsInRole(WindowsBuiltInRole.Administrator)
            Catch
                Return False
            End Try
        End Function

        Private Sub UpdateAdminUi()
            Dim isAdmin = IsRunningAsAdmin()
            ' Operações destrutivas (formatar, gravar raw, boot) exigem elevação
            _bootButton.Enabled = isAdmin AndAlso HasRealCacheSelection(_isoList, ".iso")
            _identifyUsbButton.Enabled = True
            _verifyBootButton.Enabled = True
            _zeroUsbButton.Enabled = isAdmin
            ' Avisos de modo administrador
            _adminWarningLabel.Visible = Not isAdmin
            _adminRestartButton.Visible = Not isAdmin
            ' Enumeração e cópia lógica funcionam sem elevação
            _refreshButton.Enabled = True
            _copyButton.Enabled = HasRealCacheSelection(_tibxList, ".tibx")
            _verifyTibxButton.Enabled = True
            _driveCombo.Enabled = True
            _tibxList.Enabled = True
            _isoList.Enabled = True
        End Sub

        ''' <summary>Ajusta a altura da linha 5 (status) ao texto: 40 px para uma linha, mais o que a quebra exigir.</summary>
        Private Sub FitStatusRowHeight(line As StatusLine)
            Const StatusRow = 5
            Const BaseRowHeight = 40
            Const BaseLineHeight = 22
            Dim lbl = line.TextLabel
            If lbl.Width <= 0 OrElse LayoutRoot.RowStyles.Count <= StatusRow Then Return
            Dim lineHeight = BaseLineHeight
            If lbl.Text.Length > 0 Then
                Dim measured = TextRenderer.MeasureText(lbl.Text, lbl.Font, New Size(lbl.Width, Integer.MaxValue),
                                                        TextFormatFlags.WordBreak Or TextFormatFlags.TextBoxControl)
                lineHeight = Math.Max(BaseLineHeight, measured.Height + 4)
            End If
            Dim rowHeight = CSng(BaseRowHeight + lineHeight - BaseLineHeight)
            If LayoutRoot.RowStyles(StatusRow).Height <> rowHeight Then
                LayoutRoot.RowStyles(StatusRow).Height = rowHeight
            End If
        End Sub

        Private Sub OnImageSelectionChanged(sender As Object, e As EventArgs)
            UpdateImageActionButtons()
            UpdateImageDescription(TryCast(sender, ListBox))
        End Sub

        Private _imageDescriptionPanel As Panel
        Private _imageDescriptionBox As TextBox

        ''' <summary>Mostra a descricao do catalogo e o "Como gravar" da imagem selecionada.</summary>
        Private Sub UpdateImageDescription(list As ListBox)
            If _imageDescriptionBox Is Nothing OrElse list Is Nothing Then Return
            Dim item = TryCast(list.SelectedItem, CacheItem)
            If item Is Nothing OrElse item.IsPlaceholder OrElse Not File.Exists(item.FullPath) Then
                _imageDescriptionBox.Text = String.Empty
                FitImageDescriptionHeight()
                Return
            End If
            Try
                Dim reason As String = Nothing
                Dim catalogItem = _usbWritePolicyResolver.FindCatalogItem(_catalogService.LoadAllCatalogs(AppPaths.DataDir),
                                                                          FindCatalogNameForPath(LoadDownloadStatusMap(), item.FullPath),
                                                                          Path.GetFileName(item.FullPath), New FileInfo(item.FullPath).Length, reason)
                Dim text = ImageGuidance.Describe(catalogItem)
                _imageDescriptionBox.Text = If(text.Length > 0, text, "Imagem fora dos catalogos: sem descricao.")
            Catch
                _imageDescriptionBox.Text = String.Empty
            End Try
            _imageDescriptionBox.SelectionStart = 0
            _imageDescriptionBox.ScrollToCaret()
            FitImageDescriptionHeight()
        End Sub

        ''' <summary>
        ''' Ajusta a altura da descricao ao texto (minimo 72, maximo metade do painel da lista).
        ''' Acima do maximo, fixa a altura e mostra a barra de rolagem vertical.
        ''' </summary>
        Private Sub FitImageDescriptionHeight()
            Const MinHeight = 72
            Dim panel = _imageDescriptionPanel
            Dim box = _imageDescriptionBox
            If panel Is Nothing OrElse box Is Nothing OrElse panel.Parent Is Nothing Then Return
            ' Folga para as margens internas da TextBox.
            Dim textWidth = panel.ClientSize.Width - panel.Padding.Horizontal - 8
            If textWidth <= 0 Then Return
            Dim needed = MinHeight
            If box.Text.Length > 0 Then
                Dim measured = TextRenderer.MeasureText(box.Text, box.Font, New Size(textWidth, Integer.MaxValue),
                                                        TextFormatFlags.WordBreak Or TextFormatFlags.TextBoxControl)
                needed = Math.Max(MinHeight, measured.Height + panel.Padding.Vertical + 4)
            End If
            Dim maxHeight = Math.Max(MinHeight, panel.Parent.ClientSize.Height \ 2)
            Dim wantedBars = If(needed > maxHeight, ScrollBars.Vertical, ScrollBars.None)
            If box.ScrollBars <> wantedBars Then box.ScrollBars = wantedBars
            Dim target = Math.Min(needed, maxHeight)
            If panel.Height <> target Then panel.Height = target
        End Sub

        Private Sub UpdateImageActionButtons()
            _copyButton.Enabled = _tibxList.Enabled AndAlso HasRealCacheSelection(_tibxList, ".tibx")
            _bootButton.Enabled = _isoList.Enabled AndAlso IsRunningAsAdmin() AndAlso HasRealCacheSelection(_isoList, ".iso")
        End Sub

        Private Shared Function HasRealCacheSelection(list As ListBox, expectedExtension As String) As Boolean
            Dim item = TryCast(list.SelectedItem, CacheItem)
            Return IsRealCacheItem(item, expectedExtension)
        End Function

        Private Shared Function IsRealCacheItem(item As CacheItem, expectedExtension As String) As Boolean
            Return item IsNot Nothing AndAlso Not item.IsPlaceholder AndAlso String.Equals(item.Extension, expectedExtension, StringComparison.OrdinalIgnoreCase)
        End Function

        Private Sub OnRestartAsAdminClick(sender As Object, e As EventArgs)
            Try
                _logService.WriteAsync("APP | RESTART_AS_ADMIN | REQUEST").GetAwaiter().GetResult()
                
                ' Construir argumentos com token se disponível
                Dim argsWithToken = TokenManager.BuildArgsWithToken()
                
                Dim startInfo As New ProcessStartInfo(Application.ExecutablePath) With {
                    .UseShellExecute = True,
                    .Verb = "runas"
                }
                
                ' Adicionar argumentos se houver
                If Not String.IsNullOrEmpty(argsWithToken) Then
                    startInfo.Arguments = argsWithToken
                    _logService.WriteAsync("APP | RESTART_AS_ADMIN | TOKEN_PASSED").GetAwaiter().GetResult()
                Else
                    _logService.WriteAsync("APP | RESTART_AS_ADMIN | NO_TOKEN").GetAwaiter().GetResult()
                End If
                
                Process.Start(startInfo)
                Application.Exit()
            Catch
                _logService.WriteAsync("APP | RESTART_AS_ADMIN | CANCEL").GetAwaiter().GetResult()
            End Try
        End Sub

        Private Sub LoadUsbDrives()
            _driveCombo.DataSource = Nothing
            _driveCombo.Items.Clear()
            _driveCombo.DisplayMember = "DisplayName"
            Try
                ' Discos que hospedam o Windows nunca entram na lista (D-02).
                Dim systemDisks = DiskSafety.GetSystemDiskNumbers()
                Dim usbDiskNumbers = GetUsbDiskNumbersViaSetupApi()
                _logService.WriteAsync($"USB | LIST | SETUPAPI_COUNT | {usbDiskNumbers.Count}").GetAwaiter().GetResult()
                If usbDiskNumbers.Count > 0 Then
                    _logService.WriteAsync($"USB | LIST | SETUPAPI_ITEMS | {String.Join(",", usbDiskNumbers)}").GetAwaiter().GetResult()
                End If
                If usbDiskNumbers.Count > 0 Then
                    For Each diskIndex In usbDiskNumbers
                        Dim model As String = "USB"
                        Dim sizeBytes As Long = 0
                        Dim deviceId As String = String.Empty
                        If systemDisks.Contains(diskIndex) Then
                            _logService.WriteAsync($"USB | LIST | SKIP_SYSTEM_DISK | {diskIndex}").GetAwaiter().GetResult()
                            Continue For
                        End If
                        If Not TryGetDiskInfoByIndex(diskIndex, model, sizeBytes, deviceId) Then
                            _logService.WriteAsync($"USB | LIST | SKIP_DISK | {diskIndex}").GetAwaiter().GetResult()
                            Continue For
                        End If
                    Dim letter = GetLogicalDriveLetterWithRetry(diskIndex, deviceId)
                    _logService.WriteAsync($"USB | MAP | Disk={diskIndex} | DeviceId={deviceId} | Letter={If(String.IsNullOrWhiteSpace(letter), "N/A", letter)}").GetAwaiter().GetResult()
                    _logService.WriteAsync($"USB | ADD | Disk={diskIndex} | Model={model} | Size={sizeBytes} | Letter={If(String.IsNullOrWhiteSpace(letter), "N/A", letter)}").GetAwaiter().GetResult()
                        _driveCombo.Items.Add(New UsbDriveOption(diskIndex, model, sizeBytes, letter) With {.Identity = DiskSafety.TryQuery(diskIndex)})
                    Next
                Else
                    For Each drive In DriveInfo.GetDrives()
                        If drive.DriveType <> DriveType.Removable OrElse Not drive.IsReady Then
                            Continue For
                        End If

                        Dim letter = drive.Name.Substring(0, 2)
                        Dim index = GetDiskIndexFromDriveLetter(letter)
                        If index >= 0 AndAlso systemDisks.Contains(index) Then
                            _logService.WriteAsync($"USB | LIST | SKIP_SYSTEM_DISK | {index}").GetAwaiter().GetResult()
                            Continue For
                        End If
                        Dim model As String = "USB"
                        Dim sizeBytes As Long = drive.TotalSize
                        If index >= 0 Then
                            GetDiskModelSizeAndInterfaceByIndex(index, model, sizeBytes)
                        End If

                        _logService.WriteAsync($"USB | ADD_FALLBACK | Disk={index} | Model={model} | Size={sizeBytes} | Letter={letter}").GetAwaiter().GetResult()
                        _driveCombo.Items.Add(New UsbDriveOption(index, model, sizeBytes, letter) With {.Identity = If(index >= 0, DiskSafety.TryQuery(index), Nothing)})
                    Next
                End If
            Catch ex As Exception
                _logService.WriteAsync($"USB | LOAD | ERRO | {ex.Message}").GetAwaiter().GetResult()
            End Try

            If _driveCombo.Items.Count > 0 Then
                LogUsbChanges()
                LogDriveComboItems()
                _driveCombo.SelectedIndex = 0
            Else
                _infoLabel.Text = "USB: Nenhum dispositivo USB removível detectado."
                _statusLabel.Text = "Nenhum dispositivo USB removível detectado."
                _driveCombo.Items.Add("Nenhum dispositivo USB removível detectado.")
                _driveCombo.SelectedIndex = 0
            End If

            UpdateDriveInfo()
        End Sub

        Private Sub LogDriveComboItems()
            _logService.WriteAsync($"USB | LIST | COUNT | {_driveCombo.Items.Count}").GetAwaiter().GetResult()
            For Each obj As Object In _driveCombo.Items
                Dim driveOption = TryCast(obj, UsbDriveOption)
                If driveOption IsNot Nothing Then
                    Dim driveLetterText = If(String.IsNullOrWhiteSpace(driveOption.DriveLetter), "N/A", driveOption.DriveLetter)
                    _logService.WriteAsync($"USB | LIST | ITEM | Disk={driveOption.DiskNumber} | Letter={driveLetterText} | Model={driveOption.Model} | Size={driveOption.SizeBytes}").GetAwaiter().GetResult()
                Else
                    Dim itemText = If(obj Is Nothing, "N/A", obj.ToString())
                    _logService.WriteAsync($"USB | LIST | ITEM | {itemText}").GetAwaiter().GetResult()
                End If
            Next
        End Sub

        Private Sub LogUsbChanges()
            Dim currentSnapshot As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
            Dim currentByKey As New Dictionary(Of String, UsbDriveOption)(StringComparer.OrdinalIgnoreCase)

            For Each obj As Object In _driveCombo.Items
                Dim driveOption = TryCast(obj, UsbDriveOption)
                If driveOption Is Nothing Then
                    Continue For
                End If

                Dim key As String
                If driveOption.DiskNumber >= 0 Then
                    key = $"DISK:{driveOption.DiskNumber}"
                ElseIf Not String.IsNullOrWhiteSpace(driveOption.DriveLetter) Then
                    key = $"LETTER:{driveOption.DriveLetter}"
                Else
                    key = $"MODEL:{driveOption.Model}"
                End If

                currentSnapshot.Add(key)
                If Not currentByKey.ContainsKey(key) Then
                    currentByKey.Add(key, driveOption)
                End If
            Next

            If _lastUsbSnapshot Is Nothing Then
                _lastUsbSnapshot = currentSnapshot
                Return
            End If

            For Each key In currentSnapshot
                If Not _lastUsbSnapshot.Contains(key) Then
                    Dim driveOption As UsbDriveOption = Nothing
                    currentByKey.TryGetValue(key, driveOption)
                    If driveOption IsNot Nothing Then
                        Dim driveLetterText = If(String.IsNullOrWhiteSpace(driveOption.DriveLetter), "N/A", driveOption.DriveLetter)
                        _logService.WriteAsync($"USB | NEW | Disk={driveOption.DiskNumber} | Letter={driveLetterText} | Model={driveOption.Model} | Size={driveOption.SizeBytes}").GetAwaiter().GetResult()
                    Else
                        _logService.WriteAsync($"USB | NEW | {key}").GetAwaiter().GetResult()
                    End If
                End If
            Next

            For Each key In _lastUsbSnapshot
                If Not currentSnapshot.Contains(key) Then
                    _logService.WriteAsync($"USB | REMOVED | {key}").GetAwaiter().GetResult()
                End If
            Next

            _lastUsbSnapshot = currentSnapshot
        End Sub

        Private Function GetLogicalDriveLetterWithRetry(diskIndex As Integer, deviceId As String) As String
            Dim attempts = 4
            For i = 0 To attempts - 1
                Dim letter = GetLogicalDriveLetterFromDiskNumberViaVolume(diskIndex)
                If String.IsNullOrWhiteSpace(letter) Then
                    letter = GetLogicalDriveLetterFromDiskIndex(diskIndex)
                End If
                If String.IsNullOrWhiteSpace(letter) Then
                    letter = GetLogicalDriveLetterFromDisk(deviceId)
                End If
                If Not String.IsNullOrWhiteSpace(letter) Then
                    Return letter
                End If
                If i < attempts - 1 Then
                    _logService.WriteAsync($"USB | MAP_RETRY | Disk={diskIndex} | Attempt={i + 1}").GetAwaiter().GetResult()
                    Threading.Thread.Sleep(500)
                End If
            Next
            Return String.Empty
        End Function

        Private Sub LoadCacheFiles()
            Dim cachePath = AppPaths.CacheDir
            Directory.CreateDirectory(cachePath)

            Dim tibxItems As New List(Of CacheItem)()
            Dim isoItems As New List(Of CacheItem)()
            Dim files = Directory.GetFiles(cachePath)
            For Each file In files
                Dim ext = Path.GetExtension(file).ToLowerInvariant()
                If ext = ".tibx" OrElse ext = ".iso" Then
                    Dim item = New CacheItem(file)
                    If ext = ".tibx" Then
                        tibxItems.Add(item)
                    Else
                        isoItems.Add(item)
                    End If
                End If
            Next

            Dim tibxCount = tibxItems.Count
            Dim isoCount = isoItems.Count
            tibxItems.Insert(0, CacheItem.CreatePlaceholder("Selecione um arquivo TIBX abaixo..."))
            isoItems.Insert(0, CacheItem.CreatePlaceholder("Selecione uma imagem abaixo..."))

            ' Mostrar TIBX imediatamente (não precisa análise)
            _tibxList.DataSource = tibxItems
            _tibxList.DisplayMember = "DisplayName"
            If _tibxList.Items.Count > 0 Then _tibxList.SelectedIndex = 0

            ' Mostrar ISOs sem analise inicial; a analise ocorre sob demanda na ISO selecionada.
            _isoList.DataSource = isoItems
            _isoList.DisplayMember = "DisplayName"
            If _isoList.Items.Count > 0 Then _isoList.SelectedIndex = 0
            UpdateImageActionButtons()

            Select Case _viewMode
                Case UsbOperationViewMode.CopyTibx
                    SetProgress(0)
                    _statusLabel.Text = $"Cache: {tibxCount} TIBX"
                    _logService.WriteAsync($"USB | CACHE | TIBX_ONLY | {tibxCount} TIBX").GetAwaiter().GetResult()
                Case Else
                    SetProgress(0)
                    _statusLabel.Text = $"Cache: {tibxCount} TIBX, {isoCount} ISO"
                    _logService.WriteAsync($"USB | CACHE | ISO_ON_DEMAND | {isoCount} ISO").GetAwaiter().GetResult()
            End Select
        End Sub

        ''' <summary>
        ''' Analisa tipos de ISO em background usando cache do JSON para performance
        ''' Mostra lista somente após análise completa
        ''' </summary>
        Private Async Sub AnalyzeIsoTypesAsync(isoItems As List(Of CacheItem))
            If Interlocked.CompareExchange(_isoAnalysisRunning, 1, 0) <> 0 Then
                Await _logService.WriteAsync("USB | ISO_ANALYZE | SKIP | Analise ja em andamento")
                Return
            End If

            Try
                ' VERIFICAÇÃO: Garantir que JSON tenha todos os campos necessários
                Await SynchronizeDownloadStatusJsonAsync()
                
                ' Obter ISOs para analise estrutural. A recomendacao depende da ISO atual,
                ' nao apenas do cache legado Windows/Linux.
                Dim isoToAnalyze = isoItems.Where(Function(x) x.Extension = ".iso").ToList()
                
                If isoToAnalyze.Count = 0 Then
                    _logService.WriteAsync($"USB | ISO_ANALYZE | NADA_ANALISAR | Todas as ISOs já analisadas").GetAwaiter().GetResult()
                    
                    ' Mostrar lista imediatamente se tudo já analisado
                    If Me.InvokeRequired Then
                        Me.Invoke(Sub()
                                      _isoList.DataSource = isoItems
                                      _isoList.DisplayMember = "DisplayName"
                                      _statusLabel.Text = $"Cache: {_tibxList.Items.Count} TIBX, {isoItems.Count} ISO"
                                  End Sub)
                    Else
                        _isoList.DataSource = isoItems
                        _isoList.DisplayMember = "DisplayName"
                        _statusLabel.Text = $"Cache: {_tibxList.Items.Count} TIBX, {isoItems.Count} ISO"
                    End If
                    Return
                End If
                
                ' Iniciar barra de progresso
                If Me.InvokeRequired Then
                    Me.Invoke(Sub()
                                  SetProgress(0)
                                  _statusLabel.Text = $"Analisando {isoToAnalyze.Count} ISOs..."
                              End Sub)
                Else
                    SetProgress(0)
                    _statusLabel.Text = $"Analisando {isoToAnalyze.Count} ISOs..."
                End If
                
                ' Carregar cache de tipos de ISO
                Dim statusMap = LoadDownloadStatusMap()
                Dim processedCount As Integer = 0
                
                ' Analisar cada ISO que ainda não foi analisada
                For Each item In isoToAnalyze
                    Try
                        Dim detectedType As IsoType = IsoType.Unknown
                        Dim analysis = _isoAnalysisService.Analyze(item.FullPath)
                        detectedType = MapIsoAnalysisToLegacyType(analysis)
                        _logService.WriteAsync($"USB | ISO_ANALYZE | ANALISANDO | {Path.GetFileName(item.FullPath)} | Recomendacao: {analysis.RecommendationText} | ElTorito BIOS={analysis.HasBiosElTorito} UEFI={analysis.HasUefiElTorito}").GetAwaiter().GetResult()

                        ' Atualizar cache legado apenas para manter compatibilidade com status existente.
                        Dim cacheKey = FindCatalogNameForPath(statusMap, item.FullPath)
                        If Not String.IsNullOrWhiteSpace(cacheKey) AndAlso statusMap.ContainsKey(cacheKey) Then
                            Dim cacheEntry = statusMap(cacheKey)
                            cacheEntry.IsoTypeAnalyzed = True
                            cacheEntry.IsoTypeAnalyzedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
                            Select Case detectedType
                                Case IsoType.Windows
                                    cacheEntry.IsoType = "Windows"
                                Case IsoType.Linux
                                    cacheEntry.IsoType = "Linux"
                                Case Else
                                    cacheEntry.IsoType = "Unknown"
                            End Select
                            statusMap(cacheKey) = cacheEntry
                            SaveDownloadStatusMap(statusMap)
                        End If
                        
                        ' Atualizar item individualmente na UI (atualização progressiva)
                        item.UpdateIsoAnalysis(analysis, detectedType)
                        _logService.WriteAsync($"USB | ISO_ANALYZE | ITEM_UPDATED | {Path.GetFileName(item.FullPath)} | {item.DisplayName}").GetAwaiter().GetResult()
                        
                        ' Forçar refresh completo do DataSource (ListBox não responde bem ao PropertyChanged)
                        If Me.InvokeRequired Then
                            Me.Invoke(Sub()
                                          ' Salvar seleção atual
                                          Dim selectedIndex = _isoList.SelectedIndex
                                          
                                          ' Forçar rebind completo
                                          _isoList.DataSource = Nothing
                                          _isoList.DataSource = isoItems
                                          _isoList.DisplayMember = "DisplayName"
                                          
                                          ' Restaurar seleção
                                          If selectedIndex >= 0 AndAlso selectedIndex < _isoList.Items.Count Then
                                              _isoList.SelectedIndex = selectedIndex
                                          End If
                                      End Sub)
                        Else
                            ' Salvar seleção atual
                            Dim selectedIndex = _isoList.SelectedIndex
                            
                            ' Forçar rebind completo
                            _isoList.DataSource = Nothing
                            _isoList.DataSource = isoItems
                            _isoList.DisplayMember = "DisplayName"
                            
                            ' Restaurar seleção
                            If selectedIndex >= 0 AndAlso selectedIndex < _isoList.Items.Count Then
                                _isoList.SelectedIndex = selectedIndex
                            End If
                        End If
                        
                        ' Atualizar progresso
                        processedCount += 1
                        Dim progressPercent = CInt((processedCount * 100) / isoToAnalyze.Count)
                        
                        If Me.InvokeRequired Then
                            Me.Invoke(Sub()
                                          SetProgress(progressPercent)
                                          _statusLabel.Text = $"Analisando ISOs... {processedCount}/{isoToAnalyze.Count} ({item.TypeIcon} {Path.GetFileName(item.FullPath)})"
                                      End Sub)
                        Else
                            SetProgress(progressPercent)
                            _statusLabel.Text = $"Analisando ISOs... {processedCount}/{isoToAnalyze.Count} ({item.TypeIcon} {Path.GetFileName(item.FullPath)})"
                        End If
                        
                        ' Pequeno delay para não sobrecarregar
                        Await Task.Delay(50)
                        
                    Catch ex As Exception
                        _logService.WriteAsync($"USB | ISO_ANALYZE | ERRO | {Path.GetFileName(item.FullPath)} | {ex.Message}").GetAwaiter().GetResult()
                    End Try
                Next
                
                ' FINAL: Análise concluída - lista já está atualizada progressivamente
                If Me.InvokeRequired Then
                    Me.Invoke(Sub()
                                  SetProgressMarquee(False)
                                  SetProgress(100)
                                  _statusLabel.Text = $"Análise concluída: {processedCount} ISOs analisadas"
                                  
                                  ' Atualizar status final
                                  _statusLabel.Text = $"Cache: {_tibxList.Items.Count} TIBX, {isoItems.Count} ISO"
                              End Sub)
                Else
                    SetProgressMarquee(False)
                    SetProgress(100)
                    _statusLabel.Text = $"Análise concluída: {processedCount} ISOs analisadas"
                    
                    ' Atualizar status final
                    _statusLabel.Text = $"Cache: {_tibxList.Items.Count} TIBX, {isoItems.Count} ISO"
                End If
                
                _logService.WriteAsync($"USB | ISO_ANALYZE | COMPLETO | Análise de tipos de ISO concluída | {processedCount}/{isoToAnalyze.Count}").GetAwaiter().GetResult()
                
            Catch ex As Exception
                ' Finalizar barra de progresso em caso de erro
                If Me.InvokeRequired Then
                    Me.Invoke(Sub()
                                  SetProgressMarquee(False)
                                  SetProgress(0)
                                  _statusLabel.Text = "Erro na análise de ISOs"
                                  
                                  ' Mostrar lista mesmo em caso de erro
                                  _isoList.DataSource = isoItems
                                  _isoList.DisplayMember = "DisplayName"
                              End Sub)
                Else
                    SetProgressMarquee(False)
                    SetProgress(0)
                    _statusLabel.Text = "Erro na análise de ISOs"
                    
                    ' Mostrar lista mesmo em caso de erro
                    _isoList.DataSource = isoItems
                    _isoList.DisplayMember = "DisplayName"
                End If
                
                _logService.WriteAsync($"USB | ISO_ANALYZE | ERRO_GLOBAL | {ex.Message}").GetAwaiter().GetResult()
                            Finally
                                Interlocked.Exchange(_isoAnalysisRunning, 0)
            End Try
        End Sub
        
        ''' <summary>
        ''' Verifica e sincroniza o download-status.json para garantir todos os campos necessários
        ''' </summary>
        Private Async Function SynchronizeDownloadStatusJsonAsync() As Task
            Dim pendingLog As String = Nothing
            Try
                Dim statusPath = AppPaths.DownloadStatusPath
                
                Dim needsUpdate As Boolean = False
                Dim statusMap As Dictionary(Of String, DownloadStatusEntry)
                
                ' Carregar JSON existente ou criar novo
                If File.Exists(statusPath) Then
                    Try
                        Dim json = Await File.ReadAllTextAsync(statusPath)
                        statusMap = JsonSerializer.Deserialize(Of Dictionary(Of String, DownloadStatusEntry))(json)
                    Catch ex As Exception
                        pendingLog = $"USB | JSON_SYNC | ERRO_LOAD | {ex.Message}"
                        statusMap = New Dictionary(Of String, DownloadStatusEntry)()
                    End Try
                    If pendingLog IsNot Nothing Then
                        Await _logService.WriteAsync(pendingLog)
                        pendingLog = Nothing
                    End If
                Else
                    statusMap = New Dictionary(Of String, DownloadStatusEntry)()
                End If
                
                ' Verificar cada entrada e adicionar campos faltantes
                For Each entry In statusMap.ToList()
                    Dim key = entry.Key
                    Dim value = entry.Value
                    
                    If String.IsNullOrWhiteSpace(value.IsoType) Then
                        value.IsoType = "Unknown"
                        needsUpdate = True
                    End If
                    
                    If String.IsNullOrWhiteSpace(value.IsoTypeAnalyzedAt) Then
                        value.IsoTypeAnalyzedAt = ""
                        needsUpdate = True
                    End If
                    
                    ' IsoTypeAnalyzed = False é o valor padrão — não marca needsUpdate
                    
                    If needsUpdate Then
                        statusMap(key) = value
                    End If
                Next
                
                ' Salvar JSON atualizado se necessário
                If needsUpdate Then
                    Try
                        Directory.CreateDirectory(Path.GetDirectoryName(statusPath))
                        Dim json = JsonSerializer.Serialize(statusMap, New JsonSerializerOptions With {
                            .WriteIndented = True
                        })
                        Await File.WriteAllTextAsync(statusPath, json)
                        Await _logService.WriteAsync($"USB | JSON_SYNC | ATUALIZADO | Campos de ISO sincronizados")
                    Catch ex As Exception
                        pendingLog = $"USB | JSON_SYNC | ERRO_SAVE | {ex.Message}"
                    End Try
                    If pendingLog IsNot Nothing Then
                        Await _logService.WriteAsync(pendingLog)
                        pendingLog = Nothing
                    End If
                Else
                    Await _logService.WriteAsync($"USB | JSON_SYNC | OK | JSON já sincronizado")
                End If
                
            Catch ex As Exception
                pendingLog = $"USB | JSON_SYNC | ERRO_GLOBAL | {ex.Message}"
            End Try
            If pendingLog IsNot Nothing Then
                Await _logService.WriteAsync(pendingLog)
            End If
        End Function
        
        ''' <summary>
        ''' Método estático para detecção de tipo de ISO (sem logs para performance)
        ''' Prioriza Linux (Rock Ridge) sobre Windows (UDF/Joliet) + heurística
        ''' </summary>
        Private Shared Function DetectIsoTypeStatic(isoPath As String) As IsoType
            Try
                ' HEURÍSTICA: Verificar nome do arquivo para indicadores de Linux
                Dim fileName = Path.GetFileName(isoPath).ToLowerInvariant()
                Dim isProbablyLinux = fileName.Contains("ubuntu") OrElse 
                                     fileName.Contains("linux") OrElse 
                                     fileName.Contains("debian") OrElse 
                                     fileName.Contains("mint") OrElse 
                                     fileName.Contains("fedora") OrElse 
                                     fileName.Contains("centos") OrElse 
                                     fileName.Contains("acronis") OrElse
                                     fileName.Contains("systemrescue") OrElse
                                     fileName.Contains("kali") OrElse
                                     fileName.Contains("arch")
                
                Using isoStream As FileStream = File.Open(isoPath, FileMode.Open, FileAccess.Read, FileShare.Read)
                    ' PRIMEIRO: Tentar detectar ISO9660 com Rock Ridge (Linux) - PRIORIDADE MÁXIMA
                    Try
                        Using cdReader As New CDReader(isoStream, True)
                            If cdReader.ActiveVariant = Iso9660Variant.RockRidge Then
                                Return IsoType.Linux
                            End If
                        End Using
                    Catch
                        ' Continuar para outras detecções
                    End Try
                    
                    ' Reset stream position
                    isoStream.Seek(0, SeekOrigin.Begin)
                    
                    ' SEGUNDO: Tentar detectar UDF (Windows/Moderno)
                    Try
                        Using udfReader As New UdfReader(isoStream)
                            ' Se heurística indicar Linux mas só encontrou UDF, ainda pode ser Linux
                            If isProbablyLinux Then
                                Return IsoType.Linux ' Forçar Linux baseado no nome
                            End If
                            Return IsoType.Windows
                        End Using
                    Catch
                        ' Continuar para ISO9660
                    End Try
                    
                    ' Reset stream position
                    isoStream.Seek(0, SeekOrigin.Begin)
                    
                    ' TERCEIRO: Tentar detectar ISO9660 com Joliet (Windows)
                    Try
                        Using cdReader As New CDReader(isoStream, True)
                            If cdReader.ActiveVariant = Iso9660Variant.Joliet Then
                                ' Se heurística indicar Linux mas só encontrou Joliet, ainda pode ser Linux
                                If isProbablyLinux Then
                                    Return IsoType.Linux ' Forçar Linux baseado no nome
                                End If
                                Return IsoType.Windows
                            Else
                                ' ISO9660 padrão - usar heurística
                                If isProbablyLinux Then
                                    Return IsoType.Linux
                                End If
                                Return IsoType.Windows ' Padrão assume Windows
                            End If
                        End Using
                    Catch
                        ' Último recurso: usar heurística
                        If isProbablyLinux Then
                            Return IsoType.Linux
                        End If
                        Return IsoType.Unknown
                    End Try
                End Using
            Catch
                Return IsoType.Unknown
            End Try
        End Function

        Private Shared Function MapIsoAnalysisToLegacyType(analysis As IsoAnalysisResult) As IsoType
            If analysis Is Nothing Then Return IsoType.Unknown
            If analysis.HasBootMgr OrElse analysis.HasBootSect Then Return IsoType.Windows
            If analysis.HasSyslinux OrElse analysis.HasGrub OrElse analysis.HasAcronisBootwiz Then Return IsoType.Linux
            If analysis.HasEfiBootX64 OrElse analysis.HasUefiElTorito Then Return IsoType.Windows
            Return IsoType.Unknown
        End Function

        Private Async Sub OnCopyClick(sender As Object, e As EventArgs)
            Dim drive = TryCast(_driveCombo.SelectedItem, UsbDriveOption)
            Dim item = TryCast(_tibxList.SelectedItem, CacheItem)
            If drive Is Nothing Then
                MessageBox.Show("Selecione um USB.", "USB Boot", MessageBoxButtons.OK, MessageBoxIcon.Information)
                Return
            End If
            If Not IsRealCacheItem(item, ".tibx") Then
                MessageBox.Show("Selecione um arquivo TIBX no cache.", "USB Boot", MessageBoxButtons.OK, MessageBoxIcon.Information)
                Return
            End If
            ' Formatar antes de copiar: USB sem letra ou volume atual que nao recebe o TIBX
            ' (FAT com arquivo > 4 GB, CDFS/UDF de imagem gravada em RAW/DD, particao menor que o USB e sem espaco).
            Dim tibxBytes = New FileInfo(item.FullPath).Length
            Dim formatReason As String = Nothing
            If String.IsNullOrWhiteSpace(drive.DriveLetter) Then
                formatReason = "USB sem letra detectado."
            Else
                formatReason = GetTibxFormatReason(drive, tibxBytes)
            End If

            If formatReason IsNot Nothing Then
                If drive.DiskNumber < 0 Then
                    MessageBox.Show("Nao foi possivel identificar o disco fisico do USB selecionado.", "USB Boot", MessageBoxButtons.OK, MessageBoxIcon.Error)
                    Return
                End If
                If Not EnsureUsbUacIfNeeded() Then
                    Return
                End If
                Await _logService.WriteAsync($"USB | TIBX_FORMAT | NECESSARIO | Disk {drive.DiskNumber} | Letter={drive.DriveLetter} | {formatReason}")
                Dim confirmFormat = MessageBox.Show($"{formatReason}{Environment.NewLine}O dispositivo sera formatado para receber uma letra e copiar o TIBX. Todo o conteudo atual do USB sera apagado." &
                                                    Environment.NewLine &
                                                    "Deseja continuar?",
                                                    "USB Boot",
                                                    MessageBoxButtons.YesNo,
                                                    MessageBoxIcon.Warning)
                If confirmFormat <> DialogResult.Yes Then
                    Await _logService.WriteAsync($"USB | TIBX_FORMAT | RECUSADO | Disk {drive.DiskNumber}")
                    Return
                End If

                Dim formatCancellationToken As CancellationToken
                If Not TryBeginUsbOperation(drive, True, formatCancellationToken) Then
                    Return
                End If
                Try
                    SetProgressMarquee(True)
                    _statusLabel.Text = "Formatando USB para backup TIBX..."
                    Dim requireLargeFileSupport = tibxBytes > 4L * 1024L * 1024L * 1024L
                    Await _logService.WriteAsync($"USB | ZERO_TIBX | INICIO | Disk {drive.DiskNumber} | LargeFile={requireLargeFileSupport}")
                    ' TIBX sempre em NTFS: a midia de boot Acronis restaura de NTFS; de exFAT nao (documentacao Acronis).
                    Dim mountedLetter = Await ZeroUsbAsync(drive, UsbVolumeLabels.Tibx, formatCancellationToken, requireLargeFileSupport, TibxUsbFileSystem)
                    Await _logService.WriteAsync($"USB | ZERO_TIBX | OK | Disk {drive.DiskNumber} | Letter={mountedLetter}")
                    drive.SetDriveLetter(mountedLetter)
                    If Not WaitForDriveReady(drive.RootPath, 15) Then
                        Throw New IOException("Unidade USB nao ficou pronta apos a formatacao.")
                    End If
                    _statusLabel.Text = "USB formatado. Aguardando montagem pelo sistema..."
                    Await Task.Delay(5000)
                    SetProgressMarquee(False)
                    UpdateDriveInfo()
                Catch ex As OperationCanceledException
                    SetProgressMarquee(False)
                    SetBusyIndicator(False)
                    _statusLabel.Text = "Formatacao USB cancelada."
                    _logService.WriteAsync($"USB | ZERO_TIBX | CANCELADO | Disk {drive.DiskNumber}").GetAwaiter().GetResult()
                    Return
                Catch ex As Exception
                    SetProgressMarquee(False)
                    SetBusyIndicator(False)
                    _statusLabel.Text = "Falha ao formatar USB."
                    MessageBox.Show($"Falha ao formatar USB: {ex.Message}", "USB Boot", MessageBoxButtons.OK, MessageBoxIcon.Error)
                    Return
                Finally
                    EndUsbOperation()
                End Try
            End If

            Dim errorLog As String = Nothing
            Dim autoplayBackup As AutoplayBackup = Nothing
            Dim settings = _settingsService.Load()
            Dim cancellationToken As CancellationToken
            If Not TryBeginUsbOperation(drive, False, cancellationToken) Then
                Return
            End If
            Try
                If settings.AutoDisableAutoplayDuringUsbOps Then
                    autoplayBackup = DisableAutoplay()
                End If

                Dim destination = Path.Combine(drive.RootPath, Path.GetFileName(item.FullPath))
                SetProgress(0)
                _statusLabel.Text = "Copiando TIBX..."
                SetBusyIndicator(True)
                Await _logService.WriteAsync($"USB | COPIA_TIBX | INICIO | {item.DisplayName} -> {drive.RootPath}")

                cancellationToken.ThrowIfCancellationRequested()
                If Not WaitForDriveReady(drive.RootPath, 10) Then
                    Throw New IOException("Unidade USB nao esta pronta para copia.")
                End If

                If Not Await EnsureUsbCapacityForTibxAsync(item.FullPath, drive) Then
                    Return
                End If

                ' 1: valida o TIBX em disco antes de copiar; o SHA calculado serve de referencia para a verificacao no USB.
                Dim sourceSha256 = Await ValidateTibxBeforeCopyAsync(item, cancellationToken)
                If sourceSha256 Is Nothing Then
                    Return
                End If

                If Not Await ShouldCopyExistingTibxAsync(sourceSha256, destination, cancellationToken) Then
                    Return
                End If

                ' 2: copia
                Await CopyFileWithProgressAsync(item.FullPath, destination, "Copiando TIBX...", cancellationToken)

                _statusLabel.Text = $"TIBX copiado: {Path.GetFileName(destination)}"
                Await _logService.WriteAsync($"USB | COPIA_TIBX | OK | {destination}")

                ' 3: verifica so o TIBX no USB contra o SHA validado no passo 1 (sem reler a imagem em disco).
                Dim settingsMd5 = _settingsService.Load()
                If settingsMd5.AutoValidateSha256AfterTibxCopy Then
                    Dim targetSha256 = Await ComputeSha256WithProgressAsync(destination, "Verificando TIBX no USB...", cancellationToken)
                    SetProgressMarquee(False)

                    If Not String.Equals(sourceSha256, targetSha256, StringComparison.OrdinalIgnoreCase) Then
                        Await _logService.WriteAsync($"USB | SHA256_TIBX | ERRO | {destination} | Esperado={sourceSha256} | USB={targetSha256}")
                        MessageBox.Show("SHA-256 divergente após cópia do TIBX.", "USB Boot", MessageBoxButtons.OK, MessageBoxIcon.Warning)
                    Else
                        Await _logService.WriteAsync($"USB | SHA256_TIBX | OK | {destination}")
                        _statusLabel.Text = $"Verificação do SHA-256 do {Path.GetFileName(destination)} validado com sucesso."
                    End If
                End If
            Catch ex As OperationCanceledException
                SetProgressMarquee(False)
                errorLog = $"USB | COPIA_TIBX | CANCELADO | {item.DisplayName}"
                _statusLabel.Text = "Operacao USB cancelada."
            Catch ex As Exception
                SetProgressMarquee(False)
                errorLog = $"USB | COPIA_TIBX | ERRO | {ex.Message}"
                MessageBox.Show(ex.Message, "USB Boot", MessageBoxButtons.OK, MessageBoxIcon.Error)
            Finally
                If autoplayBackup IsNot Nothing Then
                    RestoreAutoplay(autoplayBackup)
                End If

                SetBusyIndicator(False)
                EndUsbOperation()
                RefreshUsbListKeepingDisk(drive.DiskNumber)
            End Try

            If Not String.IsNullOrWhiteSpace(errorLog) Then
                Await _logService.WriteAsync(errorLog)
            End If
        End Sub

        Private Shared Function GetRecommendedBootCreationMethod(analysis As IsoAnalysisResult) As UsbBootCreationMethod
            If analysis.HasFilesOver4Gb AndAlso analysis.HasEfiBootX64 Then
                Return UsbBootCreationMethod.UefiNtfs
            End If

            Select Case analysis.Recommendation
                Case IsoBootRecommendation.Uefi
                    Return UsbBootCreationMethod.Uefi
                Case IsoBootRecommendation.UefiNtfs
                    Return UsbBootCreationMethod.UefiNtfs
                Case IsoBootRecommendation.RawRecommended, IsoBootRecommendation.Unsupported
                    Return UsbBootCreationMethod.RawDd
                Case Else
                    Return UsbBootCreationMethod.LegacyBios
            End Select
        End Function

        ''' <summary>Metodo sugerido e metodos liberados na janela "Criar USB Bootavel" (P2).</summary>
        Private NotInheritable Class BootMethodPlan
            Public Property Recommended As UsbBootCreationMethod
            ''' <summary>Nothing: todos liberados (analise local). Com politica: so os permitidos pelo catalogo.</summary>
            Public Property Allowed As HashSet(Of UsbBootCreationMethod)
            Public Property FromCatalog As Boolean
            Public Property CatalogName As String
            Public Property HeaderText As String
            Public Property NoteText As String
            Public Property LogText As String
        End Class

        ' logical-fat32 corresponde a duas opcoes do app: BIOS (copia FAT32 + MBR/PBR BootMgr, que tambem copia
        ' os arquivos EFI) e UEFI (copia FAT32). fat32-split-wim nao existe no app.
        Private Shared Function MapWriteMethod(method As String) As UsbBootCreationMethod()
            Select Case method
                Case UsbWriteMethods.LogicalFat32
                    Return {UsbBootCreationMethod.LegacyBios, UsbBootCreationMethod.Uefi}
                Case UsbWriteMethods.UefiNtfs
                    Return {UsbBootCreationMethod.UefiNtfs}
                Case UsbWriteMethods.Raw
                    Return {UsbBootCreationMethod.RawDd}
                Case Else
                    Return Array.Empty(Of UsbBootCreationMethod)()
            End Select
        End Function

        Private Shared Function MapPreferredWriteMethod(method As String, analysis As IsoAnalysisResult) As UsbBootCreationMethod?
            If method = UsbWriteMethods.LogicalFat32 Then
                Return If(analysis.HasBootMgr, UsbBootCreationMethod.LegacyBios, UsbBootCreationMethod.Uefi)
            End If
            Dim mapped = MapWriteMethod(method)
            Return If(mapped.Length = 0, CType(Nothing, UsbBootCreationMethod?), mapped(0))
        End Function

        ''' <summary>Rotulo do pendrive definido no catalogo (usbWritePolicy.volumeLabel), se a politica vale para a ISO.</summary>
        Private Function FindCatalogVolumeLabel(item As CacheItem) As String
            Try
                Dim reason As String = Nothing
                Dim isoSize = New FileInfo(item.FullPath).Length
                Dim catalogItem = _usbWritePolicyResolver.FindCatalogItem(_catalogService.LoadAllCatalogs(AppPaths.DataDir),
                                                                          FindCatalogNameForPath(LoadDownloadStatusMap(), item.FullPath),
                                                                          Path.GetFileName(item.FullPath), isoSize, reason)
                If catalogItem Is Nothing Then Return Nothing
                Dim decision = _usbWritePolicyResolver.Resolve(catalogItem, isoSize)
                Return If(decision.IsApplied, decision.VolumeLabel, Nothing)
            Catch
                Return Nothing
            End Try
        End Function

        Private Async Function BuildBootMethodPlanAsync(item As CacheItem, analysis As IsoAnalysisResult) As Task(Of BootMethodPlan)
            Dim decision As UsbWritePolicyDecision = Nothing
            Dim reason As String = Nothing
            Try
                Dim catalog = _catalogService.LoadAllCatalogs(AppPaths.DataDir)
                Dim downloadedName = FindCatalogNameForPath(LoadDownloadStatusMap(), item.FullPath)
                Dim isoSize = New FileInfo(item.FullPath).Length
                Dim catalogItem = _usbWritePolicyResolver.FindCatalogItem(catalog, downloadedName, Path.GetFileName(item.FullPath), isoSize, reason)
                If catalogItem IsNot Nothing Then
                    decision = _usbWritePolicyResolver.Resolve(catalogItem, isoSize)
                    reason = decision.IgnoredReason
                End If
            Catch ex As Exception
                reason = $"Falha ao ler a politica do catalogo: {ex.Message}"
            End Try

            If decision IsNot Nothing AndAlso decision.IsApplied Then
                Dim allowed As New HashSet(Of UsbBootCreationMethod)(decision.AllowedMethods.SelectMany(Function(method) MapWriteMethod(method)))
                Dim preferred = MapPreferredWriteMethod(decision.PreferredMethod, analysis)
                If allowed.Count > 0 AndAlso preferred.HasValue Then
                    Await _logService.WriteAsync($"USB | BOOT_METHOD | POLICY | {item.DisplayName} | Catalog={decision.CatalogName} | Preferred={decision.PreferredMethod} | Allowed={String.Join(",", decision.AllowedMethods)}")
                    Return New BootMethodPlan With {
                        .Recommended = preferred.Value,
                        .Allowed = allowed,
                        .FromCatalog = True,
                        .CatalogName = decision.CatalogName,
                        .HeaderText = $"Metodo definido pelo catalogo ({decision.CatalogName}): {decision.PreferredMethod}. Permitidos: {String.Join(", ", decision.AllowedMethods)}.",
                        .NoteText = decision.Note,
                        .LogText = $"Policy={decision.CatalogName}"
                    }
                End If
                reason = $"Politica do catalogo {decision.CatalogName} sem metodo disponivel neste app ({String.Join(", ", decision.AllowedMethods)})."
            End If

            Await _logService.WriteAsync($"USB | BOOT_METHOD | POLICY_IGNORED | {item.DisplayName} | {reason}")
            Dim assessment = _usbWriteMethodAdvisor.Assess(analysis)
            Dim local = MapPreferredWriteMethod(assessment.RecommendedMethod, analysis)
            Return New BootMethodPlan With {
                .Recommended = If(local, GetRecommendedBootCreationMethod(analysis)),
                .Allowed = Nothing,
                .HeaderText = $"Recomendacao da analise local: {If(local.HasValue, assessment.RecommendedMethod, analysis.RecommendationText)}.",
                .NoteText = String.Join(" ", assessment.Reasons),
                .LogText = $"Policy=nenhuma ({reason})"
            }
        End Function

        Private Function SelectUsbBootCreationMethod(analysis As IsoAnalysisResult, plan As BootMethodPlan) As UsbBootCreationMethod?
            Dim recommended = plan.Recommended
            Using dialog As New Form()
                dialog.Text = "Criar USB Bootavel"
                dialog.FormBorderStyle = FormBorderStyle.FixedDialog
                dialog.StartPosition = FormStartPosition.CenterParent
                dialog.MinimizeBox = False
                dialog.MaximizeBox = False
                dialog.ClientSize = New Size(420, 285)

                Dim label As New Label() With {
                    .Text = plan.HeaderText,
                    .AutoSize = False,
                    .Location = New Point(16, 16),
                    .Size = New Size(388, 40)
                }
                Dim noteLabel As New Label() With {
                    .Text = plan.NoteText,
                    .AutoSize = False,
                    .ForeColor = Color.DimGray,
                    .Location = New Point(16, 182),
                    .Size = New Size(388, 50)
                }

                Dim biosRadio As New RadioButton() With {
                    .Text = "Boot BIOS - FAT32 + MBR/PBR BootMgr proprio",
                    .Location = New Point(20, 62),
                    .AutoSize = True,
                    .Tag = UsbBootCreationMethod.LegacyBios
                }
                Dim uefiRadio As New RadioButton() With {
                    .Text = "Boot UEFI - FAT32 + EFI\BOOT\BOOTX64.EFI",
                    .Location = New Point(20, 92),
                    .AutoSize = True,
                    .Tag = UsbBootCreationMethod.Uefi
                }
                Dim rawRadio As New RadioButton() With {
                    .Text = "Boot RAW/DD - gravar imagem inteira no dispositivo",
                    .Location = New Point(20, 122),
                    .AutoSize = True,
                    .Tag = UsbBootCreationMethod.RawDd
                }
                Dim uefiNtfsRadio As New RadioButton() With {
                    .Text = "Boot UEFI:NTFS - NTFS + particao FAT auxiliar",
                    .Location = New Point(20, 152),
                    .AutoSize = True,
                    .Tag = UsbBootCreationMethod.UefiNtfs
                }

                Select Case recommended
                    Case UsbBootCreationMethod.Uefi
                        uefiRadio.Checked = True
                    Case UsbBootCreationMethod.UefiNtfs
                        uefiNtfsRadio.Checked = True
                    Case UsbBootCreationMethod.RawDd
                        rawRadio.Checked = True
                    Case Else
                        biosRadio.Checked = True
                End Select

                If plan.Allowed IsNot Nothing Then
                    For Each radio In New RadioButton() {biosRadio, uefiRadio, rawRadio, uefiNtfsRadio}
                        If Not plan.Allowed.Contains(CType(radio.Tag, UsbBootCreationMethod)) Then
                            radio.Enabled = False
                            radio.Text &= " (nao permitido pelo catalogo)"
                        End If
                    Next
                End If

                Dim okButton As New Button() With {.Text = "Continuar", .DialogResult = DialogResult.OK, .Location = New Point(218, 242), .Size = New Size(88, 28)}
                Dim cancelButton As New Button() With {.Text = "Cancelar", .DialogResult = DialogResult.Cancel, .Location = New Point(316, 242), .Size = New Size(88, 28)}
                UiTheme.StyleButton(okButton, ButtonKind.Primary)
                UiTheme.StyleButton(cancelButton, ButtonKind.Secondary)
                okButton.Top = 238
                cancelButton.Top = 238
                dialog.BackColor = Color.White

                dialog.Controls.Add(label)
                dialog.Controls.Add(noteLabel)
                dialog.Controls.Add(biosRadio)
                dialog.Controls.Add(uefiRadio)
                dialog.Controls.Add(rawRadio)
                dialog.Controls.Add(uefiNtfsRadio)
                dialog.Controls.Add(okButton)
                dialog.Controls.Add(cancelButton)
                dialog.AcceptButton = okButton
                dialog.CancelButton = cancelButton

                If dialog.ShowDialog(Me) <> DialogResult.OK Then
                    Return Nothing
                End If

                For Each radio In New RadioButton() {biosRadio, uefiRadio, rawRadio, uefiNtfsRadio}
                    If radio.Checked Then
                        Return CType(radio.Tag, UsbBootCreationMethod)
                    End If
                Next
            End Using

            Return Nothing
        End Function

        Private Async Sub OnBootClick(sender As Object, e As EventArgs)
            Dim drive = TryCast(_driveCombo.SelectedItem, UsbDriveOption)
            Dim item = TryCast(_isoList.SelectedItem, CacheItem)
            If drive Is Nothing Then
                MessageBox.Show("Selecione um USB.", "USB Boot", MessageBoxButtons.OK, MessageBoxIcon.Information)
                Return
            End If
            If Not IsRealCacheItem(item, ".iso") Then
                MessageBox.Show("Selecione um arquivo ISO no cache.", "USB Boot", MessageBoxButtons.OK, MessageBoxIcon.Information)
                Return
            End If
            If drive.DiskNumber < 0 Then
                MessageBox.Show("Nao foi possivel identificar o disco fisico do USB selecionado.", "USB Boot", MessageBoxButtons.OK, MessageBoxIcon.Error)
                Return
            End If
            If Not EnsureUsbUacIfNeeded() Then
                Return
            End If

            Dim analysis = Await EnsureIsoAnalysisAsync(item)
            If analysis Is Nothing Then
                Return
            End If

            Dim selectedMethod = Await ChooseBootMethodAsync(item, analysis)
            If Not selectedMethod.HasValue Then
                Return
            End If

            ' Um metodo por caminho: RAW/DD, UEFI:NTFS e copia FAT32 (BIOS ou UEFI).
            Select Case selectedMethod.Value
                Case UsbBootCreationMethod.RawDd
                    Await ExecuteRawBootAsync(drive, item, analysis)
                Case UsbBootCreationMethod.UefiNtfs
                    If Not Await EnsureUsbCapacityForLogicalIsoAsync(item.FullPath, drive, selectedMethod.Value) Then Return
                    If Not Await EnsureUefiNtfsPrerequisitesAsync(item.FullPath, analysis) Then Return
                    Await ExecuteUefiNtfsBootAsync(drive, item, analysis)
                Case Else
                    If Not Await EnsureUsbCapacityForLogicalIsoAsync(item.FullPath, drive, selectedMethod.Value) Then Return
                    Dim bootTarget = If(selectedMethod.Value = UsbBootCreationMethod.Uefi, UsbBootTarget.Uefi, UsbBootTarget.LegacyBios)
                    Await ExecuteLogicalFat32BootAsync(drive, item, analysis, bootTarget)
            End Select
        End Sub

        ''' <summary>Analise da ISO selecionada (reaproveita a ja feita). Nothing se falhar; o erro ja foi mostrado.</summary>
        Private Async Function EnsureIsoAnalysisAsync(item As CacheItem) As Task(Of IsoAnalysisResult)
            Dim analysis = item.IsoAnalysis
            If analysis Is Nothing Then
                Try
                    _statusLabel.Text = $"Analisando ISO selecionada: {item.DisplayName}"
                    SetProgressMarquee(True)
                    Await _logService.WriteAsync($"USB | ISO_ANALYZE_SELECTED | INICIO | {Path.GetFileName(item.FullPath)}")

                    analysis = Await Task.Run(Function() _isoAnalysisService.Analyze(item.FullPath))
                    Dim detectedType = MapIsoAnalysisToLegacyType(analysis)
                    item.UpdateIsoAnalysis(analysis, detectedType)
                    Await _logService.WriteAsync($"USB | ISO_ANALYZE_SELECTED | OK | {Path.GetFileName(item.FullPath)} | Recomendacao={analysis.RecommendationText} | ElTorito BIOS={analysis.HasBiosElTorito} UEFI={analysis.HasUefiElTorito}")
                    _isoList.Refresh()
                Catch ex As Exception
                    _logService.WriteAsync($"USB | ISO_ANALYZE_SELECTED | ERRO | {Path.GetFileName(item.FullPath)} | {ex.Message}").GetAwaiter().GetResult()
                    MessageBox.Show($"Falha ao analisar a ISO selecionada: {ex.Message}", "USB Boot", MessageBoxButtons.OK, MessageBoxIcon.Error)
                    Return Nothing
                Finally
                    SetProgressMarquee(False)
                End Try
            End If
            Return analysis
        End Function

        ''' <summary>Plano (politica do catalogo ou analise local) e janela de metodo. Nothing se cancelado.</summary>
        Private Async Function ChooseBootMethodAsync(item As CacheItem, analysis As IsoAnalysisResult) As Task(Of UsbBootCreationMethod?)
            Dim plan = Await BuildBootMethodPlanAsync(item, analysis)
            Dim recommendedMethod = plan.Recommended
            Await _logService.WriteAsync($"USB | BOOT_METHOD | DIALOG_OPEN | {item.DisplayName} | Recommendation={analysis.RecommendationText} | RecommendedMethod={recommendedMethod} | {plan.LogText}")
            Dim selectedMethod = SelectUsbBootCreationMethod(analysis, plan)
            If Not selectedMethod.HasValue Then
                Await _logService.WriteAsync($"USB | BOOT_METHOD | CANCEL | {item.DisplayName} | Recommendation={analysis.RecommendationText} | RecommendedMethod={recommendedMethod} | {plan.LogText}")
                Return Nothing
            End If
            ' Escolha diferente da recomendada fica registrada (com politica, so entre os metodos permitidos).
            Dim overrideText = If(selectedMethod.Value <> recommendedMethod, " | Override=True", String.Empty)
            Await _logService.WriteAsync($"USB | BOOT_METHOD | SELECTED | {item.DisplayName} | Recommendation={analysis.RecommendationText} | RecommendedMethod={recommendedMethod} | SelectedMethod={selectedMethod.Value} | {plan.LogText}{overrideText}")
            Return selectedMethod
        End Function

        ''' <summary>
        ''' Copia logica em FAT32 para BIOS ou UEFI: confirma, confere o SHA-256 da ISO, formata, copia, aplica e confere o
        ''' boot, registra o SHA-256 da imagem, verifica a gravacao e oferece ejetar.
        ''' </summary>
        Private Async Function ExecuteLogicalFat32BootAsync(drive As UsbDriveOption, item As CacheItem, analysis As IsoAnalysisResult, bootTarget As UsbBootTarget) As Task
            Dim bootTargetName = GetBootTargetDisplayName(bootTarget)

            Dim confirm = MessageBox.Show($"O USB selecionado sera formatado para criacao de USB bootavel ({bootTargetName}).{Environment.NewLine}{Environment.NewLine}Recomendacao da analise: {analysis.RecommendationText}{Environment.NewLine}{Environment.NewLine}Deseja continuar?",
                                          "USB Boot",
                                          MessageBoxButtons.YesNo,
                                          MessageBoxIcon.Warning)
            If confirm <> DialogResult.Yes Then
                Return
            End If

            Dim settings = _settingsService.Load()
            Dim verifySha256BeforeBurn = settings.AutoValidateSha256BeforeIsoBurn
            Dim cancellationToken As CancellationToken
            If Not TryBeginUsbOperation(drive, True, cancellationToken) Then
                Return
            End If
            Dim imageSha256 As String = String.Empty

            Dim isoErrorLog As String = Nothing
            Dim autoplayBackup As AutoplayBackup = Nothing
            Dim preMountLetters = GetDriveLetterSet()
            Dim isoDriveLetter As String = String.Empty
            Try
                ' A opcao "Desativar AutoPlay durante operacoes USB/ISO" valia so para TIBX e UEFI:NTFS; o restore no
                ' Finally ja existia, mas o AutoPlay nunca era desativado aqui.
                If _settingsService.Load().AutoDisableAutoplayDuringUsbOps Then
                    autoplayBackup = DisableAutoplay()
                End If
                _copyButton.Enabled = False
                _bootButton.Enabled = False
                _zeroUsbButton.Enabled = False
                Await _logService.WriteAsync($"USB | ISO | INICIO | Target={bootTargetName} | {item.DisplayName} -> Disk {drive.DiskNumber}")
                _statusLabel.Text = "Iniciando gravacao..."
                SetProgressMarquee(True)
                _statusLabel.Text = "Preparando USB..."
                SetBusyIndicator(True)
                Application.DoEvents()

                If verifySha256BeforeBurn AndAlso Not Await ConfirmIsoSha256BeforeBurnAsync(item, cancellationToken) Then
                    Return
                End If

                _statusLabel.Text = $"Validando ISO para boot {bootTargetName}..."
                Application.DoEvents()
                ValidateIsoForBootTarget(item.FullPath, bootTarget)

                If Not Await PrepareFat32UsbAsync(drive, item, analysis, bootTargetName, cancellationToken) Then
                    Return
                End If

                Await CopyAndVerifyIsoFilesAsync(drive, item, bootTargetName, cancellationToken)
                Await ApplyAndConfirmBootTargetAsync(drive, bootTarget)
                SetProgress(95)

                imageSha256 = Await RecordImageSha256Async(item, cancellationToken)
                If settings.AutoVerifySha256AfterUsbBurn AndAlso Not String.IsNullOrWhiteSpace(imageSha256) Then
                    Await VerifyWrittenUsbAsync(item, drive, imageSha256, cancellationToken)
                End If
                OfferEjectUsb(drive)
            Catch ex As OperationCanceledException
                SetProgressMarquee(False)
                isoErrorLog = $"USB | ISO | CANCELADO | {item.DisplayName}"
                _statusLabel.Text = "Operacao USB cancelada."
            Catch ex As Exception
                SetProgressMarquee(False)
                isoErrorLog = $"USB | ISO | ERRO | {ex.Message}"
                _logService.WriteAsync($"USB | ISO | TRACE | {ex}").GetAwaiter().GetResult()
                MessageBox.Show(ex.Message, "USB Boot", MessageBoxButtons.OK, MessageBoxIcon.Error)
            Finally
                If autoplayBackup IsNot Nothing Then
                    RestoreAutoplay(autoplayBackup)
                End If
                SetBusyIndicator(False)
                _copyButton.Enabled = True
                _bootButton.Enabled = True
                _zeroUsbButton.Enabled = True
                EndUsbOperation()
                RefreshUsbListKeepingDisk(drive.DiskNumber)
            End Try

            If Not String.IsNullOrWhiteSpace(isoErrorLog) Then
                Await _logService.WriteAsync(isoErrorLog)
            End If
        End Function

        ''' <summary>Confere o SHA-256 da ISO com o registrado no download. False se o tecnico preferir nao gravar.</summary>
        Private Async Function ConfirmIsoSha256BeforeBurnAsync(item As CacheItem, cancellationToken As CancellationToken) As Task(Of Boolean)
            _statusLabel.Text = "Verificando SHA-256 do ISO..."
            Application.DoEvents()
            Try
                Await _logService.WriteAsync($"USB | ISO | SHA256_INICIO | {item.DisplayName}")

                ' Obter SHA-256 do status do download (calculado durante o download)
                Dim statusMap = LoadDownloadStatusMap()
                Dim catalogName = FindCatalogNameForPath(statusMap, item.FullPath)
                Dim expectedSha256 As String = Nothing

                If Not String.IsNullOrWhiteSpace(catalogName) AndAlso statusMap.ContainsKey(catalogName) Then
                    expectedSha256 = statusMap(catalogName).ImageSha256
                End If

                If String.IsNullOrWhiteSpace(expectedSha256) Then
                    Await _logService.WriteAsync($"USB | ISO | SHA256_NAO_ENCONTRADO | {item.DisplayName}")
                    Dim sha256Confirm = MessageBox.Show("SHA-256 nao encontrado para este ISO. Deseja continuar a gravacao?",
                                                     "USB Boot",
                                                     MessageBoxButtons.YesNo,
                                                     MessageBoxIcon.Warning)
                    If sha256Confirm <> DialogResult.Yes Then
                        SetProgressMarquee(False)
                        Return False
                    End If
                Else
                    ' Calcular SHA-256 atual do arquivo e comparar
                    Dim actualSha256 = Await ComputeSha256WithProgressAsync(item.FullPath, "Verificando SHA-256 do ISO...", cancellationToken)
                    If Not String.Equals(expectedSha256, actualSha256, StringComparison.OrdinalIgnoreCase) Then
                        Await _logService.WriteAsync($"USB | ISO | SHA256_ERRO | {item.DisplayName}")
                        Dim sha256Confirm = MessageBox.Show("SHA-256 divergente para o ISO. Deseja continuar a gravacao?",
                                                         "USB Boot",
                                                         MessageBoxButtons.YesNo,
                                                         MessageBoxIcon.Warning)
                        If sha256Confirm <> DialogResult.Yes Then
                            SetProgressMarquee(False)
                            Return False
                        End If
                    Else
                        Await _logService.WriteAsync($"USB | ISO | SHA256_OK | {item.DisplayName}")
                    End If
                End If
            Catch ex As Exception
                _logService.WriteAsync($"USB | ISO | SHA256_ERRO | {item.DisplayName} | {ex.Message}").GetAwaiter().GetResult()
                Dim sha256Confirm = MessageBox.Show($"Falha ao validar SHA-256: {ex.Message}{Environment.NewLine}Deseja continuar a gravacao?",
                                                 "USB Boot",
                                                 MessageBoxButtons.YesNo,
                                                 MessageBoxIcon.Warning)
                If sha256Confirm <> DialogResult.Yes Then
                    SetProgressMarquee(False)
                    Return False
                End If
            End Try
            Return True
        End Function

        ''' <summary>Limpa e formata o USB em FAT32 com o rotulo da imagem e espera montar. False se falhou (erro ja mostrado).</summary>
        Private Async Function PrepareFat32UsbAsync(drive As UsbDriveOption, item As CacheItem, analysis As IsoAnalysisResult, bootTargetName As String, cancellationToken As CancellationToken) As Task(Of Boolean)
            Dim formatLetter = drive.DriveLetter
            If String.IsNullOrWhiteSpace(formatLetter) Then
                formatLetter = GetAvailableDriveLetter()
            End If
            If String.IsNullOrWhiteSpace(formatLetter) Then
                Throw New InvalidOperationException("Nao ha letras de unidade disponiveis.")
            End If

            Try
                SetProgressMarquee(True)
                _statusLabel.Text = $"Formatando USB para boot {bootTargetName}..."
                Await _logService.WriteAsync($"USB | NATIVE_FORMAT | REQUEST | Target={bootTargetName} | Disk {drive.DiskNumber}")
                Dim mountedLetter = Await RunDiskpartCleanAndFormatAsync(drive.DiskNumber, formatLetter, drive.SizeBytes, UsbVolumeLabels.ForIsoCopy(FindCatalogVolumeLabel(item), analysis?.Label, item.FullPath, "FAT32"), cancellationToken)
                drive.SetDriveLetter(mountedLetter)
                If Not WaitForDriveReady(drive.RootPath, 15) Then
                    Throw New IOException("Unidade USB nao ficou pronta apos a formatacao.")
                End If
                _statusLabel.Text = "USB formatado. Aguardando montagem pelo sistema..."
                Await Task.Delay(5000, cancellationToken)
                SetProgressMarquee(False)
                UpdateDriveInfo()
            Catch ex As OperationCanceledException
                Throw
            Catch ex As Exception
                SetProgressMarquee(False)
                SetBusyIndicator(False)
                _statusLabel.Text = "Falha ao formatar USB."
                MessageBox.Show($"Falha ao formatar USB: {ex.Message}", "USB Boot", MessageBoxButtons.OK, MessageBoxIcon.Error)
                Return False
            End Try
            Return True
        End Function

        ''' <summary>Copia os arquivos da ISO e confere presenca e tamanho de cada um.</summary>
        Private Async Function CopyAndVerifyIsoFilesAsync(drive As UsbDriveOption, item As CacheItem, bootTargetName As String, cancellationToken As CancellationToken) As Task
            _statusLabel.Text = $"Copiando arquivos da ISO para boot {bootTargetName}..."
            Application.DoEvents()
            Await _logService.WriteAsync($"USB | ISO | LOGICAL_COPY | Target={bootTargetName} | {item.DisplayName} -> {drive.RootPath}")
            Await CopyIsoFilesWithProgressAsync(item.FullPath, drive.RootPath,
                                                progressBase:=5, progressRange:=85,
                                                cancellationToken:=cancellationToken)

            SetProgress(90)
            _statusLabel.Text = "Validando copia logica da ISO..."
            Application.DoEvents()
            Await VerifyLogicalCopyManifestAsync(item.FullPath, drive.RootPath, cancellationToken)
            Await _logService.WriteAsync($"USB | ISO | OK | {item.DisplayName}")
            Await _logService.WriteAsync($"USB | COPY | OK | {item.DisplayName} -> {drive.RootPath}")
        End Function

        ''' <summary>Confere os arquivos UEFI e, para BIOS, grava e confere o boot BOOTMGR. Lanca IOException se o alvo nao foi confirmado.</summary>
        Private Async Function ApplyAndConfirmBootTargetAsync(drive As UsbDriveOption, bootTarget As UsbBootTarget) As Task
            _statusLabel.Text = "Validando artefatos de boot UEFI..."
            Application.DoEvents()
            Dim uefiBootOk = Await VerifyUefiBootFilesOnUsbAsync(drive.RootPath)
            If uefiBootOk Then
                Await _logService.WriteAsync($"USB | BOOT_UEFI | OK | Drive {drive.DriveLetter}")
            Else
                Await _logService.WriteAsync($"USB | BOOT_UEFI | SKIP | Drive {drive.DriveLetter} | Artefatos UEFI nao encontrados")
            End If

            If bootTarget = UsbBootTarget.LegacyBios Then
                _statusLabel.Text = "Aplicando boot BIOS Rufus-like..."
                Application.DoEvents()
                Dim biosBootOk = Await ApplyRufusBootMgrFat32Async(drive.DiskNumber, drive.DriveLetter)
                If biosBootOk Then
                    Await _logService.WriteAsync($"USB | BOOT_BIOS | OK | Drive {drive.DriveLetter}")
                Else
                    ' P-03: o alvo escolhido foi BIOS; sem o boot BIOS confirmado, o resultado e falha.
                    Await _logService.WriteAsync($"USB | BOOT_BIOS | FALHA | Drive {drive.DriveLetter} | UEFI={uefiBootOk}")
                    Throw New IOException("A copia terminou, mas o boot BIOS (Legacy) nao foi aplicado ou nao foi confirmado no USB." &
                                          If(uefiBootOk, " O boot UEFI foi confirmado; o USB deve iniciar apenas em modo UEFI.", " O boot UEFI tambem nao foi confirmado."))
                End If
            Else
                If Not uefiBootOk Then
                    Throw New IOException("A copia terminou, mas os artefatos UEFI nao foram encontrados no USB. O boot UEFI nao foi confirmado.")
                End If
            End If
        End Function

        ''' <summary>Calcula o SHA-256 da imagem e registra no historico de download. Vazio se falhou (aviso ja mostrado).</summary>
        Private Async Function RecordImageSha256Async(item As CacheItem, cancellationToken As CancellationToken) As Task(Of String)
            Dim imageSha256 As String = String.Empty
            Try
                imageSha256 = Await ComputeSha256WithProgressAsync(item.FullPath, "Calculando SHA-256 da imagem...", cancellationToken)

                ' Atualizar status com SHA256 da imagem
                Dim statusMap = LoadDownloadStatusMap()
                Dim catalogName = FindCatalogNameForPath(statusMap, item.FullPath)
                If Not String.IsNullOrWhiteSpace(catalogName) AndAlso statusMap.ContainsKey(catalogName) Then
                    Dim entry = statusMap(catalogName)
                    entry.ImageSha256 = imageSha256
                    statusMap(catalogName) = entry
                    SaveDownloadStatusMap(statusMap)
                End If

                Await _logService.WriteAsync($"SHA256 | IMAGEM | OK | {item.DisplayName} | {imageSha256}")
            Catch ex As Exception
                _logService.WriteAsync($"SHA256 | IMAGEM | ERRO | {item.DisplayName} | {ex.Message}").GetAwaiter().GetResult()
                MessageBox.Show($"Falha ao calcular SHA256 da imagem: {ex.Message}", "USB Boot", MessageBoxButtons.OK, MessageBoxIcon.Warning)
            End Try
            Return imageSha256
        End Function

        ''' <summary>Verificacao da gravacao (hash de cada arquivo ISO x USB) e mensagem ao tecnico.</summary>
        Private Async Function VerifyWrittenUsbAsync(item As CacheItem, drive As UsbDriveOption, imageSha256 As String, cancellationToken As CancellationToken) As Task
            Try
                _statusLabel.Text = "Verificando gravacao USB..."
                Application.DoEvents()
                SetProgressMarquee(True)

                Dim verifyResult = Await VerifyUsbWriteAsync(item, drive, imageSha256, cancellationToken)

                SetProgressMarquee(False)
                Select Case verifyResult.Status
                    Case UsbVerificationStatus.Success
                        _statusLabel.Text = "Verificação USB concluída com sucesso."
                        MessageBox.Show("USB verificado com sucesso! A gravação foi validada.", "USB Boot", MessageBoxButtons.OK, MessageBoxIcon.Information)
                    Case UsbVerificationStatus.Inconclusive
                        _statusLabel.Text = "Verificação USB inconclusiva."
                        MessageBox.Show(verifyResult.Message, "USB Boot", MessageBoxButtons.OK, MessageBoxIcon.Warning)
                    Case Else
                        _statusLabel.Text = "Falha na verificação USB."
                        MessageBox.Show("Falha na verificação USB! A gravação pode estar corrompida.", "USB Boot", MessageBoxButtons.OK, MessageBoxIcon.Warning)
                End Select
            Catch ex As Exception
                SetProgressMarquee(False)
                _statusLabel.Text = "Erro na verificação USB."
                _logService.WriteAsync($"USB | VERIFY | ERRO | {item.DisplayName} | {ex.Message}").GetAwaiter().GetResult()
                MessageBox.Show($"Erro na verificação USB: {ex.Message}", "USB Boot", MessageBoxButtons.OK, MessageBoxIcon.Error)
            End Try
        End Function

        ''' <summary>Pergunta se ejeta o USB com seguranca (ate 3 tentativas).</summary>
        Private Sub OfferEjectUsb(drive As UsbDriveOption)
            Dim ejectConfirm = MessageBox.Show("Deseja ejetar o USB com seguranca?", "USB Boot",
                                               MessageBoxButtons.YesNo, MessageBoxIcon.Question)
            If ejectConfirm = DialogResult.Yes Then
                Dim attempts As Integer = 0
                Dim ejected As Boolean = False
                Do
                    attempts += 1
                    Try
                        EjectDrive(drive.DriveLetter)
                        _logService.WriteAsync($"USB | EJECT | OK | {drive.RootPath}").GetAwaiter().GetResult()
                        ejected = True
                    Catch ex As Exception
                        _logService.WriteAsync($"USB | EJECT | ERRO | {ex.Message}").GetAwaiter().GetResult()
                        Dim retry = MessageBox.Show($"Falha ao ejetar: {ex.Message}{Environment.NewLine}Tentar novamente?",
                                                    "USB Boot",
                                                    MessageBoxButtons.YesNo,
                                                    MessageBoxIcon.Warning)
                        If retry <> DialogResult.Yes Then
                            Exit Do
                        End If
                    End Try
                Loop While Not ejected AndAlso attempts < 3
            End If
        End Sub

        Private Async Sub OnIdentifyUsbClick(sender As Object, e As EventArgs)
            Dim drive = TryCast(_driveCombo.SelectedItem, UsbDriveOption)
            If drive Is Nothing Then
                MessageBox.Show("Selecione um USB.", "USB Boot", MessageBoxButtons.OK, MessageBoxIcon.Information)
                Return
            End If

            ' Identificar le o disco (ate GBs no RAW): trava as outras operacoes de USB, como gravar e formatar, e aceita
            ' o Cancelar. Antes so desabilitava tres botoes e o USB podia ser formatado durante a leitura.
            Dim cancellationToken As CancellationToken
            If Not TryBeginUsbOperation(drive, False, cancellationToken) Then
                Return
            End If
            Try
                SetBusyIndicator(True)
                SetProgressMarquee(True)
                _statusLabel.Text = "Identificando USB..."
                Await _logService.WriteAsync($"USB | IDENTIFY | INICIO | Disk={drive.DiskNumber} | Letter={If(String.IsNullOrWhiteSpace(drive.DriveLetter), "N/A", drive.DriveLetter)}")

                Dim logicalResult = Await TryIdentifyLogicalUsbAsync(drive, cancellationToken)
                If logicalResult IsNot Nothing Then
                    Await _logService.WriteAsync($"USB | IDENTIFY | RESULT | Mode=logical | Catalog={logicalResult.CatalogName} | Status={logicalResult.Status} | Confidence={logicalResult.Confidence} | Verified={logicalResult.FilesVerified} | Missing={logicalResult.MissingCount} | Mismatch={logicalResult.MismatchCount} | Inaccessible={logicalResult.InaccessibleCount}")
                    ' USB gravado em RAW/DD pode aparecer com letra (volume CDFS/UDF so leitura) e mostrar so parte dos
                    ' arquivos da ISO: sem sucesso na verificacao logica, tenta o RAW antes de dar o resultado logico.
                    If logicalResult.Status <> UsbVerificationStatus.Success AndAlso drive.DiskNumber >= 0 AndAlso IsRunningAsAdmin() Then
                        Dim rawResult = Await TryIdentifyKnownRawUsbAsync(drive, cancellationToken)
                        Dim rawSource = "history"
                        If rawResult Is Nothing Then
                            rawResult = Await TryIdentifyRawByManifestAsync(drive, cancellationToken, logicalResult.CatalogName)
                            rawSource = "manifest"
                        End If
                        If rawResult Is Nothing Then
                            ' So a imagem que o sentinela apontou: varrer o catalogo inteiro leria GBs por candidato.
                            rawResult = Await TryIdentifyOfficialRawUsbAsync(drive, cancellationToken, logicalResult.CatalogName)
                            rawSource = "catalog"
                        End If
                        If rawResult IsNot Nothing Then
                            Await _logService.WriteAsync($"USB | IDENTIFY | RAW_MATCH | Source={rawSource} | Disk={drive.DiskNumber} | Catalog={rawResult.CatalogName} | LogicalStatus={logicalResult.Status}")
                            ShowRawUsbIdentificationResult(rawResult, If(rawSource = "history", "USB RAW/DD reconhecido por historico local.", If(rawSource = "manifest", $"USB RAW/DD identificado pelos arquivos: {rawResult.CatalogName}.", $"USB RAW/DD identificado e verificado: {rawResult.CatalogName}.")))
                            Return
                        End If
                    End If
                    ShowLogicalUsbIdentificationResult(logicalResult)
                    Return
                End If

                If drive.DiskNumber >= 0 Then
                    If Not IsRunningAsAdmin() Then
                        Await _logService.WriteAsync($"USB | IDENTIFY | RAW_SKIP | Disk={drive.DiskNumber} | Modo administrador ausente")
                    Else
                        Dim knownRaw = Await TryIdentifyKnownRawUsbAsync(drive, cancellationToken)
                        If knownRaw IsNot Nothing Then
                            Await _logService.WriteAsync($"USB | IDENTIFY | RAW_MATCH | Source=history | Disk={drive.DiskNumber} | Catalog={knownRaw.CatalogName}")
                            ShowRawUsbIdentificationResult(knownRaw, "USB RAW/DD reconhecido por historico local.")
                            Return
                        End If

                        Dim manifestRaw = Await TryIdentifyRawByManifestAsync(drive, cancellationToken)
                        If manifestRaw IsNot Nothing Then
                            Await _logService.WriteAsync($"USB | IDENTIFY | RAW_MATCH | Source=manifest | Disk={drive.DiskNumber} | Catalog={manifestRaw.CatalogName}")
                            ShowRawUsbIdentificationResult(manifestRaw, $"USB RAW/DD identificado pelos arquivos: {manifestRaw.CatalogName}.")
                            Return
                        End If

                        Dim officialRaw = Await TryIdentifyOfficialRawUsbAsync(drive, cancellationToken)
                        If officialRaw IsNot Nothing Then
                            Await _logService.WriteAsync($"USB | IDENTIFY | RAW_MATCH | Source=catalog | Disk={drive.DiskNumber} | Catalog={officialRaw.CatalogName}")
                            ShowRawUsbIdentificationResult(officialRaw, $"USB RAW/DD identificado e verificado: {officialRaw.CatalogName}.")
                            Return
                        End If
                    End If
                End If

                Dim bootResult = DetectUsbBootMode(drive)
                _statusLabel.Text = "Identificacao inconclusiva."
                Await _logService.WriteAsync($"USB | IDENTIFY | RESULT | Mode=unknown | Disk={drive.DiskNumber} | BIOS={bootResult.HasBiosBoot} | UEFI={bootResult.HasUefiBoot}")
                Dim details = New List(Of String) From {
                    "Status: Verificacao inconclusiva.",
                    "Nenhum manifesto de catalogo correspondeu aos arquivos do USB.",
                    If(IsRunningAsAdmin(), "RAW/DD: nenhum hash oficial ou historico local correspondeu.", "RAW/DD: execute em Modo Administrador para tentar leitura do disco fisico.")
                }
                details.AddRange(bootResult.Details)
                MessageBox.Show(String.Join(Environment.NewLine, details), "Identificar USB", MessageBoxButtons.OK, MessageBoxIcon.Warning)
            Catch ex As OperationCanceledException
                _statusLabel.Text = "Identificacao cancelada."
                _logService.WriteAsync("USB | IDENTIFY | CANCELADO").GetAwaiter().GetResult()
            Catch ex As Exception
                _statusLabel.Text = "Falha ao identificar USB."
                _logService.WriteAsync($"USB | IDENTIFY | ERRO | {ex.Message}").GetAwaiter().GetResult()
                MessageBox.Show($"Falha ao identificar USB: {ex.Message}", "Identificar USB", MessageBoxButtons.OK, MessageBoxIcon.Error)
            Finally
                SetProgressMarquee(False)
                SetBusyIndicator(False)
                EndUsbOperation()
            End Try
        End Sub

        Private Async Sub OnVerifyTibxOnUsbClick(sender As Object, e As EventArgs)
            Dim drive = TryCast(_driveCombo.SelectedItem, UsbDriveOption)
            Dim item = TryCast(_tibxList.SelectedItem, CacheItem)
            If drive Is Nothing OrElse String.IsNullOrWhiteSpace(drive.RootPath) OrElse Not Directory.Exists(drive.RootPath) Then
                MessageBox.Show("Selecione um USB com letra atribuida.", "USB Boot", MessageBoxButtons.OK, MessageBoxIcon.Information)
                Return
            End If
            If Not IsRealCacheItem(item, ".tibx") Then
                MessageBox.Show("Selecione o TIBX do cache que sera usado como referencia.", "USB Boot", MessageBoxButtons.OK, MessageBoxIcon.Information)
                Return
            End If

            Using dialog As New OpenFileDialog()
                dialog.Title = "Selecionar TIBX no USB"
                dialog.InitialDirectory = drive.RootPath
                dialog.Filter = "Arquivos TIBX (*.tibx)|*.tibx|Todos os arquivos (*.*)|*.*"
                dialog.CheckFileExists = True
                dialog.Multiselect = False

                If dialog.ShowDialog(Me) <> DialogResult.OK Then
                    Return
                End If

                Dim selectedUsbFile = dialog.FileName
                Dim cancellationToken As CancellationToken
                If Not TryBeginUsbOperation(drive, False, cancellationToken) Then
                    Return
                End If
                Try
                    SetUsbActionButtonsEnabled(False)
                    SetBusyIndicator(True)
                    _statusLabel.Text = "Verificando TIBX selecionado no USB..."
                    Await _logService.WriteAsync($"USB | TIBX_VERIFY | INICIO | Cache={item.FullPath} | USB={selectedUsbFile}")

                    Dim sourceInfo = New FileInfo(item.FullPath)
                    Dim usbInfo = New FileInfo(selectedUsbFile)
                    If sourceInfo.Length <> usbInfo.Length Then
                        Await _logService.WriteAsync($"USB | TIBX_VERIFY | SIZE_DIVERGENTE | CacheBytes={sourceInfo.Length} | UsbBytes={usbInfo.Length} | USB={selectedUsbFile}")
                        _statusLabel.Text = "TIBX no USB diverge do cache."
                        MessageBox.Show($"TIBX divergente: tamanho diferente.{Environment.NewLine}{Environment.NewLine}Cache: {FormatBytes(sourceInfo.Length)}{Environment.NewLine}USB: {FormatBytes(usbInfo.Length)}", "Verificar TIBX no USB", MessageBoxButtons.OK, MessageBoxIcon.Warning)
                        Return
                    End If

                    Dim sourceSha256 = Await ComputeSha256WithProgressAsync(item.FullPath, "Calculando SHA-256 do TIBX no cache...", cancellationToken)
                    Dim usbSha256 = Await ComputeSha256WithProgressAsync(selectedUsbFile, "Calculando SHA-256 do TIBX no USB...", cancellationToken)
                    Dim match = String.Equals(sourceSha256, usbSha256, StringComparison.OrdinalIgnoreCase)
                    Await _logService.WriteAsync($"USB | TIBX_VERIFY | SHA256_{If(match, "OK", "DIVERGENTE")} | Cache={item.FullPath} | USB={selectedUsbFile} | CacheSHA256={sourceSha256} | UsbSHA256={usbSha256}")

                    If match Then
                        _statusLabel.Text = "TIBX no USB validado com sucesso."
                        MessageBox.Show($"TIBX no USB corresponde ao cache selecionado.{Environment.NewLine}{Environment.NewLine}Arquivo USB: {selectedUsbFile}{Environment.NewLine}SHA-256: {usbSha256}", "Verificar TIBX no USB", MessageBoxButtons.OK, MessageBoxIcon.Information)
                    Else
                        _statusLabel.Text = "TIBX no USB diverge do cache."
                        MessageBox.Show($"TIBX divergente: SHA-256 diferente.{Environment.NewLine}{Environment.NewLine}Cache: {sourceSha256}{Environment.NewLine}USB: {usbSha256}", "Verificar TIBX no USB", MessageBoxButtons.OK, MessageBoxIcon.Warning)
                    End If
                Catch ex As OperationCanceledException
                    _statusLabel.Text = "Verificacao TIBX cancelada."
                    _logService.WriteAsync($"USB | TIBX_VERIFY | CANCELADO | USB={selectedUsbFile}").GetAwaiter().GetResult()
                Catch ex As Exception
                    _statusLabel.Text = "Falha ao verificar TIBX no USB."
                    _logService.WriteAsync($"USB | TIBX_VERIFY | ERRO | USB={selectedUsbFile} | {ex.Message}").GetAwaiter().GetResult()
                    MessageBox.Show($"Falha ao verificar TIBX no USB: {ex.Message}", "USB Boot", MessageBoxButtons.OK, MessageBoxIcon.Error)
                Finally
                    SetProgressMarquee(False)
                    SetBusyIndicator(False)
                    SetUsbActionButtonsEnabled(True)
                    EndUsbOperation()
                End Try
            End Using
        End Sub

        Private Async Function ExecuteRawBootAsync(drive As UsbDriveOption, item As CacheItem, analysis As IsoAnalysisResult) As Task
            Dim imageSize = New FileInfo(item.FullPath).Length
            Dim diskSize = If(drive.SizeBytes > 0, drive.SizeBytes, GetDiskSizeBytes(drive.DiskNumber))
            If diskSize > 0 AndAlso imageSize > diskSize Then
                MessageBox.Show($"A ISO tem {FormatBytes(imageSize)}, mas o USB tem {FormatBytes(diskSize)}. A gravacao RAW/DD nao pode continuar.", "USB Boot", MessageBoxButtons.OK, MessageBoxIcon.Error)
                Return
            End If

            Dim recommendationText = analysis.RecommendationText
            Dim warning = "ATENCAO: o modo RAW/DD grava a imagem inteira diretamente no disco fisico selecionado." & Environment.NewLine & Environment.NewLine &
                          $"ISO: {Path.GetFileName(item.FullPath)}" & Environment.NewLine &
                          $"Recomendacao da analise: {recommendationText}" & Environment.NewLine &
                          $"Destino: Disk {drive.DiskNumber} - {drive.Model} ({FormatBytes(diskSize)})" & Environment.NewLine & Environment.NewLine &
                          "Todo o conteudo, particoes e letras atuais do USB serao apagados. Deseja continuar?"
            If MessageBox.Show(warning, "Confirmar Boot RAW/DD", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) <> DialogResult.Yes Then
                Return
            End If

            If analysis.Recommendation <> IsoBootRecommendation.RawRecommended AndAlso analysis.Recommendation <> IsoBootRecommendation.Unsupported Then
                Dim secondConfirm = MessageBox.Show("A analise nao marcou RAW/DD como o modo preferencial para esta ISO. Confirmar mesmo assim a gravacao bruta?",
                                                   "Confirmar Boot RAW/DD",
                                                   MessageBoxButtons.YesNo,
                                                   MessageBoxIcon.Warning)
                If secondConfirm <> DialogResult.Yes Then
                    Return
                End If
            End If

            Dim settings = _settingsService.Load()
            Dim cancellationToken As CancellationToken
            If Not TryBeginUsbOperation(drive, True, cancellationToken) Then
                Return
            End If
            Dim imageSha256 As String = String.Empty
            Try
                _copyButton.Enabled = False
                _bootButton.Enabled = False
                _verifyBootButton.Enabled = False
                _zeroUsbButton.Enabled = False
                SetBusyIndicator(True)
                SetProgress(0)
                _statusLabel.Text = "Iniciando gravacao RAW/DD..."
                Await _logService.WriteAsync($"USB | RAW_DD | INICIO | {item.DisplayName} -> Disk {drive.DiskNumber} | Recommendation={recommendationText}")

                If settings.AutoValidateSha256BeforeIsoBurn Then
                    imageSha256 = Await ComputeSha256WithProgressAsync(item.FullPath, "Calculando SHA-256 da ISO...", cancellationToken)
                    Await _logService.WriteAsync($"USB | RAW_DD | SHA256_IMAGEM | {item.DisplayName} | {imageSha256}")
                End If

                _statusLabel.Text = "Gravando imagem RAW/DD no USB..."
                Dim verification = Await RawCopyAsync(item.FullPath, drive.DiskNumber, cancellationToken, settings.AutoVerifySha256AfterUsbBurn)
                Await _logService.WriteAsync($"USB | RAW_DD | GRAVACAO_OK | {item.DisplayName} -> Disk {drive.DiskNumber}")
                ' Como o Rufus: a particao EFI da imagem fica sem letra, para o Windows nao escrever nela.
                Dim espLetterError As String = Nothing
                Try
                    Await RemoveEfiPartitionLettersAsync(drive.DiskNumber)
                Catch ex As Exception
                    espLetterError = ex.Message
                End Try
                If espLetterError IsNot Nothing Then
                    Await _logService.WriteAsync($"USB | RAW_DD | ESP_LETRA | ERRO | {espLetterError}")
                End If

                Dim rawVerified As Boolean? = Nothing
                If settings.AutoVerifySha256AfterUsbBurn Then
                    Dim rawOk = verification IsNot Nothing AndAlso verification.MatchesImage
                    rawVerified = rawOk
                    If rawOk Then
                        _statusLabel.Text = "USB RAW/DD verificado com sucesso."
                        MessageBox.Show("USB RAW/DD verificado com sucesso.", "USB Boot", MessageBoxButtons.OK, MessageBoxIcon.Information)
                    Else
                        _statusLabel.Text = "Falha na verificacao RAW/DD."
                        MessageBox.Show("Falha na comparacao binaria do USB RAW/DD.", "USB Boot", MessageBoxButtons.OK, MessageBoxIcon.Warning)
                    End If
                Else
                    _statusLabel.Text = "USB RAW/DD criado."
                    MessageBox.Show("Gravacao RAW/DD concluida. Remova e reconecte o USB se o Windows nao atualizar as particoes imediatamente.", "USB Boot", MessageBoxButtons.OK, MessageBoxIcon.Information)
                End If
                Await SaveRawUsbWriteStatusAsync(item, drive, analysis, imageSha256, rawVerified, cancellationToken)
            Catch ex As OperationCanceledException
                _statusLabel.Text = "Operacao USB cancelada."
                _logService.WriteAsync($"USB | RAW_DD | CANCELADO | {item.DisplayName}").GetAwaiter().GetResult()
            Catch ex As Exception
                _statusLabel.Text = "Falha na gravacao RAW/DD."
                _logService.WriteAsync($"USB | RAW_DD | ERRO | {item.DisplayName} | {ex}").GetAwaiter().GetResult()
                MessageBox.Show($"Falha na gravacao RAW/DD: {ex.Message}", "USB Boot", MessageBoxButtons.OK, MessageBoxIcon.Error)
            Finally
                SetBusyIndicator(False)
                _copyButton.Enabled = True
                _bootButton.Enabled = True
                _verifyBootButton.Enabled = True
                _zeroUsbButton.Enabled = True
                EndUsbOperation()
                ' Depois do RAW/DD o Windows remonta as particoes da imagem: relista para a linha do USB nao ficar velha.
                RefreshUsbListKeepingDisk(drive.DiskNumber)
            End Try
        End Function

        Private Async Sub OnVerifyBootClick(sender As Object, e As EventArgs)
            Dim drive = TryCast(_driveCombo.SelectedItem, UsbDriveOption)
            Dim selectedIso = TryCast(_isoList.SelectedItem, CacheItem)
            If drive Is Nothing Then
                MessageBox.Show("Selecione um USB.", "USB Boot", MessageBoxButtons.OK, MessageBoxIcon.Information)
                Return
            End If

            Dim cancellationToken As CancellationToken = CancellationToken.None
            Dim operationStarted = False
            Try
                If Not TryBeginUsbOperation(drive, False, cancellationToken) Then
                    Return
                End If
                operationStarted = True
                _statusLabel.Text = "Verificando boot do USB..."
                SetBusyIndicator(True)
                SetProgressMarquee(True)

                Dim logicalResult = Await TryIdentifyLogicalUsbAsync(drive, cancellationToken)
                If logicalResult IsNot Nothing Then
                    Await _logService.WriteAsync($"USB | BOOT_DETECT_MANIFEST | Disk={drive.DiskNumber} | Catalog={logicalResult.CatalogName} | Status={logicalResult.Status} | Confidence={logicalResult.Confidence} | Verified={logicalResult.FilesVerified} | Missing={logicalResult.MissingCount} | Mismatch={logicalResult.MismatchCount} | Inaccessible={logicalResult.InaccessibleCount}")
                    ShowLogicalUsbIdentificationResult(logicalResult, "Verificacao de Boot USB")
                    Return
                End If

                If Not IsRunningAsAdmin() Then
                    _statusLabel.Text = "Modo Administrador necessario para verificacao fisica do USB."
                    MessageBox.Show("Nenhum manifesto correspondeu ao USB. Para tentar identificacao RAW/DD, MBR, particao ativa e setor de boot, execute em Modo Administrador.", "USB Boot", MessageBoxButtons.OK, MessageBoxIcon.Warning)
                    Return
                End If

                Dim knownRaw = Await TryIdentifyKnownRawUsbAsync(drive, cancellationToken)
                If knownRaw IsNot Nothing Then
                    Dim rawSummary = "USB RAW/DD reconhecido por historico local: corresponde a uma imagem gravada anteriormente por este aplicativo."
                    _statusLabel.Text = rawSummary
                    Await _logService.WriteAsync($"USB | BOOT_DETECT_RAW_HISTORY | Disk={drive.DiskNumber} | Catalog={knownRaw.CatalogName} | ImageSizeBytes={knownRaw.ImageSizeBytes} | ExpectedSHA256={knownRaw.ImageSha256} | CalculatedSHA256={knownRaw.CalculatedUsbSha256} | {rawSummary}")
                    ShowRawUsbIdentificationResult(knownRaw, rawSummary, "Verificacao de Boot USB")
                    Return
                End If

                Dim manifestRaw = Await TryIdentifyRawByManifestAsync(drive, cancellationToken)
                If manifestRaw IsNot Nothing Then
                    Dim manifestSummary = $"USB RAW/DD corresponde a imagem {manifestRaw.CatalogName} (arquivos conferidos com o manifesto)."
                    _statusLabel.Text = manifestSummary
                    Await _logService.WriteAsync($"USB | BOOT_DETECT_RAW_MANIFEST | OK | Disk={drive.DiskNumber} | Catalog={manifestRaw.CatalogName} | {manifestSummary}")
                    ShowRawUsbIdentificationResult(manifestRaw, manifestSummary, "Verificacao de Boot USB")
                    Return
                End If

                Dim officialRaw = Await TryIdentifyOfficialRawUsbAsync(drive, cancellationToken)
                If officialRaw IsNot Nothing Then
                    Dim rawSummary = $"USB corresponde a imagem oficial {officialRaw.CatalogName}."
                    _statusLabel.Text = rawSummary
                    Await _logService.WriteAsync($"USB | BOOT_DETECT_RAW_CATALOG | OK | Disk={drive.DiskNumber} | Catalog={officialRaw.CatalogName} | ImageSizeBytes={officialRaw.ImageSizeBytes} | ExpectedSHA256={officialRaw.ImageSha256} | CalculatedSHA256={officialRaw.CalculatedUsbSha256} | {rawSummary}")
                    ShowRawUsbIdentificationResult(officialRaw, rawSummary, "Verificacao de Boot USB")
                    Return
                End If

                If IsRealCacheItem(selectedIso, ".iso") Then
                    Dim analysis = If(selectedIso.IsoAnalysis, _isoAnalysisService.Analyze(selectedIso.FullPath))
                    If analysis.Recommendation = IsoBootRecommendation.RawRecommended OrElse analysis.Recommendation = IsoBootRecommendation.Unsupported Then
                        _statusLabel.Text = "Verificando imagem RAW/DD no USB..."
                        Await _logService.WriteAsync($"USB | BOOT_DETECT_RAW | INICIO | Disk={drive.DiskNumber} | {selectedIso.DisplayName}")
                        Dim rawMatches = Await CompareImageWithUsbAsync(selectedIso.FullPath, drive.DiskNumber, cancellationToken)
                        Dim rawDetails As New List(Of String) From {
                            $"Imagem identificada: {selectedIso.DisplayName}.",
                            $"Arquivo: {selectedIso.FullPath}.",
                            $"Imagem RAW/DD: {If(rawMatches, "corresponde ao ISO selecionado", "nao corresponde ao ISO selecionado")}.",
                            $"Recomendacao da analise: {analysis.RecommendationText}.",
                            $"El Torito BIOS: {If(analysis.HasBiosElTorito, "detectado", "nao detectado")}.",
                            $"El Torito UEFI: {If(analysis.HasUefiElTorito, "detectado", "nao detectado")}.",
                            $"Acronis Bootwiz: {If(analysis.HasAcronisBootwiz, "detectado", "nao detectado")}.",
                            "Observacao: RAW/DD preserva a estrutura da imagem original; a verificacao por BOOTMGR/EFI em FAT32 nao se aplica a este modo."
                        }
                        Dim rawSummary = If(rawMatches,
                                            $"USB RAW/DD confirmado: imagem gravada corresponde a {selectedIso.DisplayName}.",
                                            "USB RAW/DD nao confirmado: imagem gravada diverge do ISO selecionado.")
                        _statusLabel.Text = rawSummary
                        If rawMatches Then
                            Await SaveRawUsbWriteStatusAsync(selectedIso, drive, analysis, String.Empty, True, cancellationToken)
                        End If
                        Await _logService.WriteAsync($"USB | BOOT_DETECT_RAW | Disk={drive.DiskNumber} | Match={rawMatches} | BIOS_ELTORITO={analysis.HasBiosElTorito} | UEFI_ELTORITO={analysis.HasUefiElTorito} | ACRONIS={analysis.HasAcronisBootwiz} | {rawSummary}")
                        MessageBox.Show($"{rawSummary}{Environment.NewLine}{Environment.NewLine}{String.Join(Environment.NewLine, rawDetails)}",
                                        "Verificacao de Boot USB",
                                        MessageBoxButtons.OK,
                                        If(rawMatches, MessageBoxIcon.Information, MessageBoxIcon.Warning))
                        Return
                    End If
                End If

                Dim result = DetectUsbBootMode(drive)
                Await _logService.WriteAsync($"USB | BOOT_DETECT_RAW_CATALOG | NO_MATCH | Disk={drive.DiskNumber} | USB nao corresponde aos hashes oficiais conhecidos.")

                If Not result.HasBiosBoot AndAlso Not result.HasUefiBoot Then
                    result.Details.Add("RAW/DD: para confirmar midias gravadas em modo bruto, selecione a ISO usada na gravacao e execute a verificacao novamente.")
                    result.Details.Add("Catalogo oficial: USB nao corresponde aos hashes oficiais conhecidos.")
                End If
                _statusLabel.Text = result.Summary
                Await _logService.WriteAsync($"USB | BOOT_DETECT | Disk={drive.DiskNumber} | Letter={If(String.IsNullOrWhiteSpace(drive.DriveLetter), "N/A", drive.DriveLetter)} | BIOS={result.HasBiosBoot} | UEFI={result.HasUefiBoot} | {result.Summary}")

                Dim detail = String.Join(Environment.NewLine, result.Details)
                MessageBox.Show($"{result.Summary}{Environment.NewLine}{Environment.NewLine}{detail}",
                                "Verificacao de Boot USB",
                                MessageBoxButtons.OK,
                                If(result.HasBiosBoot OrElse result.HasUefiBoot, MessageBoxIcon.Information, MessageBoxIcon.Warning))
            Catch ex As OperationCanceledException
                _statusLabel.Text = "Verificacao de boot cancelada."
                _logService.WriteAsync("USB | BOOT_DETECT | CANCELADO").GetAwaiter().GetResult()
            Catch ex As Exception
                _statusLabel.Text = "Falha ao verificar boot do USB."
                _logService.WriteAsync($"USB | BOOT_DETECT | ERRO | {ex.Message}").GetAwaiter().GetResult()
                MessageBox.Show($"Falha ao verificar boot do USB: {ex.Message}", "USB Boot", MessageBoxButtons.OK, MessageBoxIcon.Error)
            Finally
                SetProgressMarquee(False)
                SetBusyIndicator(False)
                If operationStarted Then
                    EndUsbOperation()
                End If
            End Try
        End Sub

        ' Coleta os fatos do USB (arquivos, sistema de arquivos, MBR e setor de boot do volume); a avaliacao e do Core.
        Private Function DetectUsbBootMode(drive As UsbDriveOption) As UsbBootDetection
            Dim rootPath = If(drive.RootPath, String.Empty)
            Dim facts As New UsbBootFacts With {
                .RootAccessible = Not String.IsNullOrWhiteSpace(rootPath) AndAlso Directory.Exists(rootPath),
                .IsUefiNtfsLayout = Function() IsUefiNtfsLayout(drive.DiskNumber)
            }
            If facts.RootAccessible Then
                facts.HasBootMgr = File.Exists(Path.Combine(rootPath, "bootmgr"))
                facts.HasUefiBootx64 = File.Exists(Path.Combine(rootPath, "EFI", "BOOT", "BOOTX64.EFI"))
                Try
                    facts.DriveFormat = New DriveInfo(rootPath).DriveFormat
                Catch ex As Exception
                    facts.DriveFormatError = ex.Message
                End Try
            End If

            facts.Mbr = TryReadPhysicalSector(drive.DiskNumber, 0)
            facts.VolumeBootSector = TryReadVolumeBootSector(drive.DriveLetter)
            Dim activePartitionStart = MbrLayout.GetPartitionStartLba(facts.Mbr, MbrLayout.GetActivePartitionIndex(facts.Mbr))
            If facts.VolumeBootSector Is Nothing AndAlso activePartitionStart > 0 Then
                facts.VolumeBootSector = TryReadPhysicalSector(drive.DiskNumber, CLng(activePartitionStart) * 512L)
            End If
            Return UsbBootDetection.Evaluate(facts)
        End Function

        Private Async Function TryIdentifyLogicalUsbAsync(drive As UsbDriveOption, cancellationToken As CancellationToken) As Task(Of LogicalUsbIdentificationResult)
            If drive Is Nothing OrElse String.IsNullOrWhiteSpace(drive.RootPath) OrElse Not Directory.Exists(drive.RootPath) Then
                Await _logService.WriteAsync($"USB | IDENTIFY | LOGICAL_SKIP | Disk={If(drive Is Nothing, -1, drive.DiskNumber)} | Volume inacessivel")
                Return Nothing
            End If

            Dim catalog = _catalogService.LoadAllCatalogs(AppPaths.DataDir)
            For Each item In catalog
                If item Is Nothing OrElse item.UsbIdentification Is Nothing Then Continue For
                For Each mode In item.UsbIdentification.EffectiveModes
                    cancellationToken.ThrowIfCancellationRequested()
                    If mode Is Nothing OrElse Not String.Equals(mode.Mode, "logical", StringComparison.OrdinalIgnoreCase) Then Continue For
                    If Not Await LogicalSentinelsMatchAsync(drive.RootPath, mode, cancellationToken) Then Continue For

                    Await _logService.WriteAsync($"USB | IDENTIFY | SENTINEL_MATCH | Disk={drive.DiskNumber} | Catalog={item.Name} | TreeHash={mode.TreeHash}")
                    Dim manifest = Await LoadUsbManifestAsync(mode, item.Name, cancellationToken)
                    If manifest Is Nothing OrElse manifest.Entries.Count = 0 Then
                        Return New LogicalUsbIdentificationResult With {
                            .CatalogName = item.Name,
                            .ModeName = DetectLogicalModeName(drive),
                            .Confidence = "parcial",
                            .Status = UsbVerificationStatus.Inconclusive,
                            .ManifestSource = "indisponivel",
                            .TreeHash = mode.TreeHash,
                            .ImageSha256 = item.Sha256,
                            .Details = New List(Of String) From {
                                $"Imagem provavel: {item.Name}",
                                "Status: Verificacao inconclusiva.",
                                "Sentinelas conferem, mas o manifesto completo nao foi encontrado ou esta vazio."
                            }
                        }
                    End If

                    Await _logService.WriteAsync($"USB | IDENTIFY | MANIFEST_OK | Catalog={item.Name} | Source={manifest.Source} | Files={manifest.Entries.Count}")
                    Return Await CompareUsbWithManifestAsync(drive, item, mode, manifest, cancellationToken)
                Next
            Next

            Return Nothing
        End Function

        Private Async Function LogicalSentinelsMatchAsync(rootPath As String, mode As UsbIdentificationMode, cancellationToken As CancellationToken) As Task(Of Boolean)
            If mode.SentinelFiles Is Nothing OrElse mode.SentinelFiles.Count = 0 Then Return False
            For Each sentinel In mode.SentinelFiles
                cancellationToken.ThrowIfCancellationRequested()
                If sentinel Is Nothing OrElse String.IsNullOrWhiteSpace(sentinel.Path) OrElse String.IsNullOrWhiteSpace(sentinel.Sha256) Then Return False
                Dim usbPath = Path.Combine(rootPath, UsbManifest.NormalizeRelativePath(sentinel.Path))
                If Not File.Exists(usbPath) Then Return False
                Dim fileInfo As New FileInfo(usbPath)
                If sentinel.Size.HasValue AndAlso sentinel.Size.Value >= 0 AndAlso fileInfo.Length <> sentinel.Size.Value Then Return False
                Dim actualSha256 = Await ComputeImageSha256Async(usbPath, cancellationToken)
                If Not String.Equals(actualSha256, sentinel.Sha256.Trim(), StringComparison.OrdinalIgnoreCase) Then Return False
            Next
            Return True
        End Function

        Private Async Function LoadUsbManifestAsync(mode As UsbIdentificationMode, catalogName As String, cancellationToken As CancellationToken) As Task(Of UsbManifestData)
            If mode Is Nothing OrElse String.IsNullOrWhiteSpace(mode.ManifestUrl) Then Return Nothing
            Directory.CreateDirectory(AppPaths.ManifestCacheDir)
            Dim manifestFileName = ResolveManifestFileName(mode, catalogName)
            Dim cachePath = Path.Combine(AppPaths.ManifestCacheDir, manifestFileName)

            If File.Exists(cachePath) AndAlso ManifestHashMatches(cachePath, mode.ManifestSha256) Then
                Return ParseUsbManifest(cachePath, "cache")
            End If

            Dim manifestUrl = mode.ManifestUrl.Trim()
            ' P3: servidor principal e, se falhar ou vier com outro hash, o mesmo caminho no fallback (mateweb).
            Dim candidates = ManifestSources.GetCandidateUrls(manifestUrl, EndpointConfig.Current.PrimaryBaseUrl, EndpointConfig.Current.FallbackPublicBaseUrl)
            For index = 0 To candidates.Count - 1
                Dim candidateUrl = candidates(index)
                Dim sourceName = If(index = 0, "remoto", "fallback")
                Dim uri As Uri = Nothing
                If Not Uri.TryCreate(candidateUrl, UriKind.Absolute, uri) OrElse (uri.Scheme <> Uri.UriSchemeHttp AndAlso uri.Scheme <> Uri.UriSchemeHttps) Then
                    Continue For
                End If
                Dim downloadFailureMessage As String = Nothing
                Try
                    Await _logService.WriteAsync($"USB | IDENTIFY | MANIFEST_DOWNLOAD | Catalog={catalogName} | Origem={sourceName} | Url={candidateUrl}")
                    Dim bytes = Await _md5Client.GetByteArrayAsync(uri, cancellationToken)
                    If Not String.IsNullOrWhiteSpace(mode.ManifestSha256) Then
                        Dim downloadedSha256 = ComputeBytesSha256(bytes)
                        If Not String.Equals(downloadedSha256, mode.ManifestSha256.Trim(), StringComparison.OrdinalIgnoreCase) Then
                            Await _logService.WriteAsync($"USB | IDENTIFY | MANIFEST_REMOTE_HASH_MISMATCH | Catalog={catalogName} | Origem={sourceName} | Expected={mode.ManifestSha256.Trim()} | Actual={downloadedSha256}")
                        Else
                            Await File.WriteAllBytesAsync(cachePath, bytes, cancellationToken)
                            Return ParseUsbManifest(cachePath, sourceName)
                        End If
                    Else
                        Await File.WriteAllBytesAsync(cachePath, bytes, cancellationToken)
                        Return ParseUsbManifest(cachePath, sourceName)
                    End If
                Catch ex As Exception When Not TypeOf ex Is OperationCanceledException
                    downloadFailureMessage = $"USB | IDENTIFY | MANIFEST_DOWNLOAD_FAIL | Catalog={catalogName} | Origem={sourceName} | {ex.GetType().Name}: {ex.Message}"
                End Try
                If Not String.IsNullOrWhiteSpace(downloadFailureMessage) Then
                    Await _logService.WriteAsync(downloadFailureMessage)
                End If
            Next

            Dim trustedLocal = TryLoadTrustedLocalUsbManifest(manifestFileName, manifestUrl, mode.ManifestSha256)
            If trustedLocal IsNot Nothing Then Return trustedLocal

            Dim relativePath = manifestUrl.Replace("/"c, Path.DirectorySeparatorChar)
            Dim localCandidates = New String() {
                Path.Combine(AppPaths.ManifestCacheDir, Path.GetFileName(relativePath)),
                Path.Combine(AppPaths.DataDir, relativePath),
                Path.Combine(AppPaths.DataRoot, relativePath)
            }
            For Each candidate In localCandidates
                If File.Exists(candidate) AndAlso ManifestHashMatches(candidate, mode.ManifestSha256) Then
                    Return ParseUsbManifest(candidate, "local")
                End If
            Next

            Return Nothing
        End Function

        Private Function TryLoadTrustedLocalUsbManifest(manifestFileName As String, manifestUrl As String, expectedSha256 As String) As UsbManifestData
            Dim candidates As New List(Of String) From {
                Path.Combine(AppPaths.ManifestCacheDir, manifestFileName),
                Path.Combine(AppPaths.DataDir, "manifests", manifestFileName),
                Path.Combine(AppPaths.DataRoot, "config", "manifests", manifestFileName),
                Path.Combine(AppPaths.DataRoot, "manifests", manifestFileName)
            }

            Dim relativePath = If(manifestUrl, String.Empty).Trim().Replace("/"c, Path.DirectorySeparatorChar)
            If Not String.IsNullOrWhiteSpace(relativePath) AndAlso Not Path.IsPathRooted(relativePath) Then
                candidates.Add(Path.Combine(AppPaths.DataDir, relativePath))
                candidates.Add(Path.Combine(AppPaths.DataRoot, relativePath))
            End If

            Dim seen As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
            For Each candidate In candidates
                If String.IsNullOrWhiteSpace(candidate) OrElse Not seen.Add(candidate) Then Continue For
                If File.Exists(candidate) AndAlso ManifestHashMatches(candidate, expectedSha256) Then
                    Return ParseUsbManifest(candidate, "local")
                End If
            Next

            Return Nothing
        End Function

        Private Function ResolveManifestFileName(mode As UsbIdentificationMode, catalogName As String) As String
            Dim manifestUrl = If(mode.ManifestUrl, String.Empty).Trim()
            Dim uri As Uri = Nothing
            If Uri.TryCreate(manifestUrl, UriKind.Absolute, uri) Then
                Dim name = Path.GetFileName(uri.LocalPath)
                If Not String.IsNullOrWhiteSpace(name) Then Return name
            End If
            Dim relativeName = Path.GetFileName(manifestUrl.Replace("/"c, Path.DirectorySeparatorChar))
            If Not String.IsNullOrWhiteSpace(relativeName) Then Return relativeName
            Dim key = If(Not String.IsNullOrWhiteSpace(mode.TreeHash), mode.TreeHash.Trim(), catalogName)
            Dim safeKey = Regex.Replace(key, "[^A-Za-z0-9._-]+", "-").Trim("-"c)
            Return If(String.IsNullOrWhiteSpace(safeKey), "usb.manifest.json", safeKey & ".manifest.json")
        End Function

        Private Function ManifestHashMatches(path As String, expectedSha256 As String) As Boolean
            If String.IsNullOrWhiteSpace(expectedSha256) Then Return True
            Return String.Equals(ComputeBytesSha256(File.ReadAllBytes(path)), expectedSha256.Trim(), StringComparison.OrdinalIgnoreCase)
        End Function

        Private Shared Function ComputeBytesSha256(bytes As Byte()) As String
            Using sha256 = System.Security.Cryptography.SHA256.Create()
                Return BitConverter.ToString(sha256.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant()
            End Using
        End Function

        Private Function ParseUsbManifest(path As String, source As String) As UsbManifestData
            Return UsbManifest.Parse(File.ReadAllText(path), source, path)
        End Function

        Private Async Function CompareUsbWithManifestAsync(drive As UsbDriveOption, item As CatalogItem, mode As UsbIdentificationMode, manifest As UsbManifestData, cancellationToken As CancellationToken) As Task(Of LogicalUsbIdentificationResult)
            Dim check = Await ManifestVerifier.VerifyAsync(drive.RootPath, manifest.Entries,
                Function(path, token) ComputeImageSha256Async(path, token),
                Function(progress) LogIdentifyManifestProgressAsync(item.Name, progress.Checked, manifest.Entries.Count, progress),
                cancellationToken)
            Dim result As New LogicalUsbIdentificationResult With {
                .CatalogName = item.Name,
                .ModeName = DetectLogicalModeName(drive),
                .ManifestSource = manifest.Source,
                .TreeHash = mode.TreeHash,
                .ImageSha256 = item.Sha256,
                .FilesVerified = check.FilesVerified,
                .SizeOnlyVerified = check.SizeOnlyVerified,
                .MissingCount = check.MissingCount,
                .MismatchCount = check.MismatchCount,
                .InaccessibleCount = check.InaccessibleCount,
                .Status = ToVerificationStatus(check.Status),
                .Confidence = check.Confidence
            }
            result.Details.AddRange(check.Details)
            Return result
        End Function

        Private Shared Function ToVerificationStatus(status As UsbCheckStatus) As UsbVerificationStatus
            Select Case status
                Case UsbCheckStatus.Success : Return UsbVerificationStatus.Success
                Case UsbCheckStatus.Inconclusive : Return UsbVerificationStatus.Inconclusive
                Case Else : Return UsbVerificationStatus.Failed
            End Select
        End Function

        Private Async Function LogIdentifyManifestProgressAsync(catalogName As String, checkedEntries As Integer, totalEntries As Integer, result As ManifestCheckResult) As Task
            If totalEntries <= 0 Then Return
            If checkedEntries <> totalEntries AndAlso checkedEntries Mod 25 <> 0 Then Return
            Await _logService.WriteAsync($"USB | IDENTIFY | MANIFEST_PROGRESS | Catalog={catalogName} | Checked={checkedEntries}/{totalEntries} | Verified={result.FilesVerified} | Missing={result.MissingCount} | Mismatch={result.MismatchCount} | Inaccessible={result.InaccessibleCount} | SizeOnly={result.SizeOnlyVerified}")
        End Function

        Private Function DetectLogicalModeName(drive As UsbDriveOption) As String
            Return DetectUsbBootMode(drive).ModeName
        End Function

        Private Sub ShowLogicalUsbIdentificationResult(result As LogicalUsbIdentificationResult, Optional title As String = "Identificar USB")
            Dim certification = If(result.Status = UsbVerificationStatus.Success, "USB identificado e verificado", If(result.Status = UsbVerificationStatus.Inconclusive, "Verificacao inconclusiva", "Gravacao nao compativel com a imagem"))
            _statusLabel.Text = certification
            Dim lines = New List(Of String) From {
                $"Status: {certification}",
                $"Imagem provavel: {result.CatalogName}",
                $"Modo detectado: {result.ModeName}",
                $"Confianca: {result.Confidence}",
                $"Arquivos verificados: {result.FilesVerified:N0}",
                $"Arquivos verificados por tamanho: {result.SizeOnlyVerified:N0}",
                $"Ausentes: {result.MissingCount:N0}",
                $"Divergentes: {result.MismatchCount:N0}",
                $"Inacessiveis: {result.InaccessibleCount:N0}",
                $"Origem do manifesto: {result.ManifestSource}",
                $"Hash da imagem relacionada: {If(String.IsNullOrWhiteSpace(result.ImageSha256), "N/A", result.ImageSha256)}"
            }
            lines.AddRange(result.Details)
            MessageBox.Show(String.Join(Environment.NewLine, lines), title, MessageBoxButtons.OK, If(result.Status = UsbVerificationStatus.Success, MessageBoxIcon.Information, MessageBoxIcon.Warning))
        End Sub

        Private Sub ShowRawUsbIdentificationResult(result As RawUsbIdentificationResult, summary As String, Optional title As String = "Identificar USB")
            _statusLabel.Text = summary
            Dim lines = New List(Of String) From {
                "Status: USB identificado e verificado",
                $"Imagem provavel: {result.CatalogName}",
                "Modo detectado: RAW/DD",
                "Confianca: alta",
                $"Hash da imagem relacionada: {result.ImageSha256}"
            }
            lines.AddRange(result.Details)
            MessageBox.Show(String.Join(Environment.NewLine, lines), title, MessageBoxButtons.OK, MessageBoxIcon.Information)
        End Sub

        Private Shared Function TryReadPhysicalSector(diskNumber As Integer, byteOffset As Long) As Byte()
            If diskNumber < 0 Then Return Nothing
            Try
                Using stream As New FileStream($"\\.\PhysicalDrive{diskNumber}", FileMode.Open, FileAccess.Read, FileShare.ReadWrite)
                    stream.Seek(byteOffset, SeekOrigin.Begin)
                    Dim buffer(511) As Byte
                    Dim read = stream.Read(buffer, 0, buffer.Length)
                    If read = buffer.Length Then Return buffer
                End Using
            Catch
            End Try
            Return Nothing
        End Function

        Private Shared Function TryReadVolumeBootSector(driveLetter As String) As Byte()
            If String.IsNullOrWhiteSpace(driveLetter) Then Return Nothing
            Dim letter = driveLetter.TrimEnd("\"c).TrimEnd(":"c).Trim().ToUpperInvariant()
            If letter.Length <> 1 Then Return Nothing
            Try
                Using stream As New FileStream($"\\.\{letter}:", FileMode.Open, FileAccess.Read, FileShare.ReadWrite)
                    Dim buffer(511) As Byte
                    Dim read = stream.Read(buffer, 0, buffer.Length)
                    If read = buffer.Length Then Return buffer
                End Using
            Catch
            End Try
            Return Nothing
        End Function

        Private Sub ClearDriveRoot(rootPath As String)
            For Each filePath In IO.Directory.GetFiles(rootPath)
                IO.File.Delete(filePath)
            Next

            For Each dirPath In IO.Directory.GetDirectories(rootPath)
                IO.Directory.Delete(dirPath, True)
            Next
        End Sub

        ''' <summary>
        ''' Força a desmontagem de todos os volumes de um disco físico antes do diskpart.
        ''' 100% nativo — usa Win32 P/Invoke (mesma sequência do rpi-imager/diskpart_util.cpp):
        '''   1. SHChangeNotify — notifica o Explorer para soltar handles (antes de tudo)
        '''   2. FSCTL_LOCK_VOLUME — bloqueia o volume (até 10 × 50 ms)
        '''   3. FSCTL_DISMOUNT_VOLUME — desmonta o filesystem
        '''   4. FSCTL_UNLOCK_VOLUME — desbloqueia
        '''   5. DeleteVolumeMountPointW — remove a letra de unidade
        '''   6. SHChangeNotify — notificação final ao Explorer
        ''' Sem dependência de PowerShell, mountvol ou CMD.
        ''' </summary>
        ''' <summary>
        ''' Desmonta todos os volumes do disco via GUID de volume (FindFirstVolumeW/FindNextVolumeW),
        ''' independente de o volume ter uma letra de unidade atribuída ou não.
        ''' </summary>
        ''' <summary>
        ''' Depois da gravacao RAW/DD, tira a letra da particao EFI (tipo 0xEF no MBR) que o Windows montou. Inspirado no
        ''' Rufus, que depois de gravar imagem so remonta a primeira particao que nao e ESP. Com letra, a ESP fica exposta a
        ''' escritas do Windows ("System Volume Information"). Nao evita a primeira escrita: na bancada o Windows ja tinha
        ''' alterado 54 bytes do volume FAT antes da remocao (por isso a comparacao RAW tolera o volume FAT/EFI).
        ''' Nao impede que outro computador a monte.
        ''' Espera ate waitSeconds o Windows montar os volumes. Devolve quantas letras tirou.
        ''' </summary>
        Private Async Function RemoveEfiPartitionLettersAsync(diskNumber As Integer, Optional waitSeconds As Integer = 10) As Task(Of Integer)
            Const IOCTL_DISK_GET_PARTITION_INFO_EX As UInteger = &H70048UI
            Dim mbr = TryReadPhysicalSector(diskNumber, 0)
            Dim efiOffsets = New HashSet(Of Long)(MbrLayout.ReadPartitions(mbr).
                Where(Function(entry) entry.PartitionType = MbrLayout.EfiSystemPartitionType AndAlso entry.StartLba > 0UI).
                Select(Function(entry) entry.StartOffsetBytes))
            If efiOffsets.Count = 0 Then
                Await _logService.WriteAsync($"USB | RAW_DD | ESP_LETRA | Disk={diskNumber} | Imagem sem particao EFI no MBR")
                Return 0
            End If

            Dim removed = 0
            Dim deadline = Date.UtcNow.AddSeconds(waitSeconds)
            Do
                Dim sb As New System.Text.StringBuilder(260)
                Dim volumes As New List(Of String)()
                Dim hFind = NativeMethods.FindFirstVolumeW(sb, CUInt(sb.Capacity))
                If hFind <> New IntPtr(-1) AndAlso hFind <> IntPtr.Zero Then
                    Try
                        Do
                            volumes.Add(sb.ToString())
                        Loop While NativeMethods.FindNextVolumeW(hFind, sb, CUInt(sb.Capacity))
                    Finally
                        NativeMethods.FindVolumeClose(hFind)
                    End Try
                End If

                For Each guidWithSlash In volumes
                    Dim guidNoSlash = guidWithSlash.TrimEnd("\"c)
                    Dim startOffset As Long = -1
                    Try
                        Using hVolume = CreateFile(guidNoSlash, 0UI, FILE_SHARE_READ Or FILE_SHARE_WRITE Or FILE_SHARE_DELETE, IntPtr.Zero, FileMode.Open, 0, IntPtr.Zero)
                            If hVolume Is Nothing OrElse hVolume.IsInvalid Then Continue For
                            Dim devNum As New STORAGE_DEVICE_NUMBER()
                            Dim returned As Integer = 0
                            If Not DeviceIoControl(hVolume, IOCTL_STORAGE_GET_DEVICE_NUMBER, IntPtr.Zero, 0, devNum, Marshal.SizeOf(GetType(STORAGE_DEVICE_NUMBER)), returned, IntPtr.Zero) OrElse
                               devNum.DeviceType <> FILE_DEVICE_DISK OrElse devNum.DeviceNumber <> diskNumber Then Continue For
                            ' PARTITION_INFORMATION_EX: StartingOffset (LARGE_INTEGER) no offset 8.
                            Dim info(143) As Byte
                            Dim bytesRet As UInteger = 0
                            If NativeMethods.DeviceIoControl(hVolume, IOCTL_DISK_GET_PARTITION_INFO_EX, IntPtr.Zero, 0UI, info, CUInt(info.Length), bytesRet, IntPtr.Zero) AndAlso bytesRet >= 16UI Then
                                startOffset = BitConverter.ToInt64(info, 8)
                            End If
                        End Using
                    Catch
                        Continue For
                    End Try
                    If Not efiOffsets.Contains(startOffset) Then Continue For

                    Dim pathsSb As New System.Text.StringBuilder(1024)
                    Dim pathsLen As UInteger = 0
                    NativeMethods.GetVolumePathNamesForVolumeNameW(guidWithSlash, pathsSb, CUInt(pathsSb.Capacity), pathsLen)
                    For Each mountPath In pathsSb.ToString().Split(New Char() {ChrW(0)}, StringSplitOptions.RemoveEmptyEntries)
                        If mountPath.Length <> 3 OrElse mountPath(1) <> ":"c Then Continue For
                        Dim ok = NativeMethods.DeleteVolumeMountPointW(mountPath)
                        Await _logService.WriteAsync($"USB | RAW_DD | ESP_LETRA | Disk={diskNumber} | Removida={mountPath} | Offset={startOffset} | Ok={ok}")
                        If ok Then removed += 1
                    Next
                Next
                If removed > 0 OrElse Date.UtcNow >= deadline Then Exit Do
                Await Task.Delay(1000)
            Loop
            Await _logService.WriteAsync($"USB | RAW_DD | ESP_LETRA | Disk={diskNumber} | Removidas={removed}")
            Return removed
        End Function

        Private Async Function DismountDiskVolumesAsync(diskNumber As Integer, Optional keepLocked As Boolean = False) As Task
            GuardTargetDisk(diskNumber)
            Dim processedCount As Integer = 0
            Await _logService.WriteAsync($"D | DISMOUNT | INICIO | Disk {diskNumber}")

            ' Enumera TODOS os volumes do sistema (com ou sem letra) via FindFirstVolumeW
            Dim sb As New System.Text.StringBuilder(260)
            Dim hFind = NativeMethods.FindFirstVolumeW(sb, CUInt(sb.Capacity))
            Dim allGuids As New List(Of String)()
            If hFind <> New IntPtr(-1) AndAlso hFind <> IntPtr.Zero Then
                Try
                    Do
                        allGuids.Add(sb.ToString())
                    Loop While NativeMethods.FindNextVolumeW(hFind, sb, CUInt(sb.Capacity))
                Finally
                    NativeMethods.FindVolumeClose(hFind)
                End Try
            End If
            Await _logService.WriteAsync($"D | DISMOUNT | TOTAL_VOLUMES={allGuids.Count}")

            For Each guidWithSlash In allGuids
                ' Verificar se este volume pertence ao disco alvo
                Dim guidNoSlash = guidWithSlash.TrimEnd("\"c)
                Dim devMatches As Boolean = False
                Try
                    Using hTest = CreateFile(guidNoSlash, 0UI, FILE_SHARE_READ Or FILE_SHARE_WRITE Or FILE_SHARE_DELETE,
                            IntPtr.Zero, FileMode.Open, 0, IntPtr.Zero)
                        If Not (hTest Is Nothing OrElse hTest.IsInvalid) Then
                            Dim devNum As New STORAGE_DEVICE_NUMBER()
                            Dim returned As Integer = 0
                            Dim ok = DeviceIoControl(hTest, IOCTL_STORAGE_GET_DEVICE_NUMBER, IntPtr.Zero, 0,
                                devNum, Marshal.SizeOf(GetType(STORAGE_DEVICE_NUMBER)), returned, IntPtr.Zero)
                            devMatches = ok AndAlso devNum.DeviceType = FILE_DEVICE_DISK AndAlso devNum.DeviceNumber = diskNumber
                        End If
                    End Using
                Catch
                    ' Volume inacessível — pular
                End Try

                If Not devMatches Then Continue For

                ' Obter letras associadas ao GUID (pode ser vazio se sem letra)
                Dim pathsSb As New System.Text.StringBuilder(1024)
                Dim pathsLen As UInteger = 0
                NativeMethods.GetVolumePathNamesForVolumeNameW(guidWithSlash, pathsSb, CUInt(pathsSb.Capacity), pathsLen)
                Dim mountPaths = pathsSb.ToString().Split(New Char() {ChrW(0)}, StringSplitOptions.RemoveEmptyEntries)
                Dim lettersStr = If(mountPaths.Length = 0, "(sem letra)", String.Join(",", mountPaths))
                Await _logService.WriteAsync($"D | DISMOUNT | VOLUME_FOUND | Disk {diskNumber} | Letras=[{lettersStr}]")

                ' SHChangeNotify antes (somente para letras)
                For Each mPath In mountPaths
                    Dim dp = mPath.TrimEnd("\"c)
                    If dp.Length >= 1 Then
                        Dim drivePath = dp.Substring(0, 1).ToUpperInvariant() & ":\"
                        NativeMethods.SHChangeNotify(NativeMethods.SHCNE_MEDIAREMOVED, NativeMethods.SHCNF_PATH, drivePath, IntPtr.Zero)
                        NativeMethods.SHChangeNotify(NativeMethods.SHCNE_DRIVEREMOVED, NativeMethods.SHCNF_PATH, drivePath, IntPtr.Zero)
                    End If
                Next

                ' Abrir handle do volume pelo GUID para LOCK + DISMOUNT
                Dim hVol = NativeMethods.CreateFile(guidNoSlash,
                    NativeMethods.GENERIC_READ Or NativeMethods.GENERIC_WRITE,
                    NativeMethods.FILE_SHARE_READ Or NativeMethods.FILE_SHARE_WRITE,
                    IntPtr.Zero, NativeMethods.OPEN_EXISTING, 0UI, IntPtr.Zero)
                Dim guidShort = If(guidNoSlash.Length > 12, guidNoSlash.Substring(guidNoSlash.Length - 12), guidNoSlash)
                Await _logService.WriteAsync($"D | DISMOUNT | CREATEFILE | ...{guidShort} | Invalid={hVol.IsInvalid} | Err={If(hVol.IsInvalid, Marshal.GetLastWin32Error(), 0)}")

                If Not hVol.IsInvalid Then
                    Dim holdLock = False
                    Try
                        Dim bytesRet As UInteger = 0

                        ' FSCTL_LOCK_VOLUME — até 10 tentativas × 50 ms
                        Dim locked As Boolean = False
                        Dim lockErr As Integer = 0
                        For attempt = 1 To 10
                            If NativeMethods.DeviceIoControl(hVol, NativeMethods.FSCTL_LOCK_VOLUME, IntPtr.Zero, 0, IntPtr.Zero, 0, bytesRet, IntPtr.Zero) Then
                                locked = True
                                Await _logService.WriteAsync($"D | DISMOUNT | LOCK_OK | tentativa {attempt}/10")
                                Exit For
                            End If
                            lockErr = Marshal.GetLastWin32Error()
                            Await _logService.WriteAsync($"D | DISMOUNT | LOCK_WAIT | tentativa {attempt}/10 | Err={lockErr}")
                            Await Task.Delay(50)
                        Next
                        If Not locked Then
                            Await _logService.WriteAsync($"D | DISMOUNT | LOCK_FAILED | Err={lockErr}")
                        End If

                        ' FSCTL_DISMOUNT_VOLUME
                        Dim dismountOk = NativeMethods.DeviceIoControl(hVol, NativeMethods.FSCTL_DISMOUNT_VOLUME, IntPtr.Zero, 0, IntPtr.Zero, 0, bytesRet, IntPtr.Zero)
                        Await _logService.WriteAsync($"D | DISMOUNT | DISMOUNT_VOL={dismountOk} | Err={If(dismountOk, 0, Marshal.GetLastWin32Error())}")

                        If locked AndAlso keepLocked Then
                            ' P-06: o volume fica travado (e desmontado) ate ReleaseHeldVolumeLocks.
                            ' Assim o Windows nao o remonta no meio da gravacao RAW.
                            _heldVolumeLocks.Add(hVol)
                            holdLock = True
                            Await _logService.WriteAsync($"D | DISMOUNT | LOCK_MANTIDO | ...{guidShort}")
                        ElseIf locked Then
                            NativeMethods.DeviceIoControl(hVol, NativeMethods.FSCTL_UNLOCK_VOLUME, IntPtr.Zero, 0, IntPtr.Zero, 0, bytesRet, IntPtr.Zero)
                        End If
                    Finally
                        If Not holdLock Then
                            hVol.Dispose()
                        End If
                    End Try
                End If

                ' Remover pontos de montagem (letras)
                For Each mPath In mountPaths
                    Dim dp = mPath.TrimEnd("\"c)
                    If dp.Length >= 1 Then
                        Dim lSafe = dp.Substring(0, 1).ToUpperInvariant()
                        Dim drivePath = lSafe & ":\"
                        Dim removed = NativeMethods.DeleteVolumeMountPointW(drivePath)
                        Await _logService.WriteAsync(
                            If(removed,
                               $"USB | DISMOUNT | OK | Disk {diskNumber} | {lSafe}",
                               $"USB | DISMOUNT | LETRA_NAO_REMOVIDA | Disk {diskNumber} | {lSafe} | Err {Marshal.GetLastWin32Error()}"))
                        NativeMethods.SHChangeNotify(NativeMethods.SHCNE_MEDIAREMOVED, NativeMethods.SHCNF_PATH, drivePath, IntPtr.Zero)
                        NativeMethods.SHChangeNotify(NativeMethods.SHCNE_DRIVEREMOVED, NativeMethods.SHCNF_PATH, drivePath, IntPtr.Zero)
                    End If
                Next

                processedCount += 1
            Next

            Await _logService.WriteAsync(
                If(processedCount > 0,
                   $"USB | DISMOUNT | CONCLUIDO | Disk {diskNumber} | {processedCount} volume(s)",
                   $"USB | DISMOUNT | SEM_VOLUMES | Disk {diskNumber}"))

            Await Task.Delay(500)
        End Function

        ''' <summary>Libera os volumes mantidos travados por DismountDiskVolumesAsync(keepLocked:=True).</summary>
        Private Sub ReleaseHeldVolumeLocks()
            If _heldVolumeLocks.Count = 0 Then Return
            For Each lockHandle In _heldVolumeLocks
                Try
                    Dim bytesRet As UInteger = 0
                    NativeMethods.DeviceIoControl(lockHandle, NativeMethods.FSCTL_UNLOCK_VOLUME, IntPtr.Zero, 0, IntPtr.Zero, 0, bytesRet, IntPtr.Zero)
                Catch
                End Try
                lockHandle.Dispose()
            Next
            _logService.WriteAsync($"D | DISMOUNT | LOCKS_LIBERADOS | {_heldVolumeLocks.Count}").GetAwaiter().GetResult()
            _heldVolumeLocks.Clear()
        End Sub

        ' ─────────────────────────────────────────────────────────────────────────────
        '  FORMATAÇÃO NATIVA — sem diskpart.exe nem PowerShell
        '  Portada de rpi-imager/disk_formatter.cpp + diskpart_util.cpp
        ' ─────────────────────────────────────────────────────────────────────────────

        ''' <summary>
        ''' Limpa a tabela de partições do disco físico via IOCTL (cleanDiskFast do rpi-imager).
        ''' Equivalente a "select disk N + clean" do diskpart, mas sem processo externo.
        ''' </summary>
        Private Async Function CleanDriveNativeAsync(diskNumber As Integer) As Task
            GuardTargetDisk(diskNumber)
            Await _logService.WriteAsync($"USB | NATIVE_CLEAN | INICIO | Disk {diskNumber}")
            Dim devicePath = $"\\.\PhysicalDrive{diskNumber}"

            Dim hDisk = NativeMethods.CreateFile(
                devicePath,
                NativeMethods.GENERIC_READ Or NativeMethods.GENERIC_WRITE,
                NativeMethods.FILE_SHARE_READ Or NativeMethods.FILE_SHARE_WRITE,
                IntPtr.Zero,
                NativeMethods.OPEN_EXISTING,
                NativeMethods.FILE_FLAG_NO_BUFFERING Or NativeMethods.FILE_FLAG_WRITE_THROUGH,
                IntPtr.Zero)

            Await _logService.WriteAsync($"D | CLEAN | CREATEFILE | Disk {diskNumber} | Invalid={hDisk.IsInvalid} | Err={If(hDisk.IsInvalid, Marshal.GetLastWin32Error(), 0)}")

            If hDisk.IsInvalid Then
                Throw New IOException($"Não foi possível abrir \\.\PhysicalDrive{diskNumber} para limpeza. Erro Win32: {Marshal.GetLastWin32Error()}")
            End If

            Using hDisk
                Dim bytesRet As UInteger = 0

                Dim extIo = NativeMethods.DeviceIoControl(hDisk, NativeMethods.FSCTL_ALLOW_EXTENDED_DASD_IO, IntPtr.Zero, 0, IntPtr.Zero, 0, bytesRet, IntPtr.Zero)
                Await _logService.WriteAsync($"D | CLEAN | ALLOW_EXTENDED_IO={extIo} | Err={If(extIo, 0, Marshal.GetLastWin32Error())}")

                Dim deleteOk = NativeMethods.DeviceIoControl(hDisk, NativeMethods.IOCTL_DISK_DELETE_DRIVE_LAYOUT, IntPtr.Zero, 0, IntPtr.Zero, 0, bytesRet, IntPtr.Zero)
                Await _logService.WriteAsync($"D | CLEAN | DELETE_LAYOUT={deleteOk} | Err={If(deleteOk, 0, Marshal.GetLastWin32Error())}")

                If Not deleteOk Then
                    ' Fallback: zerar o inicio do disco. 4096 bytes cobrem 1 setor de disco 4Kn
                    ' ou 8 setores de 512 (MBR e cabecalho GPT), e respeitam o alinhamento do NO_BUFFERING.
                    Await _logService.WriteAsync($"USB | NATIVE_CLEAN | IOCTL_DELETE_fallback | Disk {diskNumber} | Err {Marshal.GetLastWin32Error()}")
                    Dim zeroes(4095) As Byte
                    Dim setOk = NativeMethods.SetFilePointerEx(hDisk, 0L, IntPtr.Zero, NativeMethods.FILE_BEGIN)
                    Dim setErr = If(setOk, 0, Marshal.GetLastWin32Error())
                    Await _logService.WriteAsync($"D | CLEAN | SEEK0={setOk} | Err={setErr}")
                    Dim written As UInteger = 0
                    Dim writeOk = setOk AndAlso NativeMethods.WriteFile(hDisk, zeroes, CUInt(zeroes.Length), written, IntPtr.Zero)
                    Dim writeErr = If(writeOk, 0, Marshal.GetLastWin32Error())
                    Await _logService.WriteAsync($"D | CLEAN | ZERO_MBR={writeOk} | Written={written} | Err={writeErr}")
                    ' P-04: sem layout removido e sem zeros gravados, o disco nao foi limpo. Nao seguir.
                    If Not writeOk OrElse written <> CUInt(zeroes.Length) Then
                        Throw New IOException($"Nao foi possivel limpar o Disk {diskNumber}: a tabela de particoes nao foi removida e a gravacao de zeros falhou (Win32 {If(setOk, writeErr, setErr)}).")
                    End If
                End If

                Dim updateOk = NativeMethods.DeviceIoControl(hDisk, NativeMethods.IOCTL_DISK_UPDATE_PROPERTIES, IntPtr.Zero, 0, IntPtr.Zero, 0, bytesRet, IntPtr.Zero)
                Await _logService.WriteAsync($"D | CLEAN | UPDATE_PROPS={updateOk} | Err={If(updateOk, 0, Marshal.GetLastWin32Error())}")
            End Using

            Await Task.Delay(200)
            Await _logService.WriteAsync($"USB | NATIVE_CLEAN | OK | Disk {diskNumber}")
        End Function

        ''' <summary>
        ''' Lê o Device Instance ID do disco via HKLM\SYSTEM\CurrentControlSet\Services\disk\Enum.
        ''' A chave N contém o instance ID do PhysicalDriveN (ex: "USBSTOR\DISK&VEN_SanDisk...").
        ''' </summary>
        Private Shared Function GetDiskDeviceInstanceId(diskNumber As Integer) As String
            Try
                Using key = Registry.LocalMachine.OpenSubKey("SYSTEM\CurrentControlSet\Services\disk\Enum")
                    If key Is Nothing Then Return String.Empty
                    Dim val = key.GetValue(diskNumber.ToString())
                    Return If(val Is Nothing, String.Empty, val.ToString())
                End Using
            Catch
                Return String.Empty
            End Try
        End Function

        ''' <summary>
        ''' Força re-enumeração PnP do disco via CM_Reenumerate_DevNode.
        ''' Usado como substituto de IOCTL_DISK_UPDATE_PROPERTIES em discos USB removíveis.
        ''' Faz o kernel reescanear a tabela de partições e remontar os volumes automaticamente.
        ''' </summary>
        Private Async Function ReenumerateDiskDeviceAsync(diskNumber As Integer) As Task
            Dim instanceId = GetDiskDeviceInstanceId(diskNumber)
            Await _logService.WriteAsync($"D | REENUMERATE | Disk {diskNumber} | InstanceId={instanceId}")
            If String.IsNullOrEmpty(instanceId) Then Return
            Dim devInst As UInteger = 0
            Dim cr = NativeMethods.CM_Locate_DevNodeW(devInst, instanceId, 0UI)
            Await _logService.WriteAsync($"D | REENUMERATE | CM_Locate={cr} | DevInst={devInst}")
            If cr = 0UI Then ' CR_SUCCESS = 0
                cr = NativeMethods.CM_Reenumerate_DevNode(devInst, 0UI)
                Await _logService.WriteAsync($"D | REENUMERATE | CM_Reenumerate={cr}")
            End If
        End Function

        ''' <summary>
        ''' Varre A-Z abrindo cada volume via CreateFile + IOCTL_STORAGE_GET_DEVICE_NUMBER
        ''' sem exigir DriveInfo.IsReady. Usado como fallback em WaitForVolumeMountAsync
        ''' para detectar letras atribuídas mas ainda não "prontas".
        ''' </summary>
        Private Function FindDriveLetterForDiskBruteForce(diskIndex As Integer) As String
            For code As Integer = AscW("A"c) To AscW("Z"c)
                Dim letter = ChrW(code)
                Dim volumePath = "\\.\\" & letter & ":"
                Try
                    Using handle = CreateFile(volumePath, 0UI, FILE_SHARE_READ Or FILE_SHARE_WRITE Or FILE_SHARE_DELETE, IntPtr.Zero, FileMode.Open, 0, IntPtr.Zero)
                        If handle Is Nothing OrElse handle.IsInvalid Then Continue For
                        Dim devNumber As New STORAGE_DEVICE_NUMBER()
                        Dim bytesReturned As Integer = 0
                        Dim ok = DeviceIoControl(handle, IOCTL_STORAGE_GET_DEVICE_NUMBER, IntPtr.Zero, 0, devNumber, Marshal.SizeOf(GetType(STORAGE_DEVICE_NUMBER)), bytesReturned, IntPtr.Zero)
                        If ok AndAlso devNumber.DeviceType = FILE_DEVICE_DISK AndAlso devNumber.DeviceNumber = diskIndex Then
                            Return letter.ToString()
                        End If
                    End Using
                Catch
                    ' Letra inacessível — continua
                End Try
            Next
            Return String.Empty
        End Function

        ''' <summary>
        ''' Aguarda a montagem automática de um novo volume no disco após formatação nativa.
        ''' Usa dois métodos: DriveInfo (só drives prontos) e varredura A-Z (inclui não-prontos).
        ''' Retorna a letra de unidade (ex: "E") ou String.Empty se o timeout expirar.
        ''' </summary>
        Private Async Function WaitForVolumeMountAsync(diskNumber As Integer, timeoutMs As Integer) As Task(Of String)
            Await _logService.WriteAsync($"USB | WAIT_MOUNT | INICIO | Disk {diskNumber} | Timeout {timeoutMs} ms")
            Const PollMs As Integer = 500
            Dim elapsed As Integer = 0
            Dim pollCount As Integer = 0
            While elapsed < timeoutMs
                pollCount += 1

                ' LOG: todos os drives visíveis (incluindo não-prontos)
                Dim allDrives = DriveInfo.GetDrives()
                Dim drivesSummary = String.Join(",", allDrives.Select(Function(d) $"{d.Name.TrimEnd("\").TrimEnd(":")}({If(d.IsReady, "R", "NR")})"))
                Await _logService.WriteAsync($"D | WAIT_MOUNT | Poll {pollCount} | {elapsed}ms | SysDrives=[{drivesSummary}]")

                ' Estratégia 1: DriveInfo (apenas drives IsReady=True)
                Dim letter1 = GetLogicalDriveLetterFromDiskNumberViaVolume(diskNumber)

                ' Estratégia 2: varredura A-Z (inclui drives não-prontos)
                Dim letter2 = String.Empty
                If String.IsNullOrWhiteSpace(letter1) Then
                    letter2 = FindDriveLetterForDiskBruteForce(diskNumber)
                End If

                Await _logService.WriteAsync($"D | WAIT_MOUNT | Poll {pollCount} | Ready={If(String.IsNullOrWhiteSpace(letter1), "(none)", letter1)} | BruteForce={If(String.IsNullOrWhiteSpace(letter2), "(none)", letter2)}")

                Dim found = If(Not String.IsNullOrWhiteSpace(letter1), letter1, letter2)
                If Not String.IsNullOrWhiteSpace(found) Then
                    Dim clean = found.TrimEnd("\"c).TrimEnd(":"c).Trim().ToUpperInvariant()
                    Await _logService.WriteAsync($"USB | WAIT_MOUNT | OK | Disk {diskNumber} | Letter {clean} | Polls {pollCount}")
                    Return clean
                End If

                Await Task.Delay(PollMs)
                elapsed += PollMs
            End While
            Await _logService.WriteAsync($"USB | WAIT_MOUNT | TIMEOUT | Disk {diskNumber} | Polls {pollCount}")
            Return String.Empty
        End Function

        ''' <summary>
        ''' Obtém o tamanho total do disco físico via IOCTL_DISK_GET_DRIVE_GEOMETRY_EX.
        ''' </summary>
        Private Shared Function GetDiskSizeBytes(diskNumber As Integer) As Long
            Dim devicePath = $"\\.\PhysicalDrive{diskNumber}"
            Dim hDisk = NativeMethods.CreateFile(
                devicePath,
                NativeMethods.GENERIC_READ,
                NativeMethods.FILE_SHARE_READ Or NativeMethods.FILE_SHARE_WRITE,
                IntPtr.Zero,
                NativeMethods.OPEN_EXISTING,
                0UI,
                IntPtr.Zero)
            If hDisk.IsInvalid Then Return 0

            Using hDisk
                Dim buf(255) As Byte
                Dim bytesRet As UInteger = 0
                Dim ok = NativeMethods.DeviceIoControl(hDisk, NativeMethods.IOCTL_DISK_GET_DRIVE_GEOMETRY_EX,
                    IntPtr.Zero, 0UI, buf, CUInt(buf.Length), bytesRet, IntPtr.Zero)
                If Not ok OrElse bytesRet < 32 Then Return 0
                ' DiskSize (LARGE_INTEGER=8 bytes) começa no offset 24 (após DISK_GEOMETRY de 24 bytes)
                Return BitConverter.ToInt64(buf, 24)
            End Using
        End Function

        ''' <summary>
        ''' Posiciona o ponteiro e escreve data no dispositivo físico (sectorOffset × 512).
        ''' Bloqueante — deve ser chamado via Task.Run.
        ''' </summary>
        Private Shared Sub WriteRawSectors(hDisk As SafeFileHandle, sectorOffset As Long, data As Byte())
            NativeMethods.SetFilePointerEx(hDisk, sectorOffset * 512L, IntPtr.Zero, NativeMethods.FILE_BEGIN)
            Dim totalWritten As Integer = 0
            While totalWritten < data.Length
                Dim chunk = Math.Min(data.Length - totalWritten, 1024 * 1024)
                Dim slice(chunk - 1) As Byte
                Array.Copy(data, totalWritten, slice, 0, chunk)
                Dim written As UInteger = 0
                If Not NativeMethods.WriteFile(hDisk, slice, CUInt(slice.Length), written, IntPtr.Zero) Then
                    Throw New IOException($"WriteFile falhou no setor {sectorOffset}. Erro Win32: {Marshal.GetLastWin32Error()}")
                End If
                totalWritten += CInt(written)
            End While
        End Sub

        ''' <summary>
        ''' Formata o disco físico como FAT32 diretamente via Win32 (sem diskpart).
        ''' Portado de DiskFormatter::FormatDrive do rpi-imager/disk_formatter.cpp.
        ''' </summary>
        Private Async Function FormatFat32NativeAsync(diskNumber As Integer, driveSizeBytes As Long, label As String) As Task
            GuardTargetDisk(diskNumber)
            Await _logService.WriteAsync($"USB | NATIVE_FAT32 | INICIO | Disk {diskNumber} | Size {driveSizeBytes}")
            Dim realSize = GetDiskSizeBytes(diskNumber)
            Await _logService.WriteAsync($"D | FAT32 | DISK_SIZE | realSize={realSize} | paramSize={driveSizeBytes}")
            If realSize <= 0 Then realSize = driveSizeBytes

            ' P-05: MBR com setores de 512 bytes enderecam no maximo 2 TiB (Fat32Layout.Plan recusa acima disso).
            Dim geometry = Fat32Layout.Plan(realSize)
            Await _logService.WriteAsync($"D | FAT32 | GEOMETRY | TotalSectors={geometry.TotalDiskSectors} | PartSectors={geometry.PartitionSizeSectors} | SPC={geometry.SectorsPerCluster} | SPF={geometry.SectorsPerFat} | Clusters={geometry.TotalClusters} | RootCluster={geometry.RootCluster}")

            Dim volumeId = BitConverter.ToUInt32(Guid.NewGuid().ToByteArray(), 0)

            Dim devicePath = $"\\.\PhysicalDrive{diskNumber}"
            Dim hDisk = NativeMethods.CreateFile(
                devicePath,
                NativeMethods.GENERIC_READ Or NativeMethods.GENERIC_WRITE,
                NativeMethods.FILE_SHARE_READ Or NativeMethods.FILE_SHARE_WRITE,
                IntPtr.Zero,
                NativeMethods.OPEN_EXISTING,
                NativeMethods.FILE_FLAG_NO_BUFFERING Or NativeMethods.FILE_FLAG_WRITE_THROUGH,
                IntPtr.Zero)

            Await _logService.WriteAsync($"D | FAT32 | CREATEFILE | Disk {diskNumber} | Invalid={hDisk.IsInvalid} | Err={If(hDisk.IsInvalid, Marshal.GetLastWin32Error(), 0)}")

            If hDisk.IsInvalid Then
                Throw New IOException($"Não foi possível abrir \\.\PhysicalDrive{diskNumber} para formatação FAT32. Erro Win32: {Marshal.GetLastWin32Error()}")
            End If

            ' Nota: FSCTL_ALLOW_EXTENDED_DASD_IO é para handles de VOLUME (\\.\E:), não para
            ' handles de disco físico (\\.\PhysicalDriveN) — ignorado intencionalmente aqui.

            ' IMPORTANTE: O MBR (setor 0) é gravado POR ÚLTIMO para evitar race condition.
            ' Se fosse gravado primeiro, o Windows detectaria a partição FAT32 durante a
            ' gravação das FATs (~2s para 30 MB) e montaria o volume, bloqueando as escritas
            ' seguintes com ERROR_ACCESS_DENIED (Win32 5).

            Using hDisk
                Dim bytesRet As UInteger = 0

                ' Boot Sector — primeiro setor da particao; FSInfo e copia do boot em seguida
                Dim bootStart = CLng(geometry.PartitionStartSector)
                Dim bootSector = Fat32Layout.BuildBootSector(geometry, volumeId, label)
                Await _logService.WriteAsync($"D | FAT32 | WRITE_BOOT | sector {bootStart} | 512 bytes")
                Await Task.Run(Sub() WriteRawSectors(hDisk, bootStart, bootSector))

                Await _logService.WriteAsync($"D | FAT32 | WRITE_FSINFO | sector {bootStart + Fat32Layout.FsInfoSector}")
                Await Task.Run(Sub() WriteRawSectors(hDisk, bootStart + Fat32Layout.FsInfoSector, Fat32Layout.BuildFsInfo(geometry.TotalClusters)))

                Await _logService.WriteAsync($"D | FAT32 | WRITE_BOOT_BKP | sector {bootStart + Fat32Layout.BackupBootSector}")
                Await Task.Run(Sub() WriteRawSectors(hDisk, bootStart + Fat32Layout.BackupBootSector, bootSector))

                Dim fatTable = Fat32Layout.BuildFatTable(geometry.SectorsPerFat)
                Await _logService.WriteAsync($"D | FAT32 | WRITE_FAT1 | sector {geometry.Fat1StartSector} | {fatTable.Length} bytes")
                Await Task.Run(Sub() WriteRawSectors(hDisk, geometry.Fat1StartSector, fatTable))
                Await _logService.WriteAsync($"D | FAT32 | WRITE_FAT2 | sector {geometry.Fat2StartSector} | {fatTable.Length} bytes")
                Await Task.Run(Sub() WriteRawSectors(hDisk, geometry.Fat2StartSector, fatTable))

                Dim rootDir = Fat32Layout.BuildRootDirectory(geometry, label)
                Await _logService.WriteAsync($"D | FAT32 | WRITE_ROOTDIR | sector {geometry.RootDirStartSector} | {rootDir.Length} bytes")
                Await Task.Run(Sub() WriteRawSectors(hDisk, geometry.RootDirStartSector, rootDir))

                ' MBR (setor 0) gravado POR ÚLTIMO — o Windows só detecta a partição após esta escrita
                Await _logService.WriteAsync($"D | FAT32 | WRITE_MBR | sector 0 | 512 bytes (ultimo)")
                Await Task.Run(Sub() WriteRawSectors(hDisk, 0, Fat32Layout.BuildMbrSector(geometry.TotalDiskSectors)))

                Dim updateOk = NativeMethods.DeviceIoControl(hDisk, NativeMethods.IOCTL_DISK_UPDATE_PROPERTIES, IntPtr.Zero, 0, IntPtr.Zero, 0, bytesRet, IntPtr.Zero)
                Await _logService.WriteAsync($"D | FAT32 | UPDATE_PROPS_INSIDE={updateOk} | Err={If(updateOk, 0, Marshal.GetLastWin32Error())}")
            End Using

            ' Reforco ao IOCTL_DISK_UPDATE_PROPERTIES acima.
            ' Usar CM_Reenumerate_DevNode para forçar o PnP a re-escanear a tabela de partições
            ' e remontar o volume sem precisar reconectar o dispositivo.
            Await Task.Delay(300)
            Await ReenumerateDiskDeviceAsync(diskNumber)

            Await _logService.WriteAsync($"USB | NATIVE_FAT32 | OK | Disk {diskNumber} | Clusters {geometry.TotalClusters} | SPF {geometry.SectorsPerFat} | SPC {geometry.SectorsPerCluster}")
        End Function

        ''' <summary>
        ''' Formata o USB como FAT32 para escrita de ISO de boot.
        ''' 100% nativo — sem diskpart.exe, sem PowerShell.
        ''' Sequência: SetThreadErrorMode → DismountAll → CleanNative → FormatFAT32Native
        '''            → WaitVolumeMount → VerifyIntegrity.
        ''' </summary>
        Private Async Function RunDiskpartCleanAndFormatAsync(diskNumber As Integer, driveLetter As String, driveSizeBytes As Long, label As String, Optional cancellationToken As CancellationToken = Nothing) As Task(Of String)
            GuardTargetDisk(diskNumber)
            Dim oldErrorMode As UInteger = 0
            NativeMethods.SetThreadErrorMode(NativeMethods.SEM_FAILCRITICALERRORS Or NativeMethods.SEM_NOOPENFILEERRORBOX, oldErrorMode)
            Try
                Await _logService.WriteAsync($"USB | NATIVE_FORMAT | INICIO | Disk {diskNumber} | Size {driveSizeBytes}")

                Await _logService.WriteAsync($"D | PAUSE | ANTES_DISMOUNT | aguardando 10s")
                Await Task.Delay(10_000, cancellationToken)

                Await DismountDiskVolumesAsync(diskNumber)

                Await _logService.WriteAsync($"D | PAUSE | APOS_DISMOUNT | aguardando 10s")
                Await Task.Delay(10_000, cancellationToken)

                Await CleanDriveNativeAsync(diskNumber)

                Await _logService.WriteAsync($"D | PAUSE | APOS_CLEAN | aguardando 10s")
                Await Task.Delay(10_000, cancellationToken)

                Await FormatFat32NativeAsync(diskNumber, driveSizeBytes, label)

                Await _logService.WriteAsync($"D | PAUSE | APOS_FAT32 | aguardando 10s")
                Await Task.Delay(10_000, cancellationToken)

                ' Aguardar o Windows montar o volume automaticamente (até 20 s)
                Dim mountedLetter = Await WaitForVolumeMountAsync(diskNumber, 20_000)
                If String.IsNullOrWhiteSpace(mountedLetter) Then
                    Dim requestedLetter = If(driveLetter, String.Empty).Trim().TrimEnd("\"c).TrimEnd(":"c).ToUpperInvariant()
                    If String.IsNullOrWhiteSpace(requestedLetter) Then
                        requestedLetter = GetAvailableDriveLetter().TrimEnd(":"c).ToUpperInvariant()
                    End If
                    If String.IsNullOrWhiteSpace(requestedLetter) Then
                        Throw New IOException($"O volume do Disk {diskNumber} não foi montado após a formatação FAT32 e não há letras disponíveis para atribuição manual.")
                    End If

                    Dim assignError As String = String.Empty
                    Dim assignFailed As Boolean = False
                    Try
                        Await _logService.WriteAsync($"USB | NATIVE_FORMAT | ASSIGN_FALLBACK | Disk {diskNumber} | Letter {requestedLetter}")
                        Dim assignScript = String.Join(Environment.NewLine, New String() {
                            $"select disk {diskNumber}",
                            "online disk noerr",
                            "rescan",
                            "select partition 1",
                            $"assign letter={requestedLetter}",
                            "exit"
                        }) & Environment.NewLine
                        ' O script seleciona o disco pelo numero: confere de novo que e o mesmo aparelho.
                        GuardTargetDisk(diskNumber)
                        Dim assignOutput = Await RunDiskpartScriptAsync(assignScript, cancellationToken)
                        Await _logService.WriteAsync($"USB | NATIVE_FORMAT | ASSIGN_FALLBACK_OK | Disk {diskNumber} | {TrimDiskpartOutput(assignOutput)}")
                    Catch ex As Exception When Not (TypeOf ex Is OperationCanceledException OrElse TypeOf ex Is DiskSafetyException)
                        assignError = ex.Message
                        assignFailed = True
                    End Try
                    If assignFailed Then
                        Await _logService.WriteAsync($"USB | NATIVE_FORMAT | ASSIGN_FALLBACK_ERRO | Disk {diskNumber} | {assignError}")
                    End If

                    mountedLetter = Await WaitForVolumeMountAsync(diskNumber, 10_000)
                    If String.IsNullOrWhiteSpace(mountedLetter) Then
                        mountedLetter = GetLogicalDriveLetterFromDiskIndex(diskNumber)
                    End If
                    If String.IsNullOrWhiteSpace(mountedLetter) Then
                        Dim suffix = If(String.IsNullOrWhiteSpace(assignError), String.Empty, $" Detalhe da atribuição manual: {assignError}")
                        Throw New IOException($"O volume do Disk {diskNumber} não foi montado após a formatação FAT32 (timeout 20 s) nem após atribuir a letra {requestedLetter}.{suffix}")
                    End If
                End If

                Await VerifyUsbWriteIntegrityAsync(mountedLetter)
                Await _logService.WriteAsync($"USB | NATIVE_FORMAT | OK | Disk {diskNumber} | Letter {mountedLetter}")
                Return mountedLetter
            Finally
                NativeMethods.SetThreadErrorMode(oldErrorMode, oldErrorMode)
            End Try
        End Function

        ''' <summary>
        ''' Traduz o output do diskpart para uma mensagem de erro amigável.
        ''' Usado por RunDiskpartScriptAsync para explicar falhas do diskpart.
        ''' </summary>
        Private Shared Function GetDiskpartErrorMessage(diskpartOutput As String, exitCode As Integer) As String
            Dim lower = If(diskpartOutput, String.Empty).ToLowerInvariant()
            If lower.Contains("write protected") OrElse lower.Contains("protegido contra grava") Then
                Return "O disco está protegido contra gravação. Verifique a chave de bloqueio do pen drive."
            End If
            If lower.Contains("not enough space") OrElse lower.Contains("espa") AndAlso lower.Contains("insuficiente") OrElse
               lower.Contains("volume") AndAlso lower.Contains("muito grande") OrElse lower.Contains("too large") Then
                Return "Espaço insuficiente ou tamanho de volume incompatível. Tente com outro pen drive."
            End If
            If lower.Contains("access is denied") OrElse lower.Contains("acesso negado") Then
                Return "Acesso negado pelo Windows ao limpar ou formatar o USB. Feche janelas abertas do pendrive, remova bloqueio somente leitura e tente novamente."
            End If
            If lower.Contains("device is not ready") OrElse lower.Contains("dispositivo n") AndAlso lower.Contains("pronto") Then
                Return "O dispositivo USB não está pronto. Reconecte o pen drive e tente novamente."
            End If
            If lower.Contains("incorrect function") OrElse lower.Contains("fun") AndAlso lower.Contains("incorreta") Then
                Return "Função inválida. Pode indicar conflito de acesso ou incompatibilidade de driver."
            End If
            If lower.Contains("no media") OrElse lower.Contains("sem m") AndAlso lower.Contains("dia") Then
                Return "Nenhuma mídia detectada. Verifique se o pen drive está corretamente inserido."
            End If
            If String.IsNullOrWhiteSpace(diskpartOutput) Then
                Return $"Falha na formatação do disco (código {exitCode})."
            End If
            Dim trimmed = diskpartOutput.Trim()
            If trimmed.Length > 300 Then trimmed = trimmed.Substring(0, 300) & "..."
            Return $"Erro do diskpart: {trimmed}"
        End Function

        Private Shared Function SelectZeroUsbFileSystem(sizeBytes As Long, Optional requireLargeFileSupport As Boolean = False) As String
            Const Fat32LimitBytes As Long = 32L * 1024L * 1024L * 1024L
            If requireLargeFileSupport Then
                Return "exFAT"
            End If
            If sizeBytes > 0 AndAlso sizeBytes <= Fat32LimitBytes Then
                Return "FAT32"
            End If
            Return "exFAT"
        End Function

        Private Async Function ZeroUsbAsync(drive As UsbDriveOption, label As String, cancellationToken As CancellationToken, Optional requireLargeFileSupport As Boolean = False, Optional fileSystemOverride As String = Nothing) As Task(Of String)
            GuardTargetDisk(If(drive Is Nothing, -1, drive.DiskNumber))
            If drive Is Nothing Then
                Throw New ArgumentNullException(NameOf(drive))
            End If
            If drive.DiskNumber < 0 Then
                Throw New InvalidOperationException("Disco USB invalido para zerar.")
            End If

            cancellationToken.ThrowIfCancellationRequested()
            Dim sizeBytes = If(drive.SizeBytes > 0, drive.SizeBytes, GetDiskSizeBytes(drive.DiskNumber))
            Dim fileSystem = If(String.IsNullOrWhiteSpace(fileSystemOverride), SelectZeroUsbFileSystem(sizeBytes, requireLargeFileSupport), fileSystemOverride)
            Dim labelSafe = NormalizeVolumeLabel(label)

            Await DismountDiskVolumesAsync(drive.DiskNumber)
            cancellationToken.ThrowIfCancellationRequested()

            Await _logService.WriteAsync($"USB | ZERO | DISKPART | Disk={drive.DiskNumber} | Size={sizeBytes} | FS={fileSystem} | Label={labelSafe}")
            Dim script = DiskpartScripts.ZeroUsb(drive.DiskNumber, fileSystem, labelSafe)

            Dim output = Await RunDiskpartScriptAsync(script, cancellationToken)
            Await _logService.WriteAsync($"USB | ZERO | DISKPART_OK | Disk={drive.DiskNumber} | {TrimDiskpartOutput(output)}")

            Dim mountedLetter = Await WaitForVolumeMountAsync(drive.DiskNumber, 20_000)
            If String.IsNullOrWhiteSpace(mountedLetter) Then
                mountedLetter = GetLogicalDriveLetterFromDiskIndex(drive.DiskNumber)
            End If
            If String.IsNullOrWhiteSpace(mountedLetter) Then
                Throw New IOException($"O volume do Disk {drive.DiskNumber} nao foi montado apos zerar o USB.")
            End If

            Await VerifyUsbWriteIntegrityAsync(mountedLetter)
            Return mountedLetter.TrimEnd("\"c).TrimEnd(":"c).Trim().ToUpperInvariant()
        End Function

        Private Shared Function NormalizeVolumeLabel(label As String) As String
            Dim value = If(label, "QUALLIT_USB").Trim()
            If value.Length = 0 Then value = "QUALLIT_USB"
            Dim invalid = Path.GetInvalidFileNameChars()
            For Each ch In invalid
                value = value.Replace(ch, "_"c)
            Next
            value = value.Replace(" "c, "_"c)
            If value.Length > 11 Then value = value.Substring(0, 11)
            Return value
        End Function

        Private Async Function RunDiskpartScriptAsync(script As String, cancellationToken As CancellationToken) As Task(Of String)
            Dim scriptPath = Path.Combine(Path.GetTempPath(), $"quallit_zero_usb_{Guid.NewGuid():N}.txt")
            Await File.WriteAllTextAsync(scriptPath, script, cancellationToken)
            Try
                Using process As New Process()
                    process.StartInfo = New ProcessStartInfo("diskpart.exe", $"/s ""{scriptPath}""") With {
                        .UseShellExecute = False,
                        .RedirectStandardOutput = True,
                        .RedirectStandardError = True,
                        .StandardOutputEncoding = ConsoleOemEncoding,
                        .StandardErrorEncoding = ConsoleOemEncoding,
                        .CreateNoWindow = True
                    }
                    process.Start()
                    Dim stdoutTask = process.StandardOutput.ReadToEndAsync()
                    Dim stderrTask = process.StandardError.ReadToEndAsync()
                    Try
                        Await process.WaitForExitAsync(cancellationToken)
                    Catch ex As OperationCanceledException
                        Try
                            If Not process.HasExited Then
                                process.Kill(entireProcessTree:=True)
                            End If
                        Catch
                        End Try
                        Throw
                    End Try
                    Dim output = Await stdoutTask
                    Dim errorOutput = Await stderrTask
                    Dim combined = (output & Environment.NewLine & errorOutput).Trim()
                    If process.ExitCode <> 0 Then
                        Throw New IOException(GetDiskpartErrorMessage(combined, process.ExitCode))
                    End If
                    Return combined
                End Using
            Finally
                Try
                    If File.Exists(scriptPath) Then File.Delete(scriptPath)
                Catch
                End Try
            End Try
        End Function

        ' Ferramentas de console do Windows (diskpart, bootsect, robocopy) escrevem na pagina de codigo OEM (850 em
        ' pt-BR); lida com a codificacao padrao, a saida ia para o log com acentos quebrados ("versÆo", "parti‡Æo").
        Private Shared ReadOnly ConsoleOemEncoding As System.Text.Encoding = CreateConsoleOemEncoding()

        Private Shared Function CreateConsoleOemEncoding() As System.Text.Encoding
            Try
                System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance)
                Return System.Text.Encoding.GetEncoding(Globalization.CultureInfo.CurrentCulture.TextInfo.OEMCodePage)
            Catch
                Return System.Text.Encoding.Default
            End Try
        End Function

        Private Shared Function TrimDiskpartOutput(output As String) As String
            Dim value = If(output, String.Empty).Replace(Environment.NewLine, " | ").Trim()
            If value.Length > 500 Then value = value.Substring(0, 500) & "..."
            Return value
        End Function

        ''' <summary>
        ''' Escreve 1 MB de padrão aleatório e relê para verificar integridade básica.
        ''' Detecta pen drives com defeito ou capacidade falsa (padrão rpi-imager).
        ''' </summary>
        Private Async Function VerifyUsbWriteIntegrityAsync(driveLetter As String) As Task
            ' Normalizar: aceitar "E", "E:" ou "E:\" — usar sempre letra pura
            Dim cleanLetter = driveLetter.TrimEnd("\"c).TrimEnd(":"c).Trim().ToUpperInvariant()
            Dim driveRoot = $"{cleanLetter}:\"
            Dim testPath = Path.Combine(driveRoot, "quallit_verify.tmp")
            Dim verifyTimedOut As Boolean = False
            Try
                ' Aguardar o volume ficar acessível após atribuição de letra pelo diskpart.
                ' O SO pode levar alguns segundos para montar o sistema de arquivos recém-criado.
                ' Usa DriveInfo.IsReady (mais confiável que Directory.Exists).
                Const VolumeReadyTimeoutMs As Integer = 15_000
                Const VolumeReadyPollMs As Integer = 500
                Dim waited As Integer = 0
                Dim volumeReady As Boolean = False
                While waited < VolumeReadyTimeoutMs
                    Try
                        Dim driveInfo As New DriveInfo(cleanLetter)
                        If driveInfo.IsReady Then
                            volumeReady = True
                            Exit While
                        End If
                    Catch
                    End Try
                    Await Task.Delay(VolumeReadyPollMs)
                    waited += VolumeReadyPollMs
                End While
                If Not volumeReady Then
                    Throw New IOException($"O volume {cleanLetter}: não ficou acessível após a formatação ({VolumeReadyTimeoutMs \ 1000} s).")
                End If
                ' Padrão pseudoaleatório de 1 MB baseado em GUID (sem dependência de crypto)
                Dim seed = Guid.NewGuid().ToByteArray()
                Dim pattern(1024 * 1024 - 1) As Byte
                For i = 0 To pattern.Length - 1
                    pattern(i) = CByte((seed(i Mod 16) Xor CByte(i And &HFF)) And &HFF)
                Next

                ' Retry na escrita: DriveInfo.IsReady pode retornar True antes do filesystem
                ' aceitar I/O (race condition em volumes recém-formatados).
                ' Tenta até 10x com intervalo de 1 s antes de desistir.
                Const WriteMaxRetries As Integer = 10
                Const WriteRetryDelayMs As Integer = 1000
                Dim lastWriteEx As Exception = Nothing
                Dim writeOk As Boolean = False
                Dim writeRetryLog As String = Nothing
                For writeAttempt = 1 To WriteMaxRetries
                    Try
                        Using cts = New Threading.CancellationTokenSource(TimeSpan.FromSeconds(30))
                            Await File.WriteAllBytesAsync(testPath, pattern, cts.Token)
                            Dim readBack = Await File.ReadAllBytesAsync(testPath, cts.Token)
                            If Not pattern.SequenceEqual(readBack) Then
                                Await _logService.WriteAsync($"USB | VERIFY | MISMATCH | Drive {cleanLetter}: dados relidos diferem dos gravados")
                                Throw New InvalidOperationException(
                                    "Verificação de integridade falhou: os dados relidos diferem dos gravados." & Environment.NewLine &
                                    "O pen drive pode estar com defeito ou ser de capacidade falsa.")
                            End If
                        End Using
                        writeOk = True
                        Exit For
                    Catch ex As InvalidOperationException
                        Throw  ' mismatch — não adianta tentar de novo
                    Catch ex As Exception
                        lastWriteEx = ex
                        writeRetryLog = $"USB | VERIFY | WRITE_RETRY | {writeAttempt}/{WriteMaxRetries} | {ex.Message}"
                    End Try
                    ' Await não é permitido em bloco Catch — executa fora do Try
                    If writeRetryLog IsNot Nothing Then
                        Await _logService.WriteAsync(writeRetryLog)
                        writeRetryLog = Nothing
                    End If
                    Await Task.Delay(WriteRetryDelayMs)
                Next
                If Not writeOk Then
                    Throw New IOException($"Não foi possível escrever no volume {cleanLetter}: após {WriteMaxRetries} tentativas. {lastWriteEx?.Message}")
                End If
                Await _logService.WriteAsync($"USB | VERIFY | OK | Drive {cleanLetter}")
            Catch ex As OperationCanceledException
                verifyTimedOut = True
            Finally
                Try
                    If File.Exists(testPath) Then File.Delete(testPath)
                Catch
                End Try
            End Try
            If verifyTimedOut Then
                Await _logService.WriteAsync($"USB | VERIFY | TIMEOUT | Drive {cleanLetter}")
                Throw New TimeoutException("Timeout na verificação de integridade. O pen drive pode ser muito lento ou estar com defeito.")
            End If
        End Function

        ' Localiza pwsh 7 se disponível; fallback para powershell 5.1.
        Private Shared Function GetPowerShellExe() As String
            Dim candidates As String() = {
                "C:\Program Files\PowerShell\7\pwsh.exe",
                "C:\Program Files\WindowsApps\Microsoft.PowerShell_7.6.0.0_x64__8wekyb3d8bbwe\pwsh.exe"
            }
            For Each p In candidates
                If IO.File.Exists(p) Then Return p
            Next
            Return "powershell.exe"
        End Function

        Private Function GetDriveLetterSet() As HashSet(Of String)
            Dim letters As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
            For Each drive In DriveInfo.GetDrives()
                If drive.Name.Length >= 2 Then
                    letters.Add(drive.Name.Substring(0, 2))
                End If
            Next
            Return letters
        End Function

        Private Function ResolveNewDriveLetter(beforeSet As HashSet(Of String), usbLetter As String, initialLetter As String, timeoutSeconds As Integer) As String
            If Not String.IsNullOrWhiteSpace(initialLetter) Then
                Dim normalized = NormalizeDriveLetterValue(initialLetter)
                If Not String.Equals(normalized, usbLetter, StringComparison.OrdinalIgnoreCase) Then
                    Return normalized
                End If
            End If

            Dim attempts = Math.Max(1, timeoutSeconds * 2)
            For i = 0 To attempts - 1
                Dim current = GetDriveLetterSet()
                For Each letter In current
                    If Not beforeSet.Contains(letter) AndAlso Not String.Equals(letter, usbLetter, StringComparison.OrdinalIgnoreCase) Then
                        Return letter
                    End If
                Next
                Threading.Thread.Sleep(500)
            Next

            Return String.Empty
        End Function

        Private Function NormalizeDriveLetterValue(letter As String) As String
            If String.IsNullOrWhiteSpace(letter) Then
                Return String.Empty
            End If
            Dim trimmed = letter.Trim()
            If trimmed.Length = 1 Then
                Return trimmed & ":"
            End If
            If trimmed.Length = 2 AndAlso trimmed.EndsWith(":", StringComparison.Ordinal) Then
                Return trimmed
            End If
            If trimmed.Length > 2 AndAlso trimmed.Contains(":") Then
                Return trimmed.Substring(0, 2)
            End If
            Return trimmed
        End Function

        ''' <summary>
        ''' Copia todos os arquivos de uma ISO para o USB usando DiscUtils — sem montar ISO via PowerShell.
        ''' Progresso real por bytes: atualiza barra e status com nome do arquivo atual.
        ''' Funciona com ISO9660 (Joliet/RockRidge) e UDF.
        ''' </summary>
        Private Async Function CopyIsoFilesWithProgressAsync(isoPath As String, destRoot As String,
                                                             Optional progressBase As Integer = 0,
                                                             Optional progressRange As Integer = 100,
                                                             Optional cancellationToken As CancellationToken = Nothing) As Task
            Await _logService.WriteAsync($"USB | ISO_COPY | INICIO | {Path.GetFileName(isoPath)} -> {destRoot}")

            ' Calcular total de bytes usando DiscUtils
            Dim allEntries As New List(Of IsoFileEntry)()
            Dim totalBytes As Long = 0

            Using isoStream As New FileStream(isoPath, FileMode.Open, FileAccess.Read, FileShare.Read, 4 * 1024 * 1024, useAsync:=True)
                ' Tentar UDF primeiro (Windows ISO moderno), fallback para ISO9660
                Dim useUdf As Boolean = False
                Try
                    Using testUdf As New UdfReader(isoStream)
                        useUdf = True
                    End Using
                    isoStream.Seek(0, SeekOrigin.Begin)
                Catch
                    isoStream.Seek(0, SeekOrigin.Begin)
                End Try

                ' Enumerar arquivos para calcular total
                If useUdf Then
                    Using reader As New UdfReader(isoStream)
                        EnumerateDiscUtilsFiles(reader.Root, String.Empty, allEntries, totalBytes)
                    End Using
                Else
                    Using reader As New CDReader(isoStream, True)
                        EnumerateDiscUtilsFiles(reader.Root, String.Empty, allEntries, totalBytes)
                    End Using
                End If
            End Using

            Await _logService.WriteAsync($"USB | ISO_COPY | TOTAL | {allEntries.Count} arquivos | {totalBytes:N0} bytes")

            Dim copiedBytes As Long = 0
            Dim bufSize = 4 * 1024 * 1024

            Using isoStream As New FileStream(isoPath, FileMode.Open, FileAccess.Read, FileShare.Read, bufSize, useAsync:=True)
                Dim useUdf As Boolean = False
                Try
                    Using testUdf As New UdfReader(isoStream)
                        useUdf = True
                    End Using
                    isoStream.Seek(0, SeekOrigin.Begin)
                Catch
                    isoStream.Seek(0, SeekOrigin.Begin)
                End Try

                Dim openFs As Func(Of DiscUtils.DiscFileSystem)
                If useUdf Then
                    openFs = Function() CType(New UdfReader(isoStream), DiscUtils.DiscFileSystem)
                Else
                    openFs = Function() CType(New CDReader(isoStream, True), DiscUtils.DiscFileSystem)
                End If

                Using fs As DiscUtils.DiscFileSystem = openFs()
                    Dim buf(bufSize - 1) As Byte

                    For Each entry In allEntries
                        cancellationToken.ThrowIfCancellationRequested()
                        Dim relPath = If(String.IsNullOrEmpty(entry.IsoDir),
                                         entry.Entry,
                                         Path.Combine(entry.IsoDir, entry.Entry))
                        Dim destPath = Path.Combine(destRoot, relPath)

                        ' Criar diretório destino se necessário
                        Dim destDir = Path.GetDirectoryName(destPath)
                        If Not String.IsNullOrEmpty(destDir) Then
                            Directory.CreateDirectory(destDir)
                        End If

                        Dim copiedPercent = If(totalBytes > 0, CInt(Math.Min(100, copiedBytes * 100L \ totalBytes)), 100)
                        _statusLabel.Text = $"Copiando {relPath}... {copiedPercent}%"

                        Try
                            Dim openedPath As String = String.Empty
                            Using srcStream As IO.Stream = OpenIsoFileWithFallback(fs, relPath, openedPath)
                                Using dstStream As New FileStream(destPath, FileMode.Create, FileAccess.Write, FileShare.None, bufSize, useAsync:=True)
                                    Dim read As Integer
                                    Do
                                        cancellationToken.ThrowIfCancellationRequested()
                                        read = Await srcStream.ReadAsync(buf, 0, buf.Length, cancellationToken)
                                        If read = 0 Then Exit Do
                                        Await dstStream.WriteAsync(buf, 0, read, cancellationToken)
                                        copiedBytes += read
                                        Dim pct = progressBase + CInt(Math.Min(progressRange,
                                                  If(totalBytes > 0, (copiedBytes * CLng(progressRange)) \ totalBytes, progressRange)))
                                        SetProgress(pct)
                                        Dim newPercent = If(totalBytes > 0, CInt(Math.Min(100, copiedBytes * 100L \ totalBytes)), 100)
                                        If newPercent <> copiedPercent Then
                                            copiedPercent = newPercent
                                            _statusLabel.Text = $"Copiando {relPath}... {copiedPercent}%"
                                        End If
                                    Loop
                                End Using
                            End Using
                        Catch ex As Exception
                            _logService.WriteAsync($"USB | ISO_COPY | ERRO_ARQUIVO | RelPath={relPath} | Dest={destPath} | Erro={ex.Message}").GetAwaiter().GetResult()
                            Throw
                        End Try
                    Next
                End Using
            End Using

            Await _logService.WriteAsync($"USB | ISO_COPY | OK | {allEntries.Count} arquivos copiados")
        End Function

        Private Structure IsoFileEntry
            Public IsoDir As String
            Public Entry As String
            Public Size As Long
        End Structure

        Private Shared Function OpenIsoFileWithFallback(fs As DiscUtils.DiscFileSystem, relPath As String, ByRef openedPath As String) As IO.Stream
            Dim candidates As New List(Of String)()
            Dim p = If(relPath, String.Empty)
            candidates.Add(p)
            candidates.Add(p.Replace("\"c, "/"c))
            candidates.Add(p.Replace("/"c, "\"c))
            candidates.Add(p.TrimStart("\"c, "/"c))
            candidates.Add(p.Replace("\"c, "/"c).TrimStart("/"c))

            Dim lastEx As Exception = Nothing
            Dim seen As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
            For Each candidate In candidates
                If String.IsNullOrWhiteSpace(candidate) Then
                    Continue For
                End If
                If seen.Contains(candidate) Then
                    Continue For
                End If
                seen.Add(candidate)
                Try
                    openedPath = candidate
                    Return fs.OpenFile(candidate, FileMode.Open, FileAccess.Read)
                Catch ex As Exception
                    lastEx = ex
                End Try
            Next

            Throw New IOException($"Nao foi possivel abrir '{relPath}' na ISO. Ultimo erro: {If(lastEx Is Nothing, "desconhecido", lastEx.Message)}", lastEx)
        End Function

        Private Shared Function GetBootTargetDisplayName(target As UsbBootTarget) As String
            If target = UsbBootTarget.Uefi Then
                Return "UEFI"
            End If
            Return "BIOS"
        End Function

        Private Shared Function NormalizeIsoEntryPath(path As String) As String
            Return If(path, String.Empty).Replace("/"c, "\"c).TrimStart("\"c).ToUpperInvariant()
        End Function

        Private Function GetIsoFileEntries(isoPath As String) As List(Of IsoFileEntry)
            Dim allEntries As New List(Of IsoFileEntry)()
            Dim totalBytes As Long = 0

            Using isoStream As New FileStream(isoPath, FileMode.Open, FileAccess.Read, FileShare.Read, 4 * 1024 * 1024, useAsync:=False)
                Dim useUdf As Boolean = False
                Try
                    Using testUdf As New UdfReader(isoStream)
                        useUdf = True
                    End Using
                    isoStream.Seek(0, SeekOrigin.Begin)
                Catch
                    isoStream.Seek(0, SeekOrigin.Begin)
                End Try

                If useUdf Then
                    Using reader As New UdfReader(isoStream)
                        EnumerateDiscUtilsFiles(reader.Root, String.Empty, allEntries, totalBytes)
                    End Using
                Else
                    Using reader As New CDReader(isoStream, True)
                        EnumerateDiscUtilsFiles(reader.Root, String.Empty, allEntries, totalBytes)
                    End Using
                End If
            End Using

            Return allEntries
        End Function

        Private Sub ValidateIsoForBootTarget(isoPath As String, target As UsbBootTarget)
            Dim analysis = _isoAnalysisService.Analyze(isoPath)
            If analysis.HasFilesOver4Gb Then
                Throw New InvalidOperationException($"A ISO contem arquivo maior que 4 GB ({analysis.LargestFilePath}). O boot USB usa FAT32 para compatibilidade BIOS/UEFI; sera necessario dividir o arquivo ou usar outra imagem.")
            End If

            If target = UsbBootTarget.Uefi Then
                If Not analysis.HasEfiBootX64 Then
                    Throw New InvalidOperationException("A ISO nao contem EFI\BOOT\BOOTX64.EFI. Sem esse arquivo, o USB nao e inicializavel no modo UEFI padrao x64.")
                End If
                If Not analysis.HasUefiElTorito AndAlso Not analysis.HasBootMgr Then
                    Throw New InvalidOperationException("A ISO contem arquivos UEFI, mas a analise nao confirmou boot UEFI por El Torito ou estrutura Windows/bootmgr.")
                End If
                Return
            End If

            If Not analysis.HasBootMgr Then
                Throw New InvalidOperationException("A ISO nao contem BOOTMGR. O metodo BIOS atual prepara apenas ISOs Windows/WinPE baseadas em bootmgr.")
            End If
        End Sub

        ' Particao FAT auxiliar do UEFI:NTFS (Rufus, res/uefi/uefi-ntfs.img). Origem e licenca em assets\uefi-ntfs\ORIGEM.txt.
        Private Const UefiNtfsImageSha256 As String = "72683fa1250eeea772d3399277b434d4e55ba8dd0dc926e52d817e701fc2eb9e"

        Private Shared Function GetUefiNtfsArtifactCandidates() As String()
            Return New String() {
                Path.Combine(AppPaths.AssetsDir, "uefi-ntfs", "uefi-ntfs.img")
            }
        End Function

        ''' <summary>Caminho da imagem UEFI:NTFS embarcada, so se o SHA-256 conferir; vazio se ausente ou diferente.</summary>
        Private Shared Function FindUefiNtfsBootloaderPath() As String
            For Each candidate In GetUefiNtfsArtifactCandidates()
                If File.Exists(candidate) AndAlso String.Equals(ComputeFileSha256(candidate), UefiNtfsImageSha256, StringComparison.OrdinalIgnoreCase) Then
                    Return candidate
                End If
            Next
            Return String.Empty
        End Function

        Private Shared Function ComputeFileSha256(path As String) As String
            Using stream = File.OpenRead(path)
                Return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(stream)).ToLowerInvariant()
            End Using
        End Function

        Private Async Function EnsureUefiNtfsPrerequisitesAsync(isoPath As String, analysis As IsoAnalysisResult) As Task(Of Boolean)
            If analysis Is Nothing Then
                Throw New ArgumentNullException(NameOf(analysis))
            End If

            If Not analysis.HasEfiBootX64 Then
                Await _logService.WriteAsync($"USB | UEFI_NTFS | BLOCK | Image={Path.GetFileName(isoPath)} | Reason=MissingIsoBootx64")
                MessageBox.Show("A ISO nao contem EFI\BOOT\BOOTX64.EFI. Sem esse arquivo, o metodo UEFI:NTFS nao consegue encaminhar o boot para o volume NTFS.",
                                "USB Boot",
                                MessageBoxButtons.OK,
                                MessageBoxIcon.Error)
                Return False
            End If

            If Not analysis.HasFilesOver4Gb Then
                Await _logService.WriteAsync($"USB | UEFI_NTFS | WARN | Image={Path.GetFileName(isoPath)} | Reason=NoFilesOver4Gb")
            End If

            Dim bootloaderPath = FindUefiNtfsBootloaderPath()
            If String.IsNullOrWhiteSpace(bootloaderPath) Then
                Dim searched = String.Join(Environment.NewLine, GetUefiNtfsArtifactCandidates())
                Await _logService.WriteAsync($"USB | UEFI_NTFS | BLOCK | Image={Path.GetFileName(isoPath)} | Reason=MissingBootloaderArtifact")
                MessageBox.Show("A imagem da particao auxiliar UEFI:NTFS nao foi encontrada ou nao confere com o SHA-256 esperado." & Environment.NewLine & Environment.NewLine &
                                "Reinstale o aplicativo para restaurar os arquivos embarcados." & Environment.NewLine & Environment.NewLine &
                                "Caminhos verificados:" & Environment.NewLine & searched,
                                "USB Boot",
                                MessageBoxButtons.OK,
                                MessageBoxIcon.Warning)
                Return False
            End If

            Await _logService.WriteAsync($"USB | UEFI_NTFS | PREREQ_OK | Image={Path.GetFileName(isoPath)} | Bootloader={bootloaderPath}")
            Return True
        End Function

        ''' <summary>
        ''' Layout UEFI:NTFS (P1): MBR; particao 1 NTFS com os arquivos da ISO; particao 2 no fim (tipo 0xEF) com a imagem
        ''' UEFI:NTFS gravada byte a byte. O firmware UEFI inicia pela particao 2, que carrega EFI\BOOT\BOOTX64.EFI da NTFS.
        ''' So UEFI: e para ISOs como a do Windows 11, que exige UEFI. A imagem da ISO nao e alterada.
        ''' </summary>
        Private Async Function ExecuteUefiNtfsBootAsync(drive As UsbDriveOption, item As CacheItem, analysis As IsoAnalysisResult) As Task
            Dim imagePath = FindUefiNtfsBootloaderPath()
            Dim confirm = MessageBox.Show($"O USB selecionado sera formatado para criacao de USB bootavel UEFI:NTFS (particao NTFS + particao FAT auxiliar).{Environment.NewLine}{Environment.NewLine}" &
                                          $"ISO: {Path.GetFileName(item.FullPath)}{Environment.NewLine}Destino: Disk {drive.DiskNumber} - {drive.Model}{Environment.NewLine}{Environment.NewLine}" &
                                          "Todo o conteudo, particoes e letras atuais do USB serao apagados. Deseja continuar?",
                                          "USB Boot", MessageBoxButtons.YesNo, MessageBoxIcon.Warning)
            If confirm <> DialogResult.Yes Then
                Await _logService.WriteAsync($"USB | UEFI_NTFS | CANCELADO_USUARIO | {item.DisplayName}")
                Return
            End If

            Dim cancellationToken As CancellationToken
            If Not TryBeginUsbOperation(drive, True, cancellationToken) Then
                Return
            End If

            Dim autoplayBackup As AutoplayBackup = Nothing
            Dim errorLog As String = Nothing
            Try
                _bootButton.Enabled = False
                _zeroUsbButton.Enabled = False
                SetBusyIndicator(True)
                SetProgressMarquee(True)
                If _settingsService.Load().AutoDisableAutoplayDuringUsbOps Then
                    autoplayBackup = DisableAutoplay()
                End If
                Await _logService.WriteAsync($"USB | UEFI_NTFS | INICIO | {item.DisplayName} -> Disk {drive.DiskNumber} | Imagem={imagePath}")

                ' Mesma opcao "Validar hash antes da gravacao ISO" da copia FAT32: confere a ISO antes de apagar o USB.
                If _settingsService.Load().AutoValidateSha256BeforeIsoBurn AndAlso Not Await ConfirmIsoSha256BeforeBurnAsync(item, cancellationToken) Then
                    Return
                End If

                _statusLabel.Text = "Criando particoes NTFS + UEFI:NTFS..."
                Await CreateUefiNtfsPartitionsAsync(drive, cancellationToken)

                ' A particao auxiliar e gravada antes de formatar a NTFS: ainda nao ha volume montado no disco.
                _statusLabel.Text = "Gravando particao auxiliar UEFI:NTFS..."
                Dim auxOffset = Await WriteUefiNtfsPartitionAsync(drive.DiskNumber, imagePath, cancellationToken)

                _statusLabel.Text = "Formatando a particao principal em NTFS..."
                Dim letter = Await FormatUefiNtfsMainPartitionAsync(drive, UsbVolumeLabels.ForIsoCopy(FindCatalogVolumeLabel(item), analysis?.Label, item.FullPath, "NTFS"), cancellationToken)
                drive.SetDriveLetter(letter)

                If Not WaitForDriveReady(drive.RootPath, 20) Then
                    Throw New IOException("O volume NTFS do USB nao ficou pronto depois de gravar a particao auxiliar.")
                End If
                SetProgressMarquee(False)
                _statusLabel.Text = "Copiando arquivos da ISO para o volume NTFS..."
                Await _logService.WriteAsync($"USB | UEFI_NTFS | COPIA | {item.DisplayName} -> {drive.RootPath}")
                Await CopyIsoFilesWithProgressAsync(item.FullPath, drive.RootPath, progressBase:=5, progressRange:=85, cancellationToken:=cancellationToken)

                SetProgress(90)
                _statusLabel.Text = "Validando copia por manifesto..."
                Await VerifyLogicalCopyManifestAsync(item.FullPath, drive.RootPath, cancellationToken)

                _statusLabel.Text = "Validando particao auxiliar UEFI:NTFS..."
                Dim auxOk = VerifyUefiNtfsPartition(drive.DiskNumber, auxOffset, imagePath)
                Await _logService.WriteAsync($"USB | UEFI_NTFS | AUX_VERIFY | Offset={auxOffset} | Ok={auxOk}")
                If Not auxOk Then
                    Throw New IOException("A particao auxiliar UEFI:NTFS gravada no USB nao confere com a imagem embarcada.")
                End If

                Dim uefiFilesOk = Await VerifyUefiBootFilesOnUsbAsync(drive.RootPath)
                If Not uefiFilesOk Then
                    Throw New IOException("A copia terminou, mas EFI\BOOT\BOOTX64.EFI nao foi encontrado no volume NTFS.")
                End If
                Await _logService.WriteAsync($"USB | UEFI_NTFS | OK | {item.DisplayName} | UEFI={uefiFilesOk}")
                SetProgress(100)
                _statusLabel.Text = "USB UEFI:NTFS verificado por manifesto e particao auxiliar."
                MessageBox.Show($"USB UEFI:NTFS verificado por manifesto e particao auxiliar.{Environment.NewLine}{Environment.NewLine}" &
                                $"O USB inicia em modo UEFI (o Windows 11 exige UEFI; nao ha boot BIOS/Legacy).{Environment.NewLine}{Environment.NewLine}" &
                                "Com Secure Boot ligado, o firmware precisa aceitar a CA UEFI de terceiros da Microsoft (padrao na maioria dos PCs).",
                                "USB Boot", MessageBoxButtons.OK, MessageBoxIcon.Information)
                OfferEjectUsb(drive)
            Catch ex As OperationCanceledException
                errorLog = $"USB | UEFI_NTFS | CANCELADO | {item.DisplayName}"
                _statusLabel.Text = "Operacao USB cancelada."
            Catch ex As Exception
                errorLog = $"USB | UEFI_NTFS | ERRO | {item.DisplayName} | {ex.Message}"
                _logService.WriteAsync($"USB | UEFI_NTFS | TRACE | {ex}").GetAwaiter().GetResult()
                _statusLabel.Text = "Falha na gravacao UEFI:NTFS."
                MessageBox.Show($"Falha na gravacao UEFI:NTFS: {ex.Message}", "USB Boot", MessageBoxButtons.OK, MessageBoxIcon.Error)
            Finally
                ReleaseHeldVolumeLocks()
                If autoplayBackup IsNot Nothing Then
                    RestoreAutoplay(autoplayBackup)
                End If
                SetProgressMarquee(False)
                SetBusyIndicator(False)
                _bootButton.Enabled = True
                _zeroUsbButton.Enabled = True
                EndUsbOperation()
                RefreshUsbListKeepingDisk(drive.DiskNumber)
            End Try

            If Not String.IsNullOrWhiteSpace(errorLog) Then
                Await _logService.WriteAsync(errorLog)
            End If
        End Function

        ''' <summary>
        ''' Cria as duas particoes do UEFI:NTFS sem formatar (MBR; 1 = principal ativa; 2 = auxiliar 0xEF no fim).
        ''' A marca de ativa nao e usada pelo boot UEFI; fica como precaucao para firmwares que estranham MBR sem
        ''' particao ativa (mesmo layout do Rufus).
        ''' </summary>
        Private Async Function CreateUefiNtfsPartitionsAsync(drive As UsbDriveOption, cancellationToken As CancellationToken) As Task
            GuardTargetDisk(drive.DiskNumber)
            Dim sizeBytes = If(drive.SizeBytes > 0, drive.SizeBytes, GetDiskSizeBytes(drive.DiskNumber))
            ' Particao 1 ate ~3 MiB do fim; a particao 2 fica com o resto (>= 1 MiB, tamanho da imagem UEFI:NTFS).
            Dim ntfsMegabytes = DiskpartScripts.UefiNtfsMainPartitionMegabytes(sizeBytes)
            If ntfsMegabytes < 1024L Then
                Throw New IOException("USB pequeno demais para o layout UEFI:NTFS.")
            End If

            Await DismountDiskVolumesAsync(drive.DiskNumber)
            cancellationToken.ThrowIfCancellationRequested()
            Await _logService.WriteAsync($"USB | UEFI_NTFS | DISKPART | Disk={drive.DiskNumber} | Size={sizeBytes} | NtfsMB={ntfsMegabytes}")
            Dim script = DiskpartScripts.UefiNtfsPartitions(drive.DiskNumber, ntfsMegabytes)
            Dim output = Await RunDiskpartScriptAsync(script, cancellationToken)
            Await _logService.WriteAsync($"USB | UEFI_NTFS | DISKPART_OK | Disk={drive.DiskNumber} | {TrimDiskpartOutput(output)}")
        End Function

        ''' <summary>Formata a particao 1 em NTFS, atribui letra e devolve a letra montada.</summary>
        Private Async Function FormatUefiNtfsMainPartitionAsync(drive As UsbDriveOption, label As String, cancellationToken As CancellationToken) As Task(Of String)
            GuardTargetDisk(drive.DiskNumber)
            Await _logService.WriteAsync($"USB | UEFI_NTFS | FORMAT | Disk={drive.DiskNumber} | Label={label}")
            Dim script = DiskpartScripts.UefiNtfsFormatMain(drive.DiskNumber, label)
            Dim output = Await RunDiskpartScriptAsync(script, cancellationToken)
            Await _logService.WriteAsync($"USB | UEFI_NTFS | FORMAT_OK | Disk={drive.DiskNumber} | {TrimDiskpartOutput(output)}")

            Dim mountedLetter = Await WaitForVolumeMountAsync(drive.DiskNumber, 20_000)
            If String.IsNullOrWhiteSpace(mountedLetter) Then
                mountedLetter = GetLogicalDriveLetterFromDiskIndex(drive.DiskNumber)
            End If
            If String.IsNullOrWhiteSpace(mountedLetter) Then
                Throw New IOException($"O volume NTFS do Disk {drive.DiskNumber} nao foi montado depois de formatar.")
            End If
            Return mountedLetter.TrimEnd("\"c).TrimEnd(":"c).Trim().ToUpperInvariant()
        End Function

        ''' <summary>
        ''' Grava a imagem UEFI:NTFS no inicio da particao 2 (lido do MBR) e garante o tipo 0xEF. Devolve o offset em bytes.
        ''' </summary>
        Private Async Function WriteUefiNtfsPartitionAsync(diskNumber As Integer, imagePath As String, cancellationToken As CancellationToken) As Task(Of Long)
            GuardTargetDisk(diskNumber)
            Dim image = File.ReadAllBytes(imagePath)
            Dim mbr = TryReadPhysicalSector(diskNumber, 0)
            Dim auxPartition = UefiNtfsAuxPartition.ValidateTarget(mbr, image.Length)
            Await _logService.WriteAsync($"USB | UEFI_NTFS | AUX_PARTITION | Type=0x{auxPartition.PartitionType:X2} | Offset={auxPartition.StartOffsetBytes} | Size={CLng(auxPartition.SectorCount) * 512L} | Image={image.Length}")

            ' Nenhuma particao esta formatada neste momento, entao nao ha volume montado a desmontar.
            cancellationToken.ThrowIfCancellationRequested()
            Dim written As (Offset As Long, TypeFixed As Boolean)
            Using handle = OpenPhysicalDeviceHandle($"\\.\PhysicalDrive{diskNumber}", NativeMethods.GENERIC_READ Or NativeMethods.GENERIC_WRITE)
                Using stream As New FileStream(handle, FileAccess.ReadWrite, 1024 * 1024)
                    written = Await UefiNtfsAuxPartition.WriteAsync(stream, mbr, image, cancellationToken)
                End Using
            End Using
            If written.TypeFixed Then
                Await _logService.WriteAsync("USB | UEFI_NTFS | AUX_TYPE_FIX | 0xEF")
            End If
            Await _logService.WriteAsync($"USB | UEFI_NTFS | AUX_WRITE_OK | Offset={written.Offset} | Bytes={image.Length}")
            Return written.Offset
        End Function

        ''' <summary>True se o MBR tem particao tipo 0xEF cujo conteudo e a imagem UEFI:NTFS embarcada.</summary>
        Private Shared Function IsUefiNtfsLayout(diskNumber As Integer) As Boolean
            Dim imagePath = FindUefiNtfsBootloaderPath()
            Dim mbr = TryReadPhysicalSector(diskNumber, 0)
            If String.IsNullOrWhiteSpace(imagePath) OrElse Not MbrLayout.HasBootSignature(mbr) Then Return False
            For Each efiEntry In UefiNtfsAuxPartition.Candidates(mbr)
                Try
                    If VerifyUefiNtfsPartition(diskNumber, efiEntry.StartOffsetBytes, imagePath) Then Return True
                Catch
                End Try
            Next
            Return False
        End Function

        Private Shared Function VerifyUefiNtfsPartition(diskNumber As Integer, offset As Long, imagePath As String) As Boolean
            Using stream As New FileStream($"\\.\PhysicalDrive{diskNumber}", FileMode.Open, FileAccess.Read, FileShare.ReadWrite)
                Return UefiNtfsAuxPartition.RegionMatches(stream, offset, File.ReadAllBytes(imagePath))
            End Using
        End Function


        Private Sub VerifyUefiBootFilesOnUsb(rootPath As String)
            Dim bootx64 = Path.Combine(rootPath, "EFI", "BOOT", "BOOTX64.EFI")
            If Not File.Exists(bootx64) Then
                Throw New IOException("A copia terminou, mas EFI\BOOT\BOOTX64.EFI nao foi encontrado no USB. O boot UEFI nao foi confirmado.")
            End If
        End Sub

        Private Async Function VerifyUefiBootFilesOnUsbAsync(rootPath As String) As Task(Of Boolean)
            Dim bootx64 = Path.Combine(rootPath, "EFI", "BOOT", "BOOTX64.EFI")
            Dim bootmgrEfi = Path.Combine(rootPath, "bootmgr.efi")
            Dim efiDir = Path.Combine(rootPath, "EFI")
            Dim ok = File.Exists(bootx64) OrElse File.Exists(bootmgrEfi) OrElse Directory.Exists(efiDir)
            Await _logService.WriteAsync($"USB | BOOT_UEFI | CHECK | Bootx64={File.Exists(bootx64)} | BootMgrEfi={File.Exists(bootmgrEfi)} | EfiDir={Directory.Exists(efiDir)}")
            Return ok
        End Function

        Private Async Function VerifyLogicalCopyManifestAsync(isoPath As String, rootPath As String, cancellationToken As CancellationToken) As Task
            Dim expected = GetIsoFileEntries(isoPath).Select(Function(entry) (If(String.IsNullOrEmpty(entry.IsoDir), entry.Entry, Path.Combine(entry.IsoDir, entry.Entry)), entry.Size))
            Dim check = LogicalCopyCheck.CheckSizes(rootPath, expected, cancellationToken)
            For Each missingPath In check.MissingPaths.Take(20)
                Await _logService.WriteAsync($"USB | VERIFY_LOGICAL | MISSING | {missingPath}")
            Next
            For Each mismatch In check.SizeMismatches.Take(20)
                Await _logService.WriteAsync($"USB | VERIFY_LOGICAL | SIZE_MISMATCH | {mismatch.RelativePath} | ISO={mismatch.Expected} | USB={mismatch.Actual}")
            Next

            Dim missingCount = check.MissingPaths.Count
            Dim mismatchCount = check.SizeMismatches.Count
            Await _logService.WriteAsync($"USB | VERIFY_LOGICAL | MANIFEST | Checked={check.Checked} | Missing={missingCount} | Mismatch={mismatchCount}")
            If missingCount > 0 OrElse mismatchCount > 0 Then
                Dim defenderText = If(missingCount > 0, Await DescribeDefenderRemovalsAsync(rootPath, check.MissingPaths), String.Empty)
                Throw New IOException($"Verificacao logica falhou: {missingCount} arquivo(s) ausente(s) e {mismatchCount} divergente(s).{defenderText}")
            End If
        End Function

        ''' <summary>
        ''' Procura no log do Microsoft Defender (ultimas 3 horas) os arquivos que faltaram no USB e devolve um texto
        ''' para a mensagem de erro; vazio se nenhum evento corresponder ou se o log nao puder ser lido.
        ''' </summary>
        Private Async Function DescribeDefenderRemovalsAsync(rootPath As String, missingPaths As List(Of String)) As Task(Of String)
            Try
                Dim startInfo As New ProcessStartInfo("wevtutil.exe", DefenderEvents.BuildWevtutilArguments(180, 500)) With {
                    .UseShellExecute = False,
                    .RedirectStandardOutput = True,
                    .RedirectStandardError = True,
                    .CreateNoWindow = True
                }
                Dim xmlText As String
                Using process = System.Diagnostics.Process.Start(startInfo)
                    Dim outputTask = process.StandardOutput.ReadToEndAsync()
                    If Not process.WaitForExit(20000) Then
                        process.Kill()
                        Await _logService.WriteAsync("USB | VERIFY_LOGICAL | DEFENDER | TIMEOUT")
                        Return String.Empty
                    End If
                    xmlText = Await outputTask
                End Using

                Dim matches = DefenderEvents.MatchMissingFiles(DefenderEvents.Parse(xmlText), rootPath, missingPaths)
                If matches.Count = 0 Then
                    Await _logService.WriteAsync("USB | VERIFY_LOGICAL | DEFENDER | SEM_EVENTO")
                    Return String.Empty
                End If
                For Each pair In matches
                    Await _logService.WriteAsync($"USB | VERIFY_LOGICAL | DEFENDER | {pair.Key} | {pair.Value}")
                Next
                Dim lines = String.Join(Environment.NewLine, matches.Take(10).Select(Function(pair) $"- {pair.Key} ({pair.Value})"))
                Return $"{Environment.NewLine}{Environment.NewLine}Removido(s) ou bloqueado(s) pelo Microsoft Defender:{Environment.NewLine}{lines}" &
                       $"{Environment.NewLine}{Environment.NewLine}Libere esses arquivos com a TI (Intune/Defender) ou use uma imagem sem eles."
            Catch ex As Exception
                _logService.WriteAsync($"USB | VERIFY_LOGICAL | DEFENDER | ERRO | {ex.Message}").GetAwaiter().GetResult()
                Return String.Empty
            End Try
        End Function

        Private Async Function ApplyRufusBootMgrFat32Async(diskNumber As Integer, driveLetter As String) As Task(Of Boolean)
            GuardTargetDisk(diskNumber)
            Dim errorMessage As String = Nothing
            Try
                Await _logService.WriteAsync($"USB | BOOT_BIOS | RUFUS_APPLY | Disk={diskNumber} | Drive {driveLetter}")
                Await Task.Run(Sub() Services.RufusBootSectorWriter.ApplyBootMgrFat32(diskNumber, driveLetter))

                Dim mbr = TryReadPhysicalSector(diskNumber, 0)
                Dim vbr = TryReadVolumeBootSector(driveLetter)
                Dim activePartitionIndex = MbrLayout.GetActivePartitionIndex(mbr)
                Dim partitionType = MbrLayout.GetPartitionType(mbr, activePartitionIndex)
                Dim vbrReferencesBootMgr = MbrLayout.SectorContainsAscii(vbr, "BOOTMGR")

                Await _logService.WriteAsync($"USB | BOOT_BIOS | RUFUS_VERIFY | MBR={MbrLayout.HasBootSignature(mbr)} | ActiveIndex={activePartitionIndex} | Type=0x{partitionType:X2} | VBR={MbrLayout.HasBootSignature(vbr)} | BOOTMGR={vbrReferencesBootMgr}")
                Return MbrLayout.HasBootSignature(mbr) AndAlso activePartitionIndex >= 0 AndAlso partitionType = &H0C AndAlso MbrLayout.HasBootSignature(vbr) AndAlso vbrReferencesBootMgr
            Catch ex As Exception
                errorMessage = ex.Message
            End Try

            Await _logService.WriteAsync($"USB | BOOT_BIOS | RUFUS_ERRO | {errorMessage}")
            Return False
        End Function

        ''' <summary>Enumera recursivamente todos os arquivos no sistema de arquivos DiscUtils.</summary>
        Private Shared Sub EnumerateDiscUtilsFiles(dir As DiscUtils.DiscDirectoryInfo,
                                                   basePath As String,
                                                   ByRef entries As List(Of IsoFileEntry),
                                                   ByRef totalBytes As Long)
            For Each file As DiscUtils.DiscFileInfo In dir.GetFiles()
                Dim e As New IsoFileEntry With {.IsoDir = basePath, .Entry = file.Name, .Size = file.Length}
                entries.Add(e)
                totalBytes += file.Length
            Next
            For Each subDir As DiscUtils.DiscDirectoryInfo In dir.GetDirectories()
                Dim subPath = If(String.IsNullOrEmpty(basePath), subDir.Name, Path.Combine(basePath, subDir.Name))
                EnumerateDiscUtilsFiles(subDir, subPath, entries, totalBytes)
            Next
        End Sub

        Private Function RunRobocopy(sourcePath As String, destinationPath As String) As Integer
            Dim sourceArg = Path.Combine(sourcePath.TrimEnd("\"c), ".")
            Dim destArg = Path.Combine(destinationPath.TrimEnd("\"c), ".")
            Dim args = $"""{sourceArg}"" ""{destArg}"" /E /R:1 /W:1"
            Dim startInfo As New ProcessStartInfo("robocopy", args) With {
                .UseShellExecute = False,
                .CreateNoWindow = True,
                .RedirectStandardOutput = True,
                .RedirectStandardError = True,
                .StandardOutputEncoding = ConsoleOemEncoding,
                .StandardErrorEncoding = ConsoleOemEncoding
            }
            Using roboProcess As Process = Process.Start(startInfo)
                If roboProcess Is Nothing Then
                    Return 16
                End If
                Dim output = roboProcess.StandardOutput.ReadToEnd()
                Dim [error] = roboProcess.StandardError.ReadToEnd()
                roboProcess.WaitForExit()
                Dim exitCode = roboProcess.ExitCode

                If exitCode >= 8 Then
                    Dim snippet = If([error], output)
                    If Not String.IsNullOrWhiteSpace(snippet) Then
                        snippet = snippet.Replace(vbCr, " ").Replace(vbLf, " ")
                        If snippet.Length > 500 Then
                            snippet = snippet.Substring(0, 500) & "..."
                        End If
                    End If
                    _logService.WriteAsync($"USB | ROBOCOPY | ERRO | {exitCode} | {snippet}").GetAwaiter().GetResult()
                Else
                    _logService.WriteAsync($"USB | ROBOCOPY | OK | {exitCode}").GetAwaiter().GetResult()
                End If
                Return exitCode
            End Using
        End Function

        ''' <summary>
        ''' Detecta o tipo de ISO (Windows/Linux/Hybrid) usando DiscUtils
        ''' </summary>
        Private Function DetectIsoType(isoPath As String) As IsoType
            Try
                _logService.WriteAsync($"USB | ISO_DETECT | INICIO | {Path.GetFileName(isoPath)}").GetAwaiter().GetResult()
                
                Using isoStream As FileStream = File.Open(isoPath, FileMode.Open, FileAccess.Read, FileShare.Read)
                    ' Tentar detectar UDF (Windows/Moderno)
                    Try
                        Using udfReader As New UdfReader(isoStream)
                            _logService.WriteAsync($"USB | ISO_DETECT | UDF | Windows UDF detectado | Label: {udfReader.VolumeLabel}").GetAwaiter().GetResult()
                            Return IsoType.Windows
                        End Using
                    Catch ex As Exception
                        _logService.WriteAsync($"USB | ISO_DETECT | UDF_ERRO | {ex.Message}").GetAwaiter().GetResult()
                    End Try
                    
                    ' Reset stream position
                    isoStream.Seek(0, SeekOrigin.Begin)
                    
                    ' Tentar detectar ISO9660 com Rock Ridge (Linux)
                    Try
                        Using cdReader As New CDReader(isoStream, True)
                            If cdReader.ActiveVariant = Iso9660Variant.RockRidge Then
                                _logService.WriteAsync($"USB | ISO_DETECT | ROCKRIDGE | Linux Rock Ridge detectado").GetAwaiter().GetResult()
                                Return IsoType.Linux
                            ElseIf cdReader.ActiveVariant = Iso9660Variant.Joliet Then
                                _logService.WriteAsync($"USB | ISO_DETECT | JOLIET | ISO9660 Joliet detectado (compatível Windows)").GetAwaiter().GetResult()
                                Return IsoType.Windows
                            Else
                                _logService.WriteAsync($"USB | ISO_DETECT | ISO9660 | ISO9660 padrão detectado").GetAwaiter().GetResult()
                                Return IsoType.Windows
                            End If
                        End Using
                    Catch ex As Exception
                        _logService.WriteAsync($"USB | ISO_DETECT | ISO9660_ERRO | {ex.Message}").GetAwaiter().GetResult()
                    End Try
                End Using
                
                _logService.WriteAsync($"USB | ISO_DETECT | UNKNOWN | Tipo não identificado").GetAwaiter().GetResult()
                Return IsoType.Unknown
                
            Catch ex As Exception
                _logService.WriteAsync($"USB | ISO_DETECT | ERRO | Falha na detecção: {ex.Message}").GetAwaiter().GetResult()
                Return IsoType.Unknown
            End Try
        End Function

        Private Function CleanDiskFast(devicePath As String, diskNumber As Integer, Optional updateProperties As Boolean = True) As Boolean
            GuardTargetDisk(diskNumber)
            _logService.WriteAsync($"USB | CLEAN_FAST | INICIO | Limpando disco {diskNumber} com IOCTL direto").GetAwaiter().GetResult()
            
            Try
                ' 1. Abrir o disco físico com acesso total
                _logService.WriteAsync($"USB | CLEAN_FAST | ABRINDO_DISCO | {devicePath}").GetAwaiter().GetResult()
                
                Dim diskHandle = NativeMethods.CreateFile(
                    devicePath,
                    NativeMethods.GENERIC_READ Or NativeMethods.GENERIC_WRITE,
                    NativeMethods.FILE_SHARE_READ Or NativeMethods.FILE_SHARE_WRITE,
                    IntPtr.Zero,
                    NativeMethods.OPEN_EXISTING,
                    0,
                    IntPtr.Zero
                )
                
                If diskHandle.IsInvalid Then
                    Dim errorCode = NativeMethods.GetLastError()
                    _logService.WriteAsync($"USB | CLEAN_FAST | ERRO_ABERTURA | Falha ao abrir disco: ERROR {errorCode}").GetAwaiter().GetResult()
                    Return False
                End If
                
                Using diskHandleFinal = diskHandle
                    _logService.WriteAsync($"USB | CLEAN_FAST | DISCO_ABERTO | Sucesso ao abrir {devicePath}").GetAwaiter().GetResult()
                    
                    ' 2. Dismountar todos os volumes (como o rpi-imager)
                    _logService.WriteAsync($"USB | CLEAN_FAST | DISMOUNT_VOLUMES | Iniciando dismount de volumes").GetAwaiter().GetResult()
                    
                    Dim volumeLetter = GetLogicalDriveLetterFromDiskIndex(diskNumber)
                    If Not String.IsNullOrWhiteSpace(volumeLetter) Then
                        _logService.WriteAsync($"USB | CLEAN_FAST | VOLUME_ENCONTRADO | Volume {volumeLetter} encontrado").GetAwaiter().GetResult()
                        
                        ' Abrir volume para dismount
                        Dim volumePath = $"\\.\{volumeLetter}"
                        Dim volumeHandle = NativeMethods.CreateFile(
                            volumePath,
                            NativeMethods.GENERIC_READ Or NativeMethods.GENERIC_WRITE,
                            NativeMethods.FILE_SHARE_READ Or NativeMethods.FILE_SHARE_WRITE,
                            IntPtr.Zero,
                            NativeMethods.OPEN_EXISTING,
                            0,
                            IntPtr.Zero
                        )
                        
                        If Not volumeHandle.IsInvalid Then
                            Using volumeHandleFinal = volumeHandle
                                Dim bytesReturned As UInteger = 0
                                
                                ' Dismountar volume (FSCTL_DISMOUNT_VOLUME)
                                If NativeMethods.DeviceIoControl(
                                    volumeHandleFinal,
                                    NativeMethods.FSCTL_DISMOUNT_VOLUME,
                                    IntPtr.Zero,
                                    0,
                                    IntPtr.Zero,
                                    0,
                                    bytesReturned,
                                    IntPtr.Zero
                                ) Then
                                    _logService.WriteAsync($"USB | CLEAN_FAST | DISMOUNT_SUCESSO | Volume {volumeLetter} dismontado").GetAwaiter().GetResult()
                                Else
                                    Dim errorCode = NativeMethods.GetLastError()
                                    _logService.WriteAsync($"USB | CLEAN_FAST | DISMOUNT_ERRO | Falha ao dismount {volumeLetter}: ERROR {errorCode}").GetAwaiter().GetResult()
                                End If
                            End Using
                        End If
                        
                        ' Remover letra da unidade (DeleteVolumeMountPointW)
                        Dim mountPoint = $"{volumeLetter}\"
                        If NativeMethods.DeleteVolumeMountPointW(mountPoint) Then
                            _logService.WriteAsync($"USB | CLEAN_FAST | LETRA_REMOVIDA | Letra {volumeLetter} removida").GetAwaiter().GetResult()
                        Else
                            Dim errorCode = NativeMethods.GetLastError()
                            _logService.WriteAsync($"USB | CLEAN_FAST | LETRA_ERRO | Falha ao remover letra {volumeLetter}: ERROR {errorCode}").GetAwaiter().GetResult()
                        End If
                    Else
                        _logService.WriteAsync($"USB | CLEAN_FAST | SEM_VOLUME | Nenhum volume encontrado para dismount").GetAwaiter().GetResult()
                    End If
                    
                    ' Pequena pausa para o Windows processar
                    Threading.Thread.Sleep(200)
                    
                    ' 3. Deletar layout das partições (IOCTL_DISK_DELETE_DRIVE_LAYOUT)
                    _logService.WriteAsync($"USB | CLEAN_FAST | DELETANDO_LAYOUT | Deletando layout das partições").GetAwaiter().GetResult()
                    
                    Dim bytesReturnedLayout As UInteger = 0
                    If NativeMethods.DeviceIoControl(
                        diskHandleFinal,
                        NativeMethods.IOCTL_DISK_DELETE_DRIVE_LAYOUT,
                        IntPtr.Zero,
                        0,
                        IntPtr.Zero,
                        0,
                        bytesReturnedLayout,
                        IntPtr.Zero
                    ) Then
                        _logService.WriteAsync($"USB | CLEAN_FAST | LAYOUT_DELETADO | Layout de partições deletado com sucesso").GetAwaiter().GetResult()
                    Else
                        Dim errorCode = NativeMethods.GetLastError()
                        _logService.WriteAsync($"USB | CLEAN_FAST | LAYOUT_ERRO | Falha ao deletar layout: ERROR {errorCode}").GetAwaiter().GetResult()
                        Return False
                    End If
                    
                    If updateProperties Then
                        ' 4. Rescan do disco (atualizar view do Windows)
                        _logService.WriteAsync($"USB | CLEAN_FAST | RESCAN | Atualizando view do Windows").GetAwaiter().GetResult()

                        Dim bytesReturnedRescan As UInteger = 0
                        NativeMethods.DeviceIoControl(
                            diskHandleFinal,
                            NativeMethods.IOCTL_DISK_UPDATE_PROPERTIES,
                            IntPtr.Zero,
                            0,
                            IntPtr.Zero,
                            0,
                            bytesReturnedRescan,
                            IntPtr.Zero
                        )
                    Else
                        _logService.WriteAsync("USB | CLEAN_FAST | RESCAN_ADIADO | RAW/DD atualiza propriedades somente apos a gravacao").GetAwaiter().GetResult()
                    End If
                    
                    _logService.WriteAsync($"USB | CLEAN_FAST | SUCESSO | Disco {diskNumber} limpo com IOCTL direto").GetAwaiter().GetResult()
                    Return True
                End Using
                
            Catch ex As Exception
                _logService.WriteAsync($"USB | CLEAN_FAST | ERRO_EXCECAO | {ex.Message}").GetAwaiter().GetResult()
                Return False
            End Try
        End Function

        Private Function OpenPhysicalDevice(devicePath As String) As FileStream
            _logService.WriteAsync($"USB | NATIVE_OPEN | Tentando abrir dispositivo: {devicePath}").GetAwaiter().GetResult()
            Dim shareMode = NativeMethods.FILE_SHARE_READ Or NativeMethods.FILE_SHARE_WRITE Or NativeMethods.FILE_SHARE_DELETE
            Dim attempts = New (Name As String, DesiredAccess As UInteger, Flags As UInteger, Access As FileAccess)() {
                ("Buffered R/W", NativeMethods.GENERIC_READ Or NativeMethods.GENERIC_WRITE, NativeMethods.FILE_ATTRIBUTE_NORMAL Or NativeMethods.FILE_FLAG_SEQUENTIAL_SCAN Or NativeMethods.FILE_FLAG_WRITE_THROUGH, FileAccess.ReadWrite),
                ("Buffered W", NativeMethods.GENERIC_WRITE, NativeMethods.FILE_ATTRIBUTE_NORMAL Or NativeMethods.FILE_FLAG_SEQUENTIAL_SCAN Or NativeMethods.FILE_FLAG_WRITE_THROUGH, FileAccess.Write),
                ("Direct R/W", NativeMethods.GENERIC_READ Or NativeMethods.GENERIC_WRITE, NativeMethods.FILE_FLAG_NO_BUFFERING Or NativeMethods.FILE_FLAG_WRITE_THROUGH, FileAccess.ReadWrite),
                ("Direct W", NativeMethods.GENERIC_WRITE, NativeMethods.FILE_FLAG_NO_BUFFERING Or NativeMethods.FILE_FLAG_WRITE_THROUGH, FileAccess.Write)
            }

            Dim lastError As UInteger = 0UI
            For Each attempt In attempts
                Dim handle = NativeMethods.CreateFile(
                    devicePath,
                    attempt.DesiredAccess,
                    shareMode,
                    IntPtr.Zero,
                    NativeMethods.OPEN_EXISTING,
                    attempt.Flags,
                    IntPtr.Zero
                )

                If Not handle.IsInvalid Then
                    _logService.WriteAsync($"USB | NATIVE_OPEN | {attempt.Name} sucesso").GetAwaiter().GetResult()
                    Return New FileStream(handle, attempt.Access, 4 * 1024 * 1024, isAsync:=False)
                End If

                lastError = NativeMethods.GetLastError()
                _logService.WriteAsync($"USB | NATIVE_OPEN | {attempt.Name} falhou: ERROR {lastError}").GetAwaiter().GetResult()
            Next

            Select Case lastError
                Case NativeMethods.ERROR_ACCESS_DENIED
                    Throw New UnauthorizedAccessException($"Access denied. O volume USB ainda pode estar montado/bloqueado por Explorer, antivirus, indexador ou outro processo. ERROR {lastError}")
                Case NativeMethods.ERROR_SHARING_VIOLATION
                    Throw New IOException($"Sharing violation. Outro processo ainda esta usando o dispositivo USB. ERROR {lastError}")
                Case Else
                    Throw New IOException($"Falha ao abrir dispositivo {devicePath}. ERROR {lastError}")
            End Select
        End Function

        Private Function OpenPhysicalDeviceHandle(devicePath As String, desiredAccess As UInteger) As SafeFileHandle
            _logService.WriteAsync($"USB | NATIVE_OPEN_HANDLE | Tentando abrir dispositivo: {devicePath}").GetAwaiter().GetResult()
            Dim shareMode = NativeMethods.FILE_SHARE_READ Or NativeMethods.FILE_SHARE_WRITE Or NativeMethods.FILE_SHARE_DELETE
            Dim attempts = New (Name As String, Flags As UInteger)() {
                ("Buffered", NativeMethods.FILE_ATTRIBUTE_NORMAL Or NativeMethods.FILE_FLAG_SEQUENTIAL_SCAN Or NativeMethods.FILE_FLAG_WRITE_THROUGH),
                ("Normal", NativeMethods.FILE_ATTRIBUTE_NORMAL),
                ("Direct", NativeMethods.FILE_FLAG_NO_BUFFERING Or NativeMethods.FILE_FLAG_WRITE_THROUGH)
            }

            Dim lastError As UInteger = 0UI
            For Each attempt In attempts
                Dim handle = NativeMethods.CreateFile(
                    devicePath,
                    desiredAccess,
                    shareMode,
                    IntPtr.Zero,
                    NativeMethods.OPEN_EXISTING,
                    attempt.Flags,
                    IntPtr.Zero)

                If Not handle.IsInvalid Then
                    _logService.WriteAsync($"USB | NATIVE_OPEN_HANDLE | {attempt.Name} sucesso").GetAwaiter().GetResult()
                    Return handle
                End If

                lastError = NativeMethods.GetLastError()
                _logService.WriteAsync($"USB | NATIVE_OPEN_HANDLE | {attempt.Name} falhou: ERROR {lastError}").GetAwaiter().GetResult()
            Next

            Select Case lastError
                Case NativeMethods.ERROR_ACCESS_DENIED
                    Throw New UnauthorizedAccessException($"Access denied. O volume USB ainda pode estar montado/bloqueado por Explorer, antivirus, indexador ou outro processo. ERROR {lastError}")
                Case NativeMethods.ERROR_SHARING_VIOLATION
                    Throw New IOException($"Sharing violation. Outro processo ainda esta usando o dispositivo USB. ERROR {lastError}")
                Case Else
                    Throw New IOException($"Falha ao abrir dispositivo {devicePath}. ERROR {lastError}")
            End Select
        End Function

        Private Async Function WriteRawImageWithWin32Async(imagePath As String, devicePath As String, diskNumber As Integer, imageSize As Long, cancellationToken As CancellationToken) As Task
            GuardTargetDisk(diskNumber)
            Const BufferSize As Integer = 1024 * 1024
            Const DeviceWriteChunkSize As Integer = 64 * 1024
            Const MaxWriteRetries As Integer = 4

            Using imgStream As New FileStream(imagePath, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize:=BufferSize, useAsync:=True)
                Dim handle As SafeFileHandle = OpenPhysicalDeviceHandle(devicePath, NativeMethods.GENERIC_READ Or NativeMethods.GENERIC_WRITE)
                Try
                    Dim buffer(BufferSize - 1) As Byte
                    Dim totalBytesWritten As Long = 0
                    Dim lastProgressUpdate As Long = 0
                    Dim ranges = RawWritePlan.Ranges(imageSize)

                    For Each range In ranges
                        imgStream.Seek(range.StartOffset, SeekOrigin.Begin)
                        If Not NativeMethods.SetFilePointerEx(handle, range.StartOffset, IntPtr.Zero, NativeMethods.FILE_BEGIN) Then
                            Throw New IOException($"SetFilePointerEx falhou ao iniciar RAW/DD no offset {range.StartOffset}. Erro Win32: {Marshal.GetLastWin32Error()}")
                        End If
                        Await _logService.WriteAsync($"USB | RAW_COPY | RANGE_START | {range.Name} | Offset={range.StartOffset} | Length={range.Length}")

                        Dim rangeBytesWritten As Long = 0
                        While rangeBytesWritten < range.Length
                            cancellationToken.ThrowIfCancellationRequested()

                            Dim bytesToRead = CInt(Math.Min(buffer.Length, range.Length - rangeBytesWritten))
                            Dim bytesRead = Await imgStream.ReadAsync(buffer, 0, bytesToRead, cancellationToken)
                            If bytesRead = 0 Then
                                Throw New EndOfStreamException($"Fim inesperado da imagem no offset {range.StartOffset + rangeBytesWritten}.")
                            End If

                            Dim blockOffset = 0
                            While blockOffset < bytesRead
                                Dim bytesToWrite = Math.Min(DeviceWriteChunkSize, bytesRead - blockOffset)
                                Dim chunk(bytesToWrite - 1) As Byte
                                Array.Copy(buffer, blockOffset, chunk, 0, bytesToWrite)

                                Dim retryCount = 0
                                While True
                                    cancellationToken.ThrowIfCancellationRequested()

                                    Dim written As UInteger = 0UI
                                    Dim writeOk = NativeMethods.WriteFile(handle, chunk, CUInt(chunk.Length), written, IntPtr.Zero)
                                    If writeOk AndAlso written > 0UI Then
                                        blockOffset += CInt(written)
                                        Exit While
                                    End If

                                    Dim absoluteOffset = range.StartOffset + rangeBytesWritten + blockOffset
                                    Dim errorCode = If(writeOk, 0UI, NativeMethods.GetLastError())
                                    retryCount += 1
                                    Await _logService.WriteAsync($"USB | RAW_COPY | WRITE_RETRY | Offset={absoluteOffset} | Chunk={chunk.Length} | Written={written} | Err={errorCode} | Tentativa={retryCount}/{MaxWriteRetries}")

                                    Dim canRetry = errorCode = 0UI OrElse errorCode = NativeMethods.ERROR_ACCESS_DENIED OrElse errorCode = NativeMethods.ERROR_SHARING_VIOLATION
                                    If retryCount > MaxWriteRetries OrElse Not canRetry Then
                                        Throw New IOException($"WriteFile falhou no offset {absoluteOffset}. Erro Win32: {errorCode}. Written={written}/{chunk.Length}")
                                    End If

                                    handle.Dispose()
                                    Await DismountDiskVolumesAsync(diskNumber, keepLocked:=True)
                                    Await Task.Delay(500, cancellationToken)
                                    handle = OpenPhysicalDeviceHandle(devicePath, NativeMethods.GENERIC_READ Or NativeMethods.GENERIC_WRITE)
                                    If Not NativeMethods.SetFilePointerEx(handle, absoluteOffset, IntPtr.Zero, NativeMethods.FILE_BEGIN) Then
                                        Throw New IOException($"SetFilePointerEx falhou ao retomar RAW/DD no offset {absoluteOffset}. Erro Win32: {Marshal.GetLastWin32Error()}")
                                    End If
                                End While
                            End While

                            rangeBytesWritten += bytesRead
                            totalBytesWritten += bytesRead
                            Dim progress = CInt((totalBytesWritten * 100) / imageSize)
                            If progress > lastProgressUpdate AndAlso progress Mod 10 = 0 Then
                                SetProgress(progress)
                                _statusLabel.Text = $"Copiando imagem bruta... {progress}%"
                                lastProgressUpdate = progress
                            End If
                        End While
                    Next

                    SetProgress(100)
                    _statusLabel.Text = "Cópia bruta concluída."
                    Await _logService.WriteAsync($"USB | RAW_COPY | FIM | {imagePath} -> Disk {diskNumber}")
                Finally
                    If handle IsNot Nothing AndAlso Not handle.IsClosed Then
                        handle.Dispose()
                    End If
                End Try
            End Using
        End Function

        ' Com verifyBeforeRelease, compara a imagem com o USB antes de liberar o disco: depois do refresh o Windows grava
        ' a assinatura do MBR e escreve no volume FAT/EFI que monta, e a comparacao deixaria de refletir a gravacao.
        Private Async Function RawCopyAsync(imagePath As String, diskNumber As Integer, cancellationToken As CancellationToken,
                                            Optional verifyBeforeRelease As Boolean = False) As Task(Of RawCompareResult)
            GuardTargetDisk(diskNumber)
            Dim verification As RawCompareResult = Nothing
            Try
                Await _logService.WriteAsync($"USB | RAW_COPY | INICIO | {imagePath} -> Disk {diskNumber}")

                Dim devicePath = $"\\.\PhysicalDrive{diskNumber}"
                Dim imageSize As Long = New FileInfo(imagePath).Length

                ' Limpar o disco com IOCTL direto (como o rpi-imager)
                Try
                    Await _logService.WriteAsync($"USB | RAW_COPY | LIMPANDO_DISCO | Disk {diskNumber}")
                    Await DismountDiskVolumesAsync(diskNumber, keepLocked:=True)
                    
                    ' 1. Dismountar volume PRIMEIRO (antes do clean)
                    Try
                        Dim volumeLetter = GetLogicalDriveLetterFromDiskIndex(diskNumber)
                        If Not String.IsNullOrWhiteSpace(volumeLetter) Then
                            _logService.WriteAsync($"USB | RAW_COPY | DISMOUNT_PREVIO | Dismounting volume {volumeLetter} antes do clean").GetAwaiter().GetResult()
                            
                            ' Remover letra da unidade (DeleteVolumeMountPointW)
                            Dim mountPoint = $"{volumeLetter}\"
                            If NativeMethods.DeleteVolumeMountPointW(mountPoint) Then
                                _logService.WriteAsync($"USB | RAW_COPY | LETRA_REMOVIDA_PREVIO | Letra {volumeLetter} removida antes do clean").GetAwaiter().GetResult()
                            Else
                                Dim errorCode = NativeMethods.GetLastError()
                                _logService.WriteAsync($"USB | RAW_COPY | LETRA_ERRO_PREVIO | Falha ao remover letra {volumeLetter}: ERROR {errorCode}").GetAwaiter().GetResult()
                            End If
                            
                            ' Abrir volume para dismount
                            Dim volumePath = $"\\.\{volumeLetter}"
                            Dim volumeHandle = NativeMethods.CreateFile(
                                volumePath,
                                NativeMethods.GENERIC_READ Or NativeMethods.GENERIC_WRITE,
                                NativeMethods.FILE_SHARE_READ Or NativeMethods.FILE_SHARE_WRITE,
                                IntPtr.Zero,
                                NativeMethods.OPEN_EXISTING,
                                0,
                                IntPtr.Zero
                            )
                            
                            If Not volumeHandle.IsInvalid Then
                                Using volumeHandleFinal = volumeHandle
                                    Dim bytesReturned As UInteger = 0
                                    
                                    ' Dismountar volume (FSCTL_DISMOUNT_VOLUME)
                                    If NativeMethods.DeviceIoControl(
                                        volumeHandleFinal,
                                        NativeMethods.FSCTL_DISMOUNT_VOLUME,
                                        IntPtr.Zero,
                                        0,
                                        IntPtr.Zero,
                                        0,
                                        bytesReturned,
                                        IntPtr.Zero
                                    ) Then
                                        _logService.WriteAsync($"USB | RAW_COPY | DISMOUNT_SUCESSO_PREVIO | Volume {volumeLetter} dismontado antes do clean").GetAwaiter().GetResult()
                                    Else
                                        Dim errorCode = NativeMethods.GetLastError()
                                        _logService.WriteAsync($"USB | RAW_COPY | DISMOUNT_ERRO_PREVIO | Falha ao dismount {volumeLetter}: ERROR {errorCode}").GetAwaiter().GetResult()
                                    End If
                                End Using
                            End If
                        Else
                            _logService.WriteAsync($"USB | RAW_COPY | SEM_VOLUME_PREVIO | Nenhum volume encontrado para dismount antes do clean").GetAwaiter().GetResult()
                        End If
                        
                        ' Aguardar um momento para o Windows processar
                        Await Task.Delay(500, cancellationToken)
                        
                    Catch ex As OperationCanceledException
                        Throw
                    Catch ex As Exception
                        _logService.WriteAsync($"USB | RAW_COPY | DISMOUNT_ERRO_PREVIO | Erro ao dismount antes do clean: {ex.Message}").GetAwaiter().GetResult()
                        ' Continuar mesmo assim
                    End Try
                    
                    ' 2. Limpar disco com CleanDiskFast
                    Dim cleanSuccess = CleanDiskFast(devicePath, diskNumber, updateProperties:=False)
                    
                    If cleanSuccess Then
                        _logService.WriteAsync($"USB | RAW_COPY | CLEAN_SUCESSO | Disco {diskNumber} limpo com IOCTL direto").GetAwaiter().GetResult()
                        Await DismountDiskVolumesAsync(diskNumber, keepLocked:=True)
                    Else
                        _logService.WriteAsync($"USB | RAW_COPY | CLEAN_FALHOU | Falha ao limpar disco {diskNumber}").GetAwaiter().GetResult()
                        Throw New InvalidOperationException($"Falha ao limpar disco {diskNumber} com IOCTL direto")
                    End If
                    
                Catch ex As OperationCanceledException
                    Throw
                Catch ex As Exception
                    _logService.WriteAsync($"USB | RAW_COPY | ERRO_LIMPEZA | {ex.Message}").GetAwaiter().GetResult()
                    Throw New InvalidOperationException($"Falha na limpeza do disco: {ex.Message}", ex)
                End Try

                _logService.WriteAsync($"USB | RAW_COPY | INICIANDO_COPIA | Disk {diskNumber} limpo, iniciando cópia").GetAwaiter().GetResult()

                Await WriteRawImageWithWin32Async(imagePath, devicePath, diskNumber, imageSize, cancellationToken)
                If verifyBeforeRelease Then
                    _statusLabel.Text = "Verificando gravacao RAW/DD..."
                    verification = Await CompareImageWithUsbDetailedAsync(imagePath, diskNumber, cancellationToken)
                End If
                ' Gravacao concluida: libera os volumes para o Windows reler o disco no refresh abaixo.
                ReleaseHeldVolumeLocks()
                    
                    ' Forçar refresh do Windows para reconhecer novas partições
                    Try
                        _logService.WriteAsync($"USB | RAW_COPY | REFRESH_INICIO | Forçando refresh do Windows").GetAwaiter().GetResult()
                        
                        ' 1. Rescan do disco via IOCTL
                        Using refreshHandle = NativeMethods.CreateFile(
                            devicePath,
                            NativeMethods.GENERIC_READ Or NativeMethods.GENERIC_WRITE,
                            NativeMethods.FILE_SHARE_READ Or NativeMethods.FILE_SHARE_WRITE,
                            IntPtr.Zero,
                            NativeMethods.OPEN_EXISTING,
                            0,
                            IntPtr.Zero
                        )
                            If Not refreshHandle.IsInvalid Then
                                Dim bytesReturned As UInteger = 0
                                NativeMethods.DeviceIoControl(
                                    refreshHandle,
                                    NativeMethods.IOCTL_DISK_UPDATE_PROPERTIES,
                                    IntPtr.Zero,
                                    0,
                                    IntPtr.Zero,
                                    0,
                                    bytesReturned,
                                    IntPtr.Zero
                                )
                                _logService.WriteAsync($"USB | RAW_COPY | REFRESH_IOCTL | IOCTL_DISK_UPDATE_PROPERTIES executado").GetAwaiter().GetResult()
                            End If
                        End Using
                        
                        ' 2. Forçar refresh via WMI
                        Try
                            Dim searcher As New ManagementObjectSearcher("SELECT * FROM Win32_Volume WHERE DriveType=2")
                            For Each volume As ManagementObject In searcher.Get()
                                volume.InvokeMethod("Refresh", Nothing)
                            Next
                            _logService.WriteAsync($"USB | RAW_COPY | REFRESH_WMI | Volumes WMI refresh executado").GetAwaiter().GetResult()
                        Catch wmiEx As Exception
                            _logService.WriteAsync($"USB | RAW_COPY | REFRESH_WMI_ERRO | {wmiEx.Message}").GetAwaiter().GetResult()
                        End Try
                        
                        ' 3. Aguardar e notificar shell
                        Await Task.Delay(1000, cancellationToken)
                        _logService.WriteAsync($"USB | RAW_COPY | REFRESH_SUCESSO | Refresh do Windows concluído").GetAwaiter().GetResult()
                        
                    Catch ex As OperationCanceledException
                        Throw
                    Catch refreshEx As Exception
                        _logService.WriteAsync($"USB | RAW_COPY | REFRESH_ERRO | {refreshEx.Message}").GetAwaiter().GetResult()
                    End Try
            Catch ex As OperationCanceledException
                _logService.WriteAsync($"USB | RAW_COPY | CANCELADO | Disk {diskNumber}").GetAwaiter().GetResult()
                Throw
            Catch ex As Exception
                _logService.WriteAsync($"USB | RAW_COPY | ERRO | Disk {diskNumber} | {ex.Message}").GetAwaiter().GetResult()
                Throw New InvalidOperationException($"Falha na cópia bruta: {ex.Message}")
            Finally
                ReleaseHeldVolumeLocks()
            End Try
            Return verification
        End Function

        Private Function WaitForDriveReady(rootPath As String, timeoutSeconds As Integer) As Boolean
            Dim driveLetter = rootPath.TrimEnd("\"c)
            For i = 0 To timeoutSeconds - 1
                Try
                    Dim info = New DriveInfo(driveLetter)
                    If info.IsReady Then
                        Return True
                    End If
                Catch
                End Try
                Threading.Thread.Sleep(1000)
            Next
            Return False
        End Function

        Private Function GetLogicalDriveLetterFromDisk(deviceId As String) As String
            Try
                Dim escapedDeviceId = deviceId.Replace("\", "\\")
                Dim partitionQuery = $"ASSOCIATORS OF {{Win32_DiskDrive.DeviceID='{escapedDeviceId}'}} WHERE AssocClass = Win32_DiskDriveToDiskPartition"
                Using partitionSearcher As New ManagementObjectSearcher(partitionQuery)
                    For Each partition As ManagementObject In partitionSearcher.Get()
                        Dim partitionId = Convert.ToString(partition("DeviceID"))
                        Dim escapedPartitionId = partitionId.Replace("\", "\\")
                        Dim logicalQuery = $"ASSOCIATORS OF {{Win32_DiskPartition.DeviceID='{escapedPartitionId}'}} WHERE AssocClass = Win32_LogicalDiskToPartition"
                        Using logicalSearcher As New ManagementObjectSearcher(logicalQuery)
                            For Each logical As ManagementObject In logicalSearcher.Get()
                                Dim letter = Convert.ToString(logical("DeviceID"))
                                If Not String.IsNullOrWhiteSpace(letter) Then
                                    Return letter
                                End If
                            Next
                        End Using
                    Next
                End Using
            Catch ex As Exception
                _logService.WriteAsync($"USB | MAP | ERRO | {ex.Message}").GetAwaiter().GetResult()
            End Try

            Return String.Empty
        End Function

        Private Function GetLogicalDriveLetterFromDiskIndex(diskIndex As Integer) As String
            Try
                Dim diskQuery = $"SELECT DeviceID FROM Win32_DiskDrive WHERE Index={diskIndex}"
                Dim deviceId As String = Nothing
                Using diskSearcher As New ManagementObjectSearcher(diskQuery)
                    For Each disk As ManagementObject In diskSearcher.Get()
                        deviceId = Convert.ToString(disk("DeviceID"))
                        Exit For
                    Next
                End Using

                If String.IsNullOrWhiteSpace(deviceId) Then
                    _logService.WriteAsync($"USB | MAP_INDEX | DISCO_NAO_ENCONTRADO | Disco {diskIndex} | Query={diskQuery}").GetAwaiter().GetResult()
                    Return String.Empty
                End If

                Dim escapedDeviceId = deviceId.Replace("\", "\\")
                Dim partitionQuery = $"ASSOCIATORS OF {{Win32_DiskDrive.DeviceID='{escapedDeviceId}'}} WHERE AssocClass = Win32_DiskDriveToDiskPartition"
                Using partitionSearcher As New ManagementObjectSearcher(partitionQuery)
                    Dim partitions As List(Of ManagementObject)
                    Try
                        partitions = partitionSearcher.Get().Cast(Of ManagementObject)().ToList()
                    Catch ex As ManagementException When ex.ErrorCode = ManagementStatus.NotFound
                        _logService.WriteAsync($"USB | MAP_INDEX | SEM_PARTICOES | Disco {diskIndex} | DeviceId={deviceId} | {DescribeWmiException(ex)} | Query={partitionQuery}").GetAwaiter().GetResult()
                        Return String.Empty
                    End Try

                    _logService.WriteAsync($"USB | MAP_INDEX | PARTITIONS | Disco {diskIndex} | DeviceId={deviceId} | Count={partitions.Count} | Query={partitionQuery}").GetAwaiter().GetResult()
                    
                    ' Se não há partições, o disco está limpo ( após clean )
                    If Not partitions.Any() Then
                        _logService.WriteAsync($"USB | MAP_INDEX | SEM_PARTICOES | Disco {diskIndex} está limpo").GetAwaiter().GetResult()
                        Return String.Empty ' Retornar vazio indica que precisa formatar
                    End If
                    
                    For Each partition As ManagementObject In partitions
                        Try
                            Dim partitionId = Convert.ToString(partition("DeviceID"))
                            Dim partitionDetails = DescribeWmiPartition(partition)
                            If String.IsNullOrWhiteSpace(partitionId) Then
                                _logService.WriteAsync($"USB | MAP_INDEX | PARTICAO_SEM_DEVICEID | Disco {diskIndex} | {partitionDetails}").GetAwaiter().GetResult()
                                Continue For
                            End If

                            Dim escapedPartitionId = partitionId.Replace("\", "\\")
                            Dim logicalQuery = $"ASSOCIATORS OF {{Win32_DiskPartition.DeviceID='{escapedPartitionId}'}} WHERE AssocClass = Win32_LogicalDiskToPartition"
                            Using logicalSearcher As New ManagementObjectSearcher(logicalQuery)
                                Dim logicalDisks As List(Of ManagementObject)
                                Try
                                    logicalDisks = logicalSearcher.Get().Cast(Of ManagementObject)().ToList()
                                Catch ex As ManagementException When ex.ErrorCode = ManagementStatus.NotFound
                                    _logService.WriteAsync($"USB | MAP_INDEX | SEM_VOLUME_LOGICO | Disco {diskIndex} | Partition={partitionId} | {partitionDetails} | {DescribeWmiException(ex)} | Query={logicalQuery}").GetAwaiter().GetResult()
                                    Continue For
                                End Try

                                For Each logical As ManagementObject In logicalDisks
                                    Dim letter = Convert.ToString(logical("DeviceID"))
                                    If Not String.IsNullOrWhiteSpace(letter) Then
                                        _logService.WriteAsync($"USB | MAP_INDEX | SUCESSO | Disco {diskIndex} -> {letter} | Partition={partitionId} | {partitionDetails}").GetAwaiter().GetResult()
                                        Return letter
                                    End If
                                Next

                                If logicalDisks.Count = 0 Then
                                    _logService.WriteAsync($"USB | MAP_INDEX | SEM_VOLUME_LOGICO | Disco {diskIndex} | Partition={partitionId} | {partitionDetails} | Query={logicalQuery}").GetAwaiter().GetResult()
                                Else
                                    _logService.WriteAsync($"USB | MAP_INDEX | VOLUME_SEM_LETRA | Disco {diskIndex} | Partition={partitionId} | {partitionDetails} | Query={logicalQuery}").GetAwaiter().GetResult()
                                End If
                            End Using
                        Catch ex As ManagementException
                            _logService.WriteAsync($"USB | MAP_INDEX | ERRO_PARTICAO | Disco {diskIndex} | {DescribeWmiException(ex)}").GetAwaiter().GetResult()
                        Catch ex As Exception
                            _logService.WriteAsync($"USB | MAP_INDEX | ERRO_PARTICAO | Disco {diskIndex} | {DescribeException(ex)}").GetAwaiter().GetResult()
                            Continue For ' Tentar próxima partição
                        End Try
                    Next
                End Using
            Catch ex As ManagementException
                _logService.WriteAsync($"USB | MAP_INDEX | ERRO | Disco {diskIndex} | {DescribeWmiException(ex)}").GetAwaiter().GetResult()
            Catch ex As Exception
                _logService.WriteAsync($"USB | MAP_INDEX | ERRO | Disco {diskIndex} | {DescribeException(ex)}").GetAwaiter().GetResult()
            End Try

            Return String.Empty
        End Function

        Private Shared Function DescribeWmiPartition(partition As ManagementObject) As String
            If partition Is Nothing Then Return "Partition=N/A"
            Return $"Type={SafeWmiValue(partition, "Type")} | Size={SafeWmiValue(partition, "Size")} | Bootable={SafeWmiValue(partition, "Bootable")} | Primary={SafeWmiValue(partition, "PrimaryPartition")}"
        End Function

        Private Shared Function SafeWmiValue(obj As ManagementObject, propertyName As String) As String
            Try
                Dim value = obj(propertyName)
                Return If(value Is Nothing, "N/A", Convert.ToString(value))
            Catch
                Return "N/A"
            End Try
        End Function

        Private Shared Function DescribeWmiException(ex As ManagementException) As String
            Return $"{DescribeException(ex)} | WMIStatus={ex.ErrorCode}"
        End Function

        Private Shared Function DescribeException(ex As Exception) As String
            Return $"{ex.GetType().Name} | HResult=0x{ex.HResult:X8} | {ex.Message}"
        End Function

        Private Function GetLogicalDriveLetterFromDiskNumberViaVolume(diskIndex As Integer) As String
            Try
                For Each drive In DriveInfo.GetDrives()
                    If Not drive.IsReady Then
                        Continue For
                    End If

                    Dim letter = drive.Name.Substring(0, 2)
                    Dim volumePath = "\\.\" & letter
                    Using handle = CreateFile(volumePath, 0UI, FILE_SHARE_READ Or FILE_SHARE_WRITE Or FILE_SHARE_DELETE, IntPtr.Zero, FileMode.Open, 0, IntPtr.Zero)
                        If handle Is Nothing OrElse handle.IsInvalid Then
                            Continue For
                        End If

                        Dim devNumber As New STORAGE_DEVICE_NUMBER()
                        Dim bytesReturned As Integer = 0
                        Dim ok = DeviceIoControl(handle, IOCTL_STORAGE_GET_DEVICE_NUMBER, IntPtr.Zero, 0, devNumber, Marshal.SizeOf(GetType(STORAGE_DEVICE_NUMBER)), bytesReturned, IntPtr.Zero)
                        If ok AndAlso devNumber.DeviceType = FILE_DEVICE_DISK AndAlso devNumber.DeviceNumber = diskIndex Then
                            _logService.WriteAsync($"USB | MAP_VOL | Disk={diskIndex} | Letter={letter}").GetAwaiter().GetResult()
                            Return letter
                        End If
                    End Using
                Next
            Catch ex As Exception
                _logService.WriteAsync($"USB | MAP_VOL | ERRO | {ex.Message}").GetAwaiter().GetResult()
            End Try

            Return String.Empty
        End Function

        Private Const DIGCF_PRESENT As Integer = &H2
        Private Const DIGCF_DEVICEINTERFACE As Integer = &H10
        Private Shared ReadOnly GUID_DEVINTERFACE_DISK As New Guid("53f56307-b6bf-11d0-94f2-00a0c91efb8b")
        Private Const IOCTL_STORAGE_QUERY_PROPERTY As UInteger = &H2D1400UI
        Private Const IOCTL_STORAGE_GET_DEVICE_NUMBER As UInteger = &H2D1080UI

        Private Enum STORAGE_PROPERTY_ID
            StorageDeviceProperty = 0
        End Enum

        Private Enum STORAGE_QUERY_TYPE
            PropertyStandardQuery = 0
        End Enum

        Private Enum STORAGE_BUS_TYPE
            BusTypeUnknown = 0
            BusTypeScsi = 1
            BusTypeAtapi = 2
            BusTypeAta = 3
            BusType1394 = 4
            BusTypeSsa = 5
            BusTypeFibre = 6
            BusTypeUsb = 7
            BusTypeNvme = 17
        End Enum

        <StructLayout(LayoutKind.Sequential)>
        Private Structure STORAGE_PROPERTY_QUERY
            Public PropertyId As STORAGE_PROPERTY_ID
            Public QueryType As STORAGE_QUERY_TYPE
            <MarshalAs(UnmanagedType.ByValArray, SizeConst:=1)>
            Public AdditionalParameters As Byte()
        End Structure

        <StructLayout(LayoutKind.Sequential)>
        Private Structure STORAGE_DEVICE_DESCRIPTOR
            Public Version As UInteger
            Public Size As UInteger
            Public DeviceType As Byte
            Public DeviceTypeModifier As Byte
            <MarshalAs(UnmanagedType.U1)>
            Public RemovableMedia As Boolean
            <MarshalAs(UnmanagedType.U1)>
            Public CommandQueueing As Boolean
            Public VendorIdOffset As UInteger
            Public ProductIdOffset As UInteger
            Public ProductRevisionOffset As UInteger
            Public SerialNumberOffset As UInteger
            Public BusType As STORAGE_BUS_TYPE
        End Structure

        ' D-03: CdRom1 e PhysicalDrive1 tem o mesmo DeviceNumber; so DeviceType os diferencia.
        Private Const FILE_DEVICE_DISK As Integer = 7

        <StructLayout(LayoutKind.Sequential)>
        Private Structure STORAGE_DEVICE_NUMBER
            Public DeviceType As Integer
            Public DeviceNumber As Integer
            Public PartitionNumber As Integer
        End Structure

        <StructLayout(LayoutKind.Sequential)>
        Private Structure SP_DEVICE_INTERFACE_DATA
            Public cbSize As Integer
            Public InterfaceClassGuid As Guid
            Public Flags As Integer
            Public Reserved As IntPtr
        End Structure

        <DllImport("setupapi.dll", SetLastError:=True, CharSet:=CharSet.Auto)>
        Private Shared Function SetupDiGetClassDevs(ByRef ClassGuid As Guid, Enumerator As IntPtr, hwndParent As IntPtr, Flags As Integer) As IntPtr
        End Function

        <DllImport("setupapi.dll", SetLastError:=True, CharSet:=CharSet.Auto)>
        Private Shared Function SetupDiEnumDeviceInterfaces(DeviceInfoSet As IntPtr, DeviceInfoData As IntPtr, ByRef InterfaceClassGuid As Guid, MemberIndex As Integer, ByRef DeviceInterfaceData As SP_DEVICE_INTERFACE_DATA) As Boolean
        End Function

        <DllImport("setupapi.dll", SetLastError:=True, CharSet:=CharSet.Auto)>
        Private Shared Function SetupDiGetDeviceInterfaceDetail(DeviceInfoSet As IntPtr, ByRef DeviceInterfaceData As SP_DEVICE_INTERFACE_DATA, DeviceInterfaceDetailData As IntPtr, DeviceInterfaceDetailDataSize As Integer, ByRef RequiredSize As Integer, DeviceInfoData As IntPtr) As Boolean
        End Function

        <DllImport("setupapi.dll")>
        Private Shared Function SetupDiDestroyDeviceInfoList(DeviceInfoSet As IntPtr) As Boolean
        End Function

        Private Const GENERIC_READ As UInteger = &H80000000UI
        Private Const FILE_SHARE_READ As UInteger = &H1UI
        Private Const FILE_SHARE_WRITE As UInteger = &H2UI
        Private Const FILE_SHARE_DELETE As UInteger = &H4UI

        <DllImport("kernel32.dll", SetLastError:=True, CharSet:=CharSet.Auto)>
        Private Shared Function CreateFile(lpFileName As String, dwDesiredAccess As UInteger, dwShareMode As UInteger, lpSecurityAttributes As IntPtr, dwCreationDisposition As FileMode, dwFlagsAndAttributes As Integer, hTemplateFile As IntPtr) As SafeFileHandle
        End Function

        <DllImport("kernel32.dll", SetLastError:=True)>
        Private Shared Function DeviceIoControl(hDevice As SafeFileHandle, dwIoControlCode As UInteger, ByRef lpInBuffer As STORAGE_PROPERTY_QUERY, nInBufferSize As Integer, lpOutBuffer As IntPtr, nOutBufferSize As Integer, ByRef lpBytesReturned As Integer, lpOverlapped As IntPtr) As Boolean
        End Function

        <DllImport("kernel32.dll", SetLastError:=True)>
        Private Shared Function DeviceIoControl(hDevice As SafeFileHandle, dwIoControlCode As UInteger, lpInBuffer As IntPtr, nInBufferSize As Integer, ByRef lpOutBuffer As STORAGE_DEVICE_NUMBER, nOutBufferSize As Integer, ByRef lpBytesReturned As Integer, lpOverlapped As IntPtr) As Boolean
        End Function

        Private Function GetUsbDiskNumbersViaSetupApi() As HashSet(Of Integer)
            Dim result As New HashSet(Of Integer)()
            _logService.WriteAsync("USB | SETUPAPI | INICIO").GetAwaiter().GetResult()
            Dim hDevInfo = SetupDiGetClassDevs(GUID_DEVINTERFACE_DISK, IntPtr.Zero, IntPtr.Zero, DIGCF_PRESENT Or DIGCF_DEVICEINTERFACE)
            If hDevInfo = IntPtr.Zero OrElse hDevInfo = New IntPtr(-1) Then
                _logService.WriteAsync("USB | SETUPAPI | ERRO | Handle invalido").GetAwaiter().GetResult()
                Return result
            End If

            Try
                Dim interfaceData As New SP_DEVICE_INTERFACE_DATA()
                interfaceData.cbSize = Marshal.SizeOf(interfaceData)
                Dim index As Integer = 0
                While SetupDiEnumDeviceInterfaces(hDevInfo, IntPtr.Zero, GUID_DEVINTERFACE_DISK, index, interfaceData)
                    Dim requiredSize As Integer = 0
                    SetupDiGetDeviceInterfaceDetail(hDevInfo, interfaceData, IntPtr.Zero, 0, requiredSize, IntPtr.Zero)
                    If requiredSize <= 0 Then
                        index += 1
                        Continue While
                    End If

                    Dim detailBuffer As IntPtr = Marshal.AllocHGlobal(requiredSize)
                    Try
                        Marshal.WriteInt32(detailBuffer, If(IntPtr.Size = 8, 8, 6))
                        If SetupDiGetDeviceInterfaceDetail(hDevInfo, interfaceData, detailBuffer, requiredSize, requiredSize, IntPtr.Zero) Then
                            Dim devicePath = ReadDevicePath(detailBuffer)
                            If Not String.IsNullOrWhiteSpace(devicePath) Then
                                Dim busType = GetBusType(devicePath)
                                Dim diskNumber As Integer
                                Dim hasNumber = TryGetDeviceNumber(devicePath, diskNumber)
                                If busType = STORAGE_BUS_TYPE.BusTypeUnknown AndAlso devicePath.IndexOf("usbstor#", StringComparison.OrdinalIgnoreCase) >= 0 Then
                                    busType = STORAGE_BUS_TYPE.BusTypeUsb
                                End If
                                _logService.WriteAsync($"USB | SETUPAPI | DEVICE | Path={devicePath} | BusType={busType} | Disk={If(hasNumber, diskNumber.ToString(), "N/A")}").GetAwaiter().GetResult()
                                If busType = STORAGE_BUS_TYPE.BusTypeUsb AndAlso hasNumber Then
                                    result.Add(diskNumber)
                                End If
                            End If
                        End If
                    Finally
                        Marshal.FreeHGlobal(detailBuffer)
                    End Try
                    index += 1
                End While
            Finally
                SetupDiDestroyDeviceInfoList(hDevInfo)
            End Try

            _logService.WriteAsync($"USB | SETUPAPI | OK | Discos={result.Count}").GetAwaiter().GetResult()
            Return result
        End Function

        Private Function ReadDevicePath(detailBuffer As IntPtr) As String
            Dim offsets = New Integer() {If(IntPtr.Size = 8, 8, 4), 4, 8}
            For Each offset In offsets
                Dim candidate = Marshal.PtrToStringUni(IntPtr.Add(detailBuffer, offset))
                If Not String.IsNullOrWhiteSpace(candidate) Then
                    If candidate.Contains("\\?\") OrElse candidate.IndexOf("PhysicalDrive", StringComparison.OrdinalIgnoreCase) >= 0 Then
                        Return candidate
                    End If
                End If
            Next

            Dim fallback = Marshal.PtrToStringAuto(IntPtr.Add(detailBuffer, If(IntPtr.Size = 8, 8, 4)))
            Return If(String.IsNullOrWhiteSpace(fallback), String.Empty, fallback)
        End Function

        Private Function GetBusType(devicePath As String) As STORAGE_BUS_TYPE
            Using handle = CreateFile(devicePath, 0UI, FILE_SHARE_READ Or FILE_SHARE_WRITE Or FILE_SHARE_DELETE, IntPtr.Zero, FileMode.Open, 0, IntPtr.Zero)
                If handle Is Nothing OrElse handle.IsInvalid Then
                    Dim handleErr = Marshal.GetLastWin32Error()
                    _logService.WriteAsync($"USB | SETUPAPI | BUS | HANDLE_ERRO | {handleErr}").GetAwaiter().GetResult()
                    Return STORAGE_BUS_TYPE.BusTypeUnknown
                End If

                Dim query As New STORAGE_PROPERTY_QUERY With {
                    .PropertyId = STORAGE_PROPERTY_ID.StorageDeviceProperty,
                    .QueryType = STORAGE_QUERY_TYPE.PropertyStandardQuery,
                    .AdditionalParameters = New Byte(0) {}
                }

                Dim outSize = Marshal.SizeOf(GetType(STORAGE_DEVICE_DESCRIPTOR)) + 512
                Dim outBuffer = Marshal.AllocHGlobal(outSize)
                Try
                    Dim bytesReturned As Integer = 0
                    Dim ok = DeviceIoControl(handle, IOCTL_STORAGE_QUERY_PROPERTY, query, Marshal.SizeOf(GetType(STORAGE_PROPERTY_QUERY)), outBuffer, outSize, bytesReturned, IntPtr.Zero)
                    If ok Then
                        Dim descriptor = Marshal.PtrToStructure(Of STORAGE_DEVICE_DESCRIPTOR)(outBuffer)
                        Return descriptor.BusType
                    End If
                    Dim ioctlErr = Marshal.GetLastWin32Error()
                    _logService.WriteAsync($"USB | SETUPAPI | BUS | IOCTL_ERRO | {ioctlErr}").GetAwaiter().GetResult()
                Finally
                    Marshal.FreeHGlobal(outBuffer)
                End Try
            End Using

            Return STORAGE_BUS_TYPE.BusTypeUnknown
        End Function

        Private Function TryGetDeviceNumber(devicePath As String, ByRef diskNumber As Integer) As Boolean
            diskNumber = -1
            Using handle = CreateFile(devicePath, 0UI, FILE_SHARE_READ Or FILE_SHARE_WRITE Or FILE_SHARE_DELETE, IntPtr.Zero, FileMode.Open, 0, IntPtr.Zero)
                If handle Is Nothing OrElse handle.IsInvalid Then
                    Dim handleErr = Marshal.GetLastWin32Error()
                    _logService.WriteAsync($"USB | SETUPAPI | DEVNUM | HANDLE_ERRO | {handleErr}").GetAwaiter().GetResult()
                    Return False
                End If

                Dim devNumber As New STORAGE_DEVICE_NUMBER()
                Dim bytesReturned As Integer = 0
                Dim ok = DeviceIoControl(handle, IOCTL_STORAGE_GET_DEVICE_NUMBER, IntPtr.Zero, 0, devNumber, Marshal.SizeOf(GetType(STORAGE_DEVICE_NUMBER)), bytesReturned, IntPtr.Zero)
                If ok Then
                    diskNumber = devNumber.DeviceNumber
                    Return True
                End If
                Dim ioctlErr = Marshal.GetLastWin32Error()
                _logService.WriteAsync($"USB | SETUPAPI | DEVNUM | IOCTL_ERRO | {ioctlErr}").GetAwaiter().GetResult()
            End Using

            Return False
        End Function

        Private Function GetDiskIndexFromDriveLetter(driveLetter As String) As Integer
            Dim query = $"ASSOCIATORS OF {{Win32_LogicalDisk.DeviceID='{driveLetter}'}} WHERE AssocClass = Win32_LogicalDiskToPartition"
            Using searcher As New ManagementObjectSearcher(query)
                For Each partition As ManagementObject In searcher.Get()
                    Dim diskQuery = $"ASSOCIATORS OF {{Win32_DiskPartition.DeviceID='{partition("DeviceID")}'}} WHERE AssocClass = Win32_DiskDriveToDiskPartition"
                    Using diskSearcher As New ManagementObjectSearcher(diskQuery)
                        For Each disk As ManagementObject In diskSearcher.Get()
                            Return Convert.ToInt32(disk("Index"))
                        Next
                    End Using
                Next
            End Using

            Return -1
        End Function

        Private Function GetDiskModelSizeAndInterfaceByIndex(index As Integer, ByRef model As String, ByRef sizeBytes As Long) As String
            Dim interfaceType As String = String.Empty
            Using searcher As New ManagementObjectSearcher($"SELECT * FROM Win32_DiskDrive WHERE Index={index}")
                For Each disk As ManagementObject In searcher.Get()
                    Dim m = Convert.ToString(disk("Model"))
                    If Not String.IsNullOrWhiteSpace(m) Then
                        model = m
                    End If
                    Dim sizeValue = Convert.ToString(disk("Size"))
                    If Not String.IsNullOrWhiteSpace(sizeValue) Then
                        Long.TryParse(sizeValue, sizeBytes)
                    End If
                    interfaceType = Convert.ToString(disk("InterfaceType"))
                    Exit For
                Next
            End Using
            Return interfaceType
        End Function

        Private Function TryGetDiskInfoByIndex(index As Integer, ByRef model As String, ByRef sizeBytes As Long, ByRef deviceId As String) As Boolean
            Using searcher As New ManagementObjectSearcher($"SELECT * FROM Win32_DiskDrive WHERE Index={index}")
                For Each disk As ManagementObject In searcher.Get()
                    deviceId = Convert.ToString(disk("DeviceID"))
                    Dim m = Convert.ToString(disk("Model"))
                    If Not String.IsNullOrWhiteSpace(m) Then
                        model = m
                    End If
                    Dim sizeValue = Convert.ToString(disk("Size"))
                    If Not String.IsNullOrWhiteSpace(sizeValue) Then
                        Long.TryParse(sizeValue, sizeBytes)
                    End If
                    Return True
                Next
            End Using

            Return False
        End Function

        Private Function GetAvailableDriveLetter() As String
            Dim used As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
            For Each d In DriveInfo.GetDrives()
                used.Add(d.Name.Substring(0, 1))
            Next

            Dim letters = "DEFGHIJKLMNOPQRSTUVWXYZ"
            For i As Integer = 0 To letters.Length - 1
                Dim letter = letters.Substring(i, 1)
                If Not used.Contains(letter) Then
                    Return letter & ":"
                End If
            Next

            Return String.Empty
        End Function

        Private Class RegistryValueSnapshot
            Public Property Exists As Boolean
            Public Property Value As Object
            Public Property Kind As RegistryValueKind
        End Class

        Private Class AutoplayBackup
            Public ReadOnly Values As New Dictionary(Of String, RegistryValueSnapshot)(StringComparer.OrdinalIgnoreCase)
        End Class

        Private Function DisableAutoplay() As AutoplayBackup
            Dim backup As New AutoplayBackup()
            Try
                SnapshotValue(backup, "Software\Microsoft\Windows\CurrentVersion\Policies\Explorer", "NoViewAutoPlay")
                SnapshotValue(backup, "Software\Microsoft\Windows\CurrentVersion\Explorer\AutoplayHandlers", "DisableAutoplay")
                SnapshotValue(backup, "Software\Microsoft\Windows\CurrentVersion\Notifications\Settings\Windows.Explorer", "Enabled")

                SetDwordValue("Software\Microsoft\Windows\CurrentVersion\Policies\Explorer", "NoViewAutoPlay", &HFF)
                SetDwordValue("Software\Microsoft\Windows\CurrentVersion\Explorer\AutoplayHandlers", "DisableAutoplay", 1)
                SetDwordValue("Software\Microsoft\Windows\CurrentVersion\Notifications\Settings\Windows.Explorer", "Enabled", 0)

                Dim handlersPath = "Software\Microsoft\Windows\CurrentVersion\Explorer\AutoplayHandlers\UserChosenExecuteHandlers"
                Dim mediaTypes = New String() {"StorageOnArrival", "Volume", "CDBurnOnArrival", "DVDMovieOnArrival", "BluRayOnArrival"}
                For Each mediaName As String In mediaTypes
                    SnapshotValue(backup, handlersPath, mediaName)
                    SetStringValue(handlersPath, mediaName, "MSTakeNoAction")
                Next

                _logService.WriteAsync("USB | AUTOPLAY | DESATIVADO").GetAwaiter().GetResult()
                Return backup
            Catch ex As UnauthorizedAccessException
                _logService.WriteAsync($"USB | AUTOPLAY | ERRO | {ex.Message}").GetAwaiter().GetResult()
                Return Nothing
            Catch ex As Exception
                _logService.WriteAsync($"USB | AUTOPLAY | ERRO | {ex.Message}").GetAwaiter().GetResult()
                Return Nothing
            End Try
        End Function

        Private Sub RestoreAutoplay(backup As AutoplayBackup)
            Try
                For Each entry In backup.Values
                    Dim parts = entry.Key.Split("|"c)
                    If parts.Length <> 2 Then
                        Continue For
                    End If
                    Dim path = parts(0)
                    Dim name = parts(1)
                    Using key = Registry.CurrentUser.CreateSubKey(path, True)
                        If key Is Nothing Then
                            Continue For
                        End If
                        Dim snapshot = entry.Value
                        If snapshot.Exists Then
                            key.SetValue(name, snapshot.Value, snapshot.Kind)
                        Else
                            Try
                                key.DeleteValue(name, False)
                            Catch
                            End Try
                        End If
                    End Using
                Next
                _logService.WriteAsync("USB | AUTOPLAY | RESTAURADO").GetAwaiter().GetResult()
            Catch ex As Exception
                _logService.WriteAsync($"USB | AUTOPLAY | RESTAURAR_ERRO | {ex.Message}").GetAwaiter().GetResult()
            End Try
        End Sub

        Private Sub SnapshotValue(backup As AutoplayBackup, keyPath As String, name As String)
            Using key = Registry.CurrentUser.OpenSubKey(keyPath, False)
                Dim snapshot As New RegistryValueSnapshot()
                If key IsNot Nothing Then
                    Dim value = key.GetValue(name, Nothing)
                    If value IsNot Nothing Then
                        snapshot.Exists = True
                        snapshot.Value = value
                        snapshot.Kind = key.GetValueKind(name)
                    Else
                        snapshot.Exists = False
                    End If
                Else
                    snapshot.Exists = False
                End If
                backup.Values($"{keyPath}|{name}") = snapshot
            End Using
        End Sub

        Private Sub SetDwordValue(keyPath As String, name As String, value As Integer)
            Using key = Registry.CurrentUser.CreateSubKey(keyPath, True)
                If key IsNot Nothing Then
                    key.SetValue(name, value, RegistryValueKind.DWord)
                End If
            End Using
        End Sub

        Private Sub SetStringValue(keyPath As String, name As String, value As String)
            Using key = Registry.CurrentUser.CreateSubKey(keyPath, True)
                If key IsNot Nothing Then
                    key.SetValue(name, value, RegistryValueKind.String)
                End If
            End Using
        End Sub

        Private Sub EjectDrive(driveLetter As String)
            If String.IsNullOrWhiteSpace(driveLetter) Then
                Throw New InvalidOperationException("Letra de unidade invalida para ejetar.")
            End If

            Dim command = $"$d=(New-Object -ComObject Shell.Application).NameSpace(17).ParseName('{driveLetter}\'); $d.InvokeVerb('Eject')"
            Dim startInfo As New ProcessStartInfo(GetPowerShellExe(), $"-NoProfile -Command ""{command}""") With {
                .UseShellExecute = False,
                .CreateNoWindow = True
            }
            Using ejectProcess As Process = Process.Start(startInfo)
                If ejectProcess IsNot Nothing Then
                    ejectProcess.WaitForExit()
                End If
            End Using
        End Sub

        Private Sub UpdateDriveInfo()
            If _driveCombo.Items.Count = 0 Then
                _infoLabel.Text = "USB: Nenhum dispositivo removível detectado."
                _statusLabel.Text = "Nenhum dispositivo USB removível detectado."
                Return
            End If

            Dim drive = TryCast(_driveCombo.SelectedItem, UsbDriveOption)
            If drive Is Nothing Then
                _infoLabel.Text = "USB: Nenhum dispositivo removível detectado."
                _statusLabel.Text = "Nenhum dispositivo USB removível detectado."
                Return
            End If

            If String.IsNullOrWhiteSpace(drive.DriveLetter) Then
                Dim sizeGb = drive.SizeBytes / (1024.0 * 1024.0 * 1024.0)
                Dim diskText = If(drive.DiskNumber < 0, "USB", $"Disk {drive.DiskNumber}")
                _infoLabel.Text = $"USB: {diskText} | Modelo: {drive.Model} | {sizeGb:N1} GB | Sem letra"
                Return
            End If

            Dim info = drive.DriveInfo
            Dim totalGb = info.TotalSize / (1024.0 * 1024.0 * 1024.0)
            Dim freeGb = info.AvailableFreeSpace / (1024.0 * 1024.0 * 1024.0)
            Dim label = If(String.IsNullOrWhiteSpace(info.VolumeLabel), "Sem rótulo", info.VolumeLabel)
            _infoLabel.Text = $"USB: {info.Name} | Rótulo: {label} | Sistema: {info.DriveFormat} | Livre: {freeGb:N1} GB de {totalGb:N1} GB"
        End Sub

        Private Shared Function FormatBytes(bytes As Long) As String
            If bytes <= 0 Then Return "tamanho desconhecido"
            Dim value = CDbl(bytes)
            Dim units = {"B", "KB", "MB", "GB", "TB"}
            Dim index = 0
            While value >= 1024.0 AndAlso index < units.Length - 1
                value /= 1024.0
                index += 1
            End While
            If index = 0 Then
                Return $"{bytes:N0} {units(index)}"
            End If
            Return $"{value:N1} {units(index)}"
        End Function

        Private Function GetUsbCapacityBytes(drive As UsbDriveOption) As Long
            If drive Is Nothing Then
                Return 0
            End If

            If Not String.IsNullOrWhiteSpace(drive.RootPath) AndAlso Directory.Exists(drive.RootPath) Then
                Try
                    Return New DriveInfo(drive.RootPath).TotalSize
                Catch
                End Try
            End If

            If drive.SizeBytes > 0 Then
                Return drive.SizeBytes
            End If

            If drive.DiskNumber >= 0 Then
                Return GetDiskSizeBytes(drive.DiskNumber)
            End If

            Return 0
        End Function

        Private Function GetUsbFreeBytes(drive As UsbDriveOption) As Long
            If drive Is Nothing OrElse String.IsNullOrWhiteSpace(drive.RootPath) OrElse Not Directory.Exists(drive.RootPath) Then
                Return 0
            End If

            Try
                Return New DriveInfo(drive.RootPath).AvailableFreeSpace
            Catch
                Return 0
            End Try
        End Function

        Private Function GetUsbPhysicalCapacityBytes(drive As UsbDriveOption) As Long
            If drive Is Nothing Then
                Return 0
            End If

            If drive.SizeBytes > 0 Then
                Return drive.SizeBytes
            End If

            If drive.DiskNumber >= 0 Then
                Return GetDiskSizeBytes(drive.DiskNumber)
            End If

            Return GetUsbCapacityBytes(drive)
        End Function

        Private Function EstimateLogicalIsoRequiredBytes(isoPath As String, method As UsbBootCreationMethod) As Long
            Dim logicalBytes = GetIsoFileEntries(isoPath).Sum(Function(entry) entry.Size)
            Dim filesystemOverhead = Math.Max(64L * 1024L * 1024L, logicalBytes \ 20)
            Dim generatedBootArtifacts As Long
            Select Case method
                Case UsbBootCreationMethod.LegacyBios
                    generatedBootArtifacts = 16L * 1024L * 1024L
                Case UsbBootCreationMethod.UefiNtfs
                    generatedBootArtifacts = 128L * 1024L * 1024L
                Case Else
                    generatedBootArtifacts = 8L * 1024L * 1024L
            End Select
            Return logicalBytes + filesystemOverhead + generatedBootArtifacts
        End Function

        Private Async Function EnsureUsbCapacityForLogicalIsoAsync(isoPath As String, drive As UsbDriveOption, method As UsbBootCreationMethod) As Task(Of Boolean)
            Dim requiredBytes = EstimateLogicalIsoRequiredBytes(isoPath, method)
            Dim capacityBytes = GetUsbPhysicalCapacityBytes(drive)
            Dim allow = capacityBytes <= 0 OrElse requiredBytes <= capacityBytes
            Await _logService.WriteAsync($"USB | CAPACITY | ISO_LOGICAL | Method={method} | Image={Path.GetFileName(isoPath)} | RequiredBytes={requiredBytes} | UsbBytes={capacityBytes} | Decision={If(allow, "ALLOW", "BLOCK")}")

            If allow Then
                Return True
            End If

            _statusLabel.Text = "USB insuficiente para gravar ISO."
            MessageBox.Show($"USB insuficiente: imagem requer {FormatBytes(requiredBytes)}, dispositivo possui {FormatBytes(capacityBytes)}.",
                            "USB Boot",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Error)
            Return False
        End Function

        ''' <summary>
        ''' Motivo para formatar antes de copiar o TIBX num USB que ja tem letra, ou Nothing se o volume serve.
        ''' </summary>
        Private Const TibxUsbFileSystem As String = TibxVolumeCheck.RequiredFileSystem

        Private Shared Function GetTibxFormatReason(drive As UsbDriveOption, tibxBytes As Long) As String
            Try
                Dim info As New DriveInfo(drive.RootPath)
                If Not info.IsReady Then Return TibxVolumeCheck.FormatReason(False, Nothing, 0, 0, drive.SizeBytes, tibxBytes)
                Return TibxVolumeCheck.FormatReason(True, info.DriveFormat, info.AvailableFreeSpace, info.TotalSize, drive.SizeBytes, tibxBytes)
            Catch ex As Exception
                Return $"Nao foi possivel ler o volume atual do USB ({ex.Message})."
            End Try
        End Function

        Private Async Function EnsureUsbCapacityForTibxAsync(tibxPath As String, drive As UsbDriveOption) As Task(Of Boolean)
            Dim fileBytes = New FileInfo(tibxPath).Length
            Dim freeBytes = GetUsbFreeBytes(drive)
            Dim capacityBytes = GetUsbCapacityBytes(drive)
            Dim usableBytes = If(freeBytes > 0, freeBytes, capacityBytes)
            Dim allow = usableBytes <= 0 OrElse fileBytes <= usableBytes
            Dim filesystem = String.Empty
            If Not String.IsNullOrWhiteSpace(drive.RootPath) AndAlso Directory.Exists(drive.RootPath) Then
                Try
                    filesystem = New DriveInfo(drive.RootPath).DriveFormat
                Catch
                    filesystem = "desconhecido"
                End Try
            End If

            Await _logService.WriteAsync($"USB | CAPACITY | TIBX | File={Path.GetFileName(tibxPath)} | FileBytes={fileBytes} | FreeBytes={freeBytes} | UsbBytes={capacityBytes} | FileSystem={filesystem} | Decision={If(allow, "ALLOW", "BLOCK")}")

            If allow Then
                Return True
            End If

            _statusLabel.Text = "USB insuficiente para copiar TIBX."
            MessageBox.Show($"USB insuficiente para copiar TIBX: arquivo {FormatBytes(fileBytes)}, livre {FormatBytes(usableBytes)}.",
                            "USB Boot",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Error)
            Return False
        End Function

        ''' <summary>Copia com o percentual real na barra e na status ("{statusText} NN%").</summary>
        Private Async Function CopyFileWithProgressAsync(sourcePath As String, destinationPath As String, statusText As String, cancellationToken As CancellationToken) As Task
            Dim bufferSize = 1024 * 1024
            Using sourceStream As New FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize, True)
                Using destStream As New FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize, True)
                    Dim total = sourceStream.Length
                    Dim buffer(bufferSize - 1) As Byte
                    Dim read As Integer
                    Dim totalRead As Long = 0
                    Dim lastPercent = -1
                    lastPercent = ShowProgressPercent(statusText, 0, lastPercent)

                    Do
                        cancellationToken.ThrowIfCancellationRequested()
                        read = Await sourceStream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken)
                        If read = 0 Then
                            Exit Do
                        End If
                        Await destStream.WriteAsync(buffer.AsMemory(0, read), cancellationToken)
                        totalRead += read
                        lastPercent = ShowProgressPercent(statusText, If(total > 0, CInt(Math.Min(100, (totalRead * 100L) \ total)), 100), lastPercent)
                    Loop
                End Using
            End Using
        End Function

        ''' <summary>
        ''' SHA-256 do arquivo com o percentual real de leitura na barra e na status ("{statusText} NN%").
        ''' Se a barra estava animada (etapa sem percentual), volta a animar ao terminar.
        ''' </summary>
        Private Async Function ComputeSha256WithProgressAsync(filePath As String, statusText As String, cancellationToken As CancellationToken) As Task(Of String)
            Const bufferSize = 1024 * 1024
            Dim wasMarquee = _progressBar.Style = ProgressBarStyle.Marquee
            Try
                Using sha256 = System.Security.Cryptography.IncrementalHash.CreateHash(System.Security.Cryptography.HashAlgorithmName.SHA256)
                    Using stream = New FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize, True)
                        Dim total = stream.Length
                        Dim buffer(bufferSize - 1) As Byte
                        Dim totalRead As Long = 0
                        Dim lastPercent = -1
                        lastPercent = ShowProgressPercent(statusText, 0, lastPercent)
                        Do
                            Dim read = Await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken)
                            If read = 0 Then
                                Exit Do
                            End If
                            sha256.AppendData(buffer, 0, read)
                            totalRead += read
                            lastPercent = ShowProgressPercent(statusText, If(total > 0, CInt(Math.Min(100, (totalRead * 100L) \ total)), 100), lastPercent)
                        Loop
                        Return Convert.ToHexString(sha256.GetHashAndReset()).ToLowerInvariant()
                    End Using
                End Using
            Catch ex As Exception
                _logService.WriteAsync($"SHA256 | ERRO | {filePath} | {ex.Message}").GetAwaiter().GetResult()
                Throw
            Finally
                If wasMarquee Then
                    SetProgressMarquee(True)
                End If
            End Try
        End Function

        ''' <summary>Atualiza barra e status so quando o percentual muda. Retorna o percentual exibido.</summary>
        Private Function ShowProgressPercent(statusText As String, percent As Integer, lastPercent As Integer) As Integer
            If percent = lastPercent Then Return lastPercent
            SetProgress(percent)
            _statusLabel.Text = $"{statusText} {percent}%"
            Return percent
        End Function

        ''' <summary>
        ''' Passo 1 da copia do TIBX: calcula o SHA-256 do arquivo em disco e confere com a referencia
        ''' (validada no download ou do catalogo). Retorna o SHA calculado; Nothing se divergiu (copia abortada).
        ''' Sem referencia, segue com o SHA calculado.
        ''' </summary>
        Private Async Function ValidateTibxBeforeCopyAsync(item As CacheItem, cancellationToken As CancellationToken) As Task(Of String)
            Dim expectedSha256 = GetExpectedImageSha256(item)
            Dim actualSha256 As String
            Try
                actualSha256 = Await ComputeSha256WithProgressAsync(item.FullPath, "Validando SHA-256 do TIBX antes da cópia...", cancellationToken)
            Finally
                SetProgressMarquee(False)
            End Try

            If String.IsNullOrWhiteSpace(expectedSha256) Then
                Await _logService.WriteAsync($"USB | COPIA_TIBX | SHA256_ORIGEM | SEM_REFERENCIA | {item.DisplayName} | SHA256={actualSha256}")
                Return actualSha256
            End If

            If Not String.Equals(expectedSha256, actualSha256, StringComparison.OrdinalIgnoreCase) Then
                Await _logService.WriteAsync($"USB | COPIA_TIBX | SHA256_ORIGEM | DIVERGENTE | {item.DisplayName} | Esperado={expectedSha256} | Calculado={actualSha256}")
                _statusLabel.Text = "TIBX em disco com SHA-256 divergente. Cópia cancelada."
                MessageBox.Show($"O SHA-256 do TIBX em disco não confere com o validado no download/catálogo. A cópia foi cancelada.{Environment.NewLine}{Environment.NewLine}" &
                                $"Esperado: {expectedSha256}{Environment.NewLine}Calculado: {actualSha256}{Environment.NewLine}{Environment.NewLine}" &
                                "Baixe a imagem novamente na tela Downloads.",
                                "USB Boot", MessageBoxButtons.OK, MessageBoxIcon.Error)
                Return Nothing
            End If

            Await _logService.WriteAsync($"USB | COPIA_TIBX | SHA256_ORIGEM | OK | {item.DisplayName} | SHA256={actualSha256}")
            Return actualSha256
        End Function

        ''' <summary>
        ''' SHA-256 de referencia da imagem: o gravado no download_status quando o download foi validado;
        ''' senao, o do catalogo. Vazio se nenhum dos dois existir.
        ''' </summary>
        Private Function GetExpectedImageSha256(item As CacheItem) As String
            Dim statusMap = LoadDownloadStatusMap()
            Dim catalogName = FindCatalogNameForPath(statusMap, item.FullPath)
            Dim entry As DownloadStatusEntry = Nothing
            If Not String.IsNullOrWhiteSpace(catalogName) AndAlso statusMap.TryGetValue(catalogName, entry) AndAlso entry IsNot Nothing AndAlso
               entry.Downloaded AndAlso entry.Md5Valid.GetValueOrDefault() AndAlso Not String.IsNullOrWhiteSpace(entry.ImageSha256) Then
                Return entry.ImageSha256.Trim()
            End If
            Try
                Dim reason As String = Nothing
                Dim catalogItem = _usbWritePolicyResolver.FindCatalogItem(_catalogService.LoadAllCatalogs(AppPaths.DataDir), catalogName,
                                                                          Path.GetFileName(item.FullPath), New FileInfo(item.FullPath).Length, reason)
                If catalogItem IsNot Nothing AndAlso Not String.IsNullOrWhiteSpace(catalogItem.Sha256) Then
                    Return catalogItem.Sha256.Trim()
                End If
            Catch ex As Exception
                _logService.WriteAsync($"USB | COPIA_TIBX | SHA256_REFERENCIA | ERRO_CATALOGO | {item.DisplayName} | {ex.Message}").GetAwaiter().GetResult()
            End Try
            Return String.Empty
        End Function

        ''' <summary>Arquivo com o mesmo nome ja no USB: compara o SHA dele com o SHA validado no passo 1.</summary>
        Private Async Function ShouldCopyExistingTibxAsync(sourceSha256 As String, destinationPath As String, cancellationToken As CancellationToken) As Task(Of Boolean)
            If Not File.Exists(destinationPath) Then
                Return True
            End If

            Dim settings = _settingsService.Load()
            If Not settings.AutoVerifyExistingTibxBeforeCopy Then
                Await _logService.WriteAsync($"USB | COPIA_TIBX | EXISTE | VERIFICACAO_DESABILITADA | {destinationPath}")
                Return True
            End If

            Await _logService.WriteAsync($"USB | COPIA_TIBX | EXISTE | {destinationPath}")
            Dim verifyExisting = MessageBox.Show($"Ja existe um arquivo TIBX com o mesmo nome no USB:{Environment.NewLine}{Path.GetFileName(destinationPath)}{Environment.NewLine}{Environment.NewLine}Deseja verificar se ele ja esta atualizado calculando o SHA-256 do arquivo no USB e comparando com o cache?",
                                                 "USB Boot",
                                                 MessageBoxButtons.YesNo,
                                                 MessageBoxIcon.Question)

            If verifyExisting <> DialogResult.Yes Then
                Await _logService.WriteAsync($"USB | COPIA_TIBX | EXISTE | VERIFICACAO_IGNORADA | {destinationPath}")
                Return True
            End If

            Try
                Dim targetSha256 = Await ComputeSha256WithProgressAsync(destinationPath, "Verificando TIBX existente no USB...", cancellationToken)
                Dim match = String.Equals(sourceSha256, targetSha256, StringComparison.OrdinalIgnoreCase)
                Await _logService.WriteAsync($"USB | COPIA_TIBX | EXISTE | SHA256_{If(match, "OK", "DIVERGENTE")} | {destinationPath}")

                If match Then
                    _statusLabel.Text = "TIBX existente no USB ja esta atualizado."
                    MessageBox.Show("O TIBX existente no USB ja esta atualizado. A copia foi dispensada.", "USB Boot", MessageBoxButtons.OK, MessageBoxIcon.Information)
                    Return False
                End If

                _statusLabel.Text = "TIBX existente no USB esta diferente. Copia sera atualizada."
                Return True
            Finally
                SetProgressMarquee(False)
            End Try
        End Function

        Private Sub SetProgress(value As Integer)
            _progressBar.Style = ProgressBarStyle.Continuous
            _progressBar.Value = Math.Max(0, Math.Min(100, value))
        End Sub

        Private Sub SetProgressMarquee(enabled As Boolean)
            If enabled Then
                _progressBar.Style = ProgressBarStyle.Marquee
                _progressBar.MarqueeAnimationSpeed = 30
            Else
                _progressBar.MarqueeAnimationSpeed = 0
                _progressBar.Style = ProgressBarStyle.Continuous
                _progressBar.Value = 0
            End If
        End Sub

        Private Sub SetBusyIndicator(isBusy As Boolean)
            _busyGif.Visible = isBusy
        End Sub

        Private Async Function FetchMd5Async(md5Url As String) As Task(Of String)
            Dim content = Await GetStringWithRedirectAsync(md5Url, 0)
            Return ParseMd5(content)
        End Function

        Private Function ParseMd5(content As String) As String
            If String.IsNullOrWhiteSpace(content) Then
                Return String.Empty
            End If

            Dim tokens = content.Trim().Split({" "c, ControlChars.Tab, ControlChars.Cr, ControlChars.Lf}, StringSplitOptions.RemoveEmptyEntries)
            If tokens.Length = 0 Then
                Return String.Empty
            End If

            Return tokens(0).Trim()
        End Function

        Private Async Function GetStringWithRedirectAsync(url As String, depth As Integer) As Task(Of String)
            If depth > 3 Then
                Throw New InvalidOperationException("Numero maximo de redirecionamentos excedido.")
            End If

            Dim response = Await _md5Client.GetAsync(url)
            response.EnsureSuccessStatusCode()

            Dim mediaType = response.Content.Headers.ContentType?.MediaType
            Dim content = Await response.Content.ReadAsStringAsync()
            If Not String.IsNullOrWhiteSpace(mediaType) AndAlso mediaType.StartsWith("text/html", StringComparison.OrdinalIgnoreCase) Then
                Dim redirectedUrl = TryExtractRedirectUrl(content)
                If Not String.IsNullOrWhiteSpace(redirectedUrl) Then
                    Return Await GetStringWithRedirectAsync(redirectedUrl, depth + 1)
                End If
            End If

            Return content
        End Function

        Private Function TryExtractRedirectUrl(html As String) As String
            If String.IsNullOrWhiteSpace(html) Then
                Return String.Empty
            End If

            Dim patterns = New String() {
                "window\.location\.replace\(['""](?<url>[^'""]+)['""]\)",
                "redirect\(['""](?<url>[^'""]+)['""]\)",
                "href=['""](?<url>https?://[^'""]*.(?:iso|img|zip|exe|tibx|bin)[^'""]*)['""]",
                "redirect\(\""(?<url>[^\""]+)\""\)",
                "window\.location\.replace\(\""(?<url>[^\""]+)\""\)"
            }

            For Each pattern In patterns
                Dim match = Regex.Match(html, pattern, RegexOptions.IgnoreCase)
                If match.Success Then
                    Dim value = match.Groups("url").Value
                    If Not String.IsNullOrWhiteSpace(value) Then
                        Return value
                    End If
                End If
            Next

            Return String.Empty
        End Function

        Private Function LoadDownloadStatusMap() As Dictionary(Of String, DownloadStatusEntry)
            Dim statusPath = AppPaths.DownloadStatusPath
            If Not File.Exists(statusPath) Then
                Return New Dictionary(Of String, DownloadStatusEntry)(StringComparer.OrdinalIgnoreCase)
            End If

            Dim json = File.ReadAllText(statusPath)
            Dim options = New System.Text.Json.JsonSerializerOptions() With {
                .PropertyNameCaseInsensitive = True
            }
            Dim data = System.Text.Json.JsonSerializer.Deserialize(Of Dictionary(Of String, DownloadStatusEntry))(json, options)
            If data Is Nothing Then
                Return New Dictionary(Of String, DownloadStatusEntry)(StringComparer.OrdinalIgnoreCase)
            End If

            Return New Dictionary(Of String, DownloadStatusEntry)(data, StringComparer.OrdinalIgnoreCase)
        End Function

        Private Sub SaveDownloadStatusMap(data As Dictionary(Of String, DownloadStatusEntry))
            Dim statusPath = AppPaths.DownloadStatusPath
            Dim options = New System.Text.Json.JsonSerializerOptions() With {.WriteIndented = True}
            File.WriteAllText(statusPath, System.Text.Json.JsonSerializer.Serialize(data, options))
        End Sub

        Private Function FindCatalogNameForPath(statusMap As Dictionary(Of String, DownloadStatusEntry), fullPath As String) As String
            For Each pair In statusMap
                If String.Equals(pair.Value.Path, fullPath, StringComparison.OrdinalIgnoreCase) Then
                    Return pair.Key
                End If
            Next
            Return String.Empty
        End Function

        Private Function GetDiskSerialNumberByIndex(diskNumber As Integer) As String
            Try
                Using searcher As New ManagementObjectSearcher($"SELECT SerialNumber FROM Win32_DiskDrive WHERE Index={diskNumber}")
                    For Each disk As ManagementObject In searcher.Get()
                        Return Convert.ToString(disk("SerialNumber")).Trim()
                    Next
                End Using
            Catch ex As Exception
                _logService.WriteAsync($"USB | ID | SERIAL_ERRO | Disk {diskNumber} | {ex.Message}").GetAwaiter().GetResult()
            End Try
            Return String.Empty
        End Function

        Private Async Function SaveRawUsbWriteStatusAsync(item As CacheItem, drive As UsbDriveOption, analysis As IsoAnalysisResult, imageSha256 As String, rawVerified As Boolean?, cancellationToken As CancellationToken) As Task
            Dim statusMap = LoadDownloadStatusMap()
            Dim catalogName = FindCatalogNameForPath(statusMap, item.FullPath)
            If String.IsNullOrWhiteSpace(catalogName) Then
                ' P8: ISO que nao veio do download do app; reconhece pelo catalogo (fileName + tamanho), como a politica.
                Dim reason As String = Nothing
                Dim catalogItem = _usbWritePolicyResolver.FindCatalogItem(_catalogService.LoadAllCatalogs(AppPaths.DataDir), Nothing,
                                                                          Path.GetFileName(item.FullPath), New FileInfo(item.FullPath).Length, reason)
                If catalogItem Is Nothing Then
                    Await _logService.WriteAsync($"USB | RAW_ID | SKIP | Catalogo nao encontrado para {item.FullPath} | {reason}")
                    Return
                End If
                catalogName = catalogItem.Name
                If Not statusMap.ContainsKey(catalogName) Then
                    statusMap(catalogName) = New DownloadStatusEntry With {.Path = item.FullPath}
                End If
                Await _logService.WriteAsync($"USB | RAW_ID | CATALOGO_POR_ARQUIVO | Catalog={catalogName} | {item.FullPath}")
            End If

            If String.IsNullOrWhiteSpace(imageSha256) Then
                imageSha256 = Await ComputeImageSha256Async(item.FullPath, cancellationToken)
            End If

            Dim entry = statusMap(catalogName)
            If String.IsNullOrWhiteSpace(entry.Path) OrElse Not File.Exists(entry.Path) Then
                entry.Path = item.FullPath
            End If
            entry.ImageSha256 = imageSha256
            entry.UsbSha256 = imageSha256
            entry.UsbVerified = rawVerified.GetValueOrDefault(False)
            entry.UsbVerifiedAt = If(rawVerified.HasValue, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"), entry.UsbVerifiedAt)
            entry.UsbWrittenByApp = True
            entry.UsbWriteMethod = "RawDd"
            entry.UsbWrittenAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
            entry.UsbImageName = Path.GetFileName(item.FullPath)
            entry.UsbImageSizeBytes = New FileInfo(item.FullPath).Length
            entry.UsbDiskNumber = drive.DiskNumber
            entry.UsbDiskModel = drive.Model
            entry.UsbDiskSerialNumber = GetDiskSerialNumberByIndex(drive.DiskNumber)
            entry.UsbDiskSizeBytes = drive.SizeBytes
            entry.UsbBootEvidence = $"Recommendation={analysis.RecommendationText}; ElToritoBIOS={analysis.HasBiosElTorito}; ElToritoUEFI={analysis.HasUefiElTorito}; AcronisBootwiz={analysis.HasAcronisBootwiz}"
            statusMap(catalogName) = entry
            SaveDownloadStatusMap(statusMap)
            Await _logService.WriteAsync($"USB | RAW_ID | SAVED | Catalog={catalogName} | Disk={drive.DiskNumber} | Serial={entry.UsbDiskSerialNumber} | SHA256={imageSha256} | Verified={entry.UsbVerified}")
        End Function

        Private Async Function TryIdentifyKnownRawUsbAsync(drive As UsbDriveOption, cancellationToken As CancellationToken) As Task(Of RawUsbIdentificationResult)
            Dim statusMap = LoadDownloadStatusMap()
            Dim currentSerial = GetDiskSerialNumberByIndex(drive.DiskNumber)

            For Each pair In statusMap
                Dim entry = pair.Value
                If Not entry.UsbWrittenByApp Then Continue For
                If Not String.Equals(entry.UsbWriteMethod, "RawDd", StringComparison.OrdinalIgnoreCase) Then Continue For
                If String.IsNullOrWhiteSpace(entry.Path) OrElse Not File.Exists(entry.Path) Then Continue For
                If Not String.IsNullOrWhiteSpace(entry.UsbDiskSerialNumber) AndAlso Not String.IsNullOrWhiteSpace(currentSerial) AndAlso
                   Not String.Equals(entry.UsbDiskSerialNumber, currentSerial, StringComparison.OrdinalIgnoreCase) Then
                    Continue For
                End If

                Await _logService.WriteAsync($"USB | RAW_ID | TRY | Disk={drive.DiskNumber} | Catalog={pair.Key} | Image={entry.Path}")
                Dim comparison = Await CompareImageWithUsbDetailedAsync(entry.Path, drive.DiskNumber, cancellationToken)
                If comparison IsNot Nothing AndAlso comparison.MatchesWithVolumeChanges Then
                    Dim result As New RawUsbIdentificationResult With {
                        .CatalogName = pair.Key,
                        .ImagePath = entry.Path,
                        .ImageSha256 = entry.ImageSha256,
                        .ImageSizeBytes = If(File.Exists(entry.Path), New FileInfo(entry.Path).Length, 0),
                        .CalculatedUsbSha256 = entry.ImageSha256
                    }
                    result.Details.Add($"Catalogo: {pair.Key}")
                    result.Details.Add($"Imagem: {If(String.IsNullOrWhiteSpace(entry.UsbImageName), Path.GetFileName(entry.Path), entry.UsbImageName)}")
                    If result.ImageSizeBytes > 0 Then
                        result.Details.Add($"Tamanho validado: {result.ImageSizeBytes:N0} bytes")
                    End If
                    result.Details.Add($"SHA-256: {entry.ImageSha256}")
                    If comparison.VolumeDifferences > 0 Then
                        result.Details.Add($"Igual a imagem fora do volume FAT/EFI; {comparison.VolumeDifferences:N0} byte(s) alterado(s) dentro dele pelo Windows ao montar o USB (normal ao conectar; o restante confere byte a byte).")
                    End If
                    result.Details.Add($"Gravado em: {entry.UsbWrittenAt}")
                    result.Details.Add($"USB registrado: {entry.UsbDiskModel} | Serial: {If(String.IsNullOrWhiteSpace(entry.UsbDiskSerialNumber), "N/A", entry.UsbDiskSerialNumber)}")
                    If Not String.IsNullOrWhiteSpace(entry.UsbBootEvidence) Then
                        result.Details.Add($"Evidencias: {entry.UsbBootEvidence}")
                    End If
                    Return result
                End If
            Next

            Return Nothing
        End Function

        ''' <summary>
        ''' USB gravado em RAW/DD identificado pelos arquivos: le a ISO direto do disco fisico (UDF ou ISO9660), confere as
        ''' sentinelas e depois cada arquivo com o manifesto logico do item. Nao precisa da ISO local e aceita, com aviso,
        ''' arquivos inteiros dentro da particao FAT/EFI que o Windows altera ao montar o USB.
        ''' </summary>
        Private Async Function TryIdentifyRawByManifestAsync(drive As UsbDriveOption, cancellationToken As CancellationToken,
                                                             Optional onlyCatalogName As String = Nothing) As Task(Of RawUsbIdentificationResult)
            Dim catalog = _catalogService.LoadAllCatalogs(AppPaths.DataDir)
            Dim candidates = catalog.Where(Function(item)
                                               If item Is Nothing OrElse item.UsbIdentification Is Nothing Then Return False
                                               If Not String.Equals(Path.GetExtension(If(item.FileName, String.Empty)), ".iso", StringComparison.OrdinalIgnoreCase) Then Return False
                                               If Not String.IsNullOrWhiteSpace(onlyCatalogName) AndAlso Not String.Equals(item.Name, onlyCatalogName, StringComparison.OrdinalIgnoreCase) Then Return False
                                               Dim allowed = item.UsbWritePolicy?.AllowedMethods
                                               Return allowed IsNot Nothing AndAlso allowed.Any(Function(method) String.Equals(method?.Trim(), UsbWriteMethods.Raw, StringComparison.OrdinalIgnoreCase))
                                           End Function).ToList()
            If candidates.Count = 0 Then
                Await _logService.WriteAsync($"USB | RAW_MANIFEST | SKIP | Disk={drive.DiskNumber} | Nenhum item com RAW permitido e manifesto")
                Return Nothing
            End If

            Dim mbr = TryReadPhysicalSector(drive.DiskNumber, 0)
            Dim fatRanges = RawImageCompare.FatPartitionRanges(mbr)
            Dim diskSize = If(drive.SizeBytes > 0, drive.SizeBytes, GetDiskSizeBytes(drive.DiskNumber))
            Await _logService.WriteAsync($"USB | RAW_MANIFEST | INICIO | Disk={drive.DiskNumber} | Candidatos={String.Join(",", candidates.Select(Function(item) item.Name))} | FaixasFat={fatRanges.Count}")

            Return Await Task.Run(
                Function() As RawUsbIdentificationResult
                    Using disk As New FileStream($"\\.\PhysicalDrive{drive.DiskNumber}", FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 1)
                        Using aligned As New AlignedReadStream(disk, diskSize)
                            Dim systems = RawFileManifestVerifier.OpenIsoFileSystems(aligned)
                            Try
                                If systems.Count = 0 Then
                                    _logService.WriteAsync($"USB | RAW_MANIFEST | SEM_ISO | Disk={drive.DiskNumber}").GetAwaiter().GetResult()
                                    Return Nothing
                                End If
                                Dim tested As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
                                For Each item In candidates
                                    For Each mode In item.UsbIdentification.EffectiveModes
                                        cancellationToken.ThrowIfCancellationRequested()
                                        If mode Is Nothing OrElse Not String.Equals(mode.Mode, "logical", StringComparison.OrdinalIgnoreCase) Then Continue For
                                        ' Pares -sharepoint/-dropbox tem o mesmo manifesto: confere uma vez so.
                                        If Not tested.Add(If(mode.TreeHash, item.Name)) Then Continue For
                                        For Each isoFs In systems
                                            If Not RawFileManifestVerifier.SentinelsMatch(isoFs.FileSystem, isoFs.Paths, mode.SentinelFiles) Then Continue For
                                            _logService.WriteAsync($"USB | RAW_MANIFEST | SENTINEL_MATCH | Disk={drive.DiskNumber} | Catalog={item.Name} | Leitor={isoFs.Name}").GetAwaiter().GetResult()
                                            Dim manifest = LoadUsbManifestAsync(mode, item.Name, cancellationToken).GetAwaiter().GetResult()
                                            If manifest Is Nothing OrElse manifest.Entries.Count = 0 Then Continue For
                                            Dim check = RawFileManifestVerifier.Verify(isoFs.FileSystem, isoFs.Paths, isoFs.Name, manifest.Entries, fatRanges,
                                                Sub(progress)
                                                    If progress.Checked Mod 250 = 0 OrElse progress.Checked = manifest.Entries.Count Then
                                                        _logService.WriteAsync($"USB | RAW_MANIFEST | PROGRESSO | Catalog={item.Name} | {progress.Checked}/{manifest.Entries.Count} | Iguais={progress.FilesVerified} | Divergentes={progress.MismatchCount} | Ausentes={progress.MissingCount}").GetAwaiter().GetResult()
                                                    End If
                                                End Sub,
                                                cancellationToken)
                                            _logService.WriteAsync($"USB | RAW_MANIFEST | RESULTADO | Catalog={item.Name} | Leitor={isoFs.Name} | Iguais={check.FilesVerified} | SoTamanho={check.SizeOnlyVerified} | Tolerados={check.ToleratedFiles.Count} | Divergentes={check.MismatchCount} | Ausentes={check.MissingCount} | Ilegiveis={check.UnreadableCount}").GetAwaiter().GetResult()
                                            If Not check.Matches Then Continue For

                                            Dim result As New RawUsbIdentificationResult With {
                                                .CatalogName = item.Name,
                                                .ImagePath = item.FileUrl,
                                                .ImageSha256 = item.Sha256,
                                                .ImageSizeBytes = item.ImageSizeBytes
                                            }
                                            result.Details.Add($"Modo: arquivos da ISO conferidos direto no disco ({isoFs.Name}) com o manifesto do catalogo.")
                                            result.Details.Add($"Arquivos conferidos: {check.FilesVerified} por SHA-256" & If(check.SizeOnlyVerified > 0, $", {check.SizeOnlyVerified} so pelo tamanho", String.Empty) & $" (manifesto: {manifest.Entries.Count}).")
                                            If check.ToleratedFiles.Count > 0 Then
                                                result.Details.Add($"Alterado(s) pelo Windows dentro da particao EFI ao montar o USB (normal ao conectar): {String.Join(", ", check.ToleratedFiles)}.")
                                            End If
                                            result.Details.Add($"Imagem do catalogo: {item.FileName} | SHA-256: {item.Sha256}")
                                            Return result
                                        Next
                                    Next
                                Next
                                Return Nothing
                            Finally
                                For Each isoFs In systems
                                    isoFs.FileSystem.Dispose()
                                Next
                            End Try
                        End Using
                    End Using
                End Function, cancellationToken)
        End Function

        Private Async Function TryIdentifyOfficialRawUsbAsync(drive As UsbDriveOption, cancellationToken As CancellationToken,
                                                              Optional onlyCatalogName As String = Nothing) As Task(Of RawUsbIdentificationResult)
            Dim candidates = GetRawCatalogCandidates(drive).
                Where(Function(candidate) String.IsNullOrWhiteSpace(onlyCatalogName) OrElse
                                          String.Equals(candidate.Item.Name, onlyCatalogName, StringComparison.OrdinalIgnoreCase)).
                GroupBy(Function(candidate) candidate.SizeBytes).
                OrderBy(Function(group) group.Key)

            If Not candidates.Any() Then
                Await _logService.WriteAsync($"USB | RAW_CATALOG | SKIP | Disk={drive.DiskNumber} | Nenhum item oficial com SHA-256 e tamanho compativel.")
                Return Nothing
            End If

            For Each sizeGroup In candidates
                cancellationToken.ThrowIfCancellationRequested()
                Dim bytesToRead = sizeGroup.Key
                Await _logService.WriteAsync($"USB | RAW_CATALOG | TRY | Disk={drive.DiskNumber} | ImageSizeBytes={bytesToRead} | Candidates={sizeGroup.Count()}")
                Dim usbSha256 = Await ReadUsbRawAndComputeSha256Async(drive.DiskNumber, bytesToRead, cancellationToken)

                For Each candidate In sizeGroup
                    Dim item = candidate.Item
                    Dim expectedSha256 = candidate.ImageSha256.Trim().ToLowerInvariant()
                    Dim matches = String.Equals(usbSha256, expectedSha256, StringComparison.OrdinalIgnoreCase)
                    Await _logService.WriteAsync($"USB | RAW_CATALOG | COMPARE | Disk={drive.DiskNumber} | Catalog={item.Name} | Source={candidate.Source} | ImageSizeBytes={bytesToRead} | ExpectedSHA256={expectedSha256} | CalculatedSHA256={usbSha256} | Match={matches}")

                    If matches Then
                        Dim result As New RawUsbIdentificationResult With {
                            .CatalogName = item.Name,
                            .ImagePath = item.FileUrl,
                            .ImageSha256 = expectedSha256,
                            .ImageSizeBytes = bytesToRead,
                            .CalculatedUsbSha256 = usbSha256
                        }
                        result.Details.Add($"Modo: validacao oficial por catalogo ({candidate.Source}).")
                        result.Details.Add($"Catalogo: {item.Name}")
                        result.Details.Add($"Grupo: {item.Group}")
                        result.Details.Add($"Imagem: {item.FileUrl}")
                        result.Details.Add($"Tamanho validado: {bytesToRead:N0} bytes")
                        result.Details.Add($"SHA-256 esperado: {expectedSha256}")
                        result.Details.Add($"SHA-256 calculado no USB: {usbSha256}")
                        Return result
                    End If
                Next
            Next

            Return Nothing
        End Function

        Private Function GetRawCatalogCandidates(drive As UsbDriveOption) As List(Of RawCatalogCandidate)
            Dim result As New List(Of RawCatalogCandidate)()
            Dim catalog = _catalogService.LoadAllCatalogs(AppPaths.DataDir)
            For Each item In catalog
                If item Is Nothing Then Continue For
                ' So imagem que pode ter sido gravada em RAW/DD: TIBX e copiado como arquivo, e cada candidato custa ler
                ' o tamanho inteiro da imagem do USB.
                Dim extension = Path.GetExtension(If(item.FileName, String.Empty)).ToLowerInvariant()
                If extension <> ".iso" AndAlso extension <> ".img" Then Continue For
                Dim allowed = item.UsbWritePolicy?.AllowedMethods
                If allowed IsNot Nothing AndAlso allowed.Count > 0 AndAlso
                   Not allowed.Any(Function(method) String.Equals(method?.Trim(), UsbWriteMethods.Raw, StringComparison.OrdinalIgnoreCase)) Then Continue For

                Dim addedFromUsbIdentification = False
                If item.UsbIdentification IsNot Nothing Then
                    For Each mode In item.UsbIdentification.EffectiveModes
                        If mode Is Nothing OrElse Not String.Equals(mode.Mode, "raw", StringComparison.OrdinalIgnoreCase) Then Continue For
                        If mode.SizeBytes.GetValueOrDefault() <= 0 OrElse String.IsNullOrWhiteSpace(mode.ImageSha256) Then Continue For
                        If drive IsNot Nothing AndAlso drive.SizeBytes > 0 AndAlso mode.SizeBytes.Value > drive.SizeBytes Then Continue For
                        result.Add(New RawCatalogCandidate With {
                            .Item = item,
                            .ImageSha256 = mode.ImageSha256,
                            .SizeBytes = mode.SizeBytes.Value,
                            .Source = "usbIdentification.raw"
                        })
                        addedFromUsbIdentification = True
                    Next
                End If

                If addedFromUsbIdentification Then Continue For
                If item.ImageSizeBytes <= 0 OrElse String.IsNullOrWhiteSpace(item.Sha256) Then Continue For
                If drive IsNot Nothing AndAlso drive.SizeBytes > 0 AndAlso item.ImageSizeBytes > drive.SizeBytes Then Continue For
                result.Add(New RawCatalogCandidate With {
                    .Item = item,
                    .ImageSha256 = item.Sha256,
                    .SizeBytes = item.ImageSizeBytes,
                    .Source = "catalog.sha256"
                })
            Next
            Return result
        End Function

        Private Async Function ComputeImageSha256Async(filePath As String, cancellationToken As CancellationToken) As Task(Of String)
            Try
                Using sha256 = System.Security.Cryptography.SHA256.Create()
                    Using stream = New FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize:=81920, useAsync:=True)
                        Dim hash = Await sha256.ComputeHashAsync(stream, cancellationToken)
                        Return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant()
                    End Using
                End Using
            Catch ex As Exception
                _logService.WriteAsync($"SHA256 | ERRO | {filePath} | {ex.Message}").GetAwaiter().GetResult()
                Throw
            End Try
        End Function

        ''' <summary>
        ''' P-10: grava em disco os buffers do volume do USB antes da verificacao.
        ''' Substitui a rotina antiga, que fazia flush da letra fixa Z: por um .bat criado no diretorio
        ''' atual e rodava diskpart com caminho sem aspas e sem conferir o resultado.
        ''' </summary>
        Private Sub FlushUsbVolume(driveLetter As String)
            Dim letter = If(driveLetter, String.Empty).TrimEnd("\"c).TrimEnd(":"c).Trim().ToUpperInvariant()
            If letter.Length <> 1 Then
                _logService.WriteAsync($"USB | FLUSH | IGNORADO | Letra invalida: {driveLetter}").GetAwaiter().GetResult()
                Return
            End If

            Using volume = NativeMethods.CreateFile($"\\.\{letter}:",
                                                    NativeMethods.GENERIC_READ Or NativeMethods.GENERIC_WRITE,
                                                    NativeMethods.FILE_SHARE_READ Or NativeMethods.FILE_SHARE_WRITE,
                                                    IntPtr.Zero, NativeMethods.OPEN_EXISTING, 0UI, IntPtr.Zero)
                If volume.IsInvalid Then
                    _logService.WriteAsync($"USB | FLUSH | AVISO | {letter}: nao abriu | Err={Marshal.GetLastWin32Error()}").GetAwaiter().GetResult()
                    Return
                End If

                Dim ok = FlushFileBuffers(volume)
                _logService.WriteAsync($"USB | FLUSH | {(If(ok, "OK", "AVISO"))} | {letter}: | Err={If(ok, 0, Marshal.GetLastWin32Error())}").GetAwaiter().GetResult()
            End Using
        End Sub

        <DllImport("kernel32.dll", SetLastError:=True)>
        Private Shared Function FlushFileBuffers(hFile As SafeFileHandle) As <MarshalAs(UnmanagedType.Bool)> Boolean
        End Function

        Private Async Function ReadUsbRawAndComputeSha256Async(diskNumber As Integer, cancellationToken As CancellationToken) As Task(Of String)
            Try
                _logService.WriteAsync($"USB | RAW_READ | INICIO | Disk {diskNumber}").GetAwaiter().GetResult()
                
                Using sha256 = System.Security.Cryptography.SHA256.Create()
                    ' Abrir dispositivo físico para acesso raw
                    Dim devicePath = $"\\.\PhysicalDrive{diskNumber}"
                    
                    Using stream = New FileStream(devicePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, bufferSize:=1048576, useAsync:=True)
                        Dim buffer(1048575) As Byte ' 1MB buffer
                        Dim totalBytesRead As Long = 0
                        Dim fileSize = stream.Length
                        
                        While totalBytesRead < fileSize
                            If cancellationToken.IsCancellationRequested Then
                                Throw New OperationCanceledException()
                            End If
                            
                            Dim bytesRead = Await stream.ReadAsync(buffer, 0, buffer.Length, cancellationToken)
                            If bytesRead = 0 Then Exit While
                            
                            sha256.TransformBlock(buffer, 0, bytesRead, Nothing, 0)
                            totalBytesRead += bytesRead
                            
                            ' Log progress a cada 100MB
                            If totalBytesRead Mod (100 * 1024 * 1024) = 0 Then
                                Dim progress = CInt((totalBytesRead * 100) / fileSize)
                                _logService.WriteAsync($"USB | RAW_READ | PROGRESS | Disk {diskNumber} | {progress}% | {totalBytesRead}/{fileSize} bytes").GetAwaiter().GetResult()
                            End If
                        End While
                        
                        ' Finalizar hash
                        sha256.TransformFinalBlock(New Byte(-1) {}, 0, 0)
                        Dim hash = sha256.Hash
                        
                        Dim hashString = BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant()
                        _logService.WriteAsync($"USB | RAW_READ | OK | Disk {diskNumber} | SHA256={hashString}").GetAwaiter().GetResult()
                        
                        Return hashString
                    End Using
                End Using
            Catch ex As Exception
                _logService.WriteAsync($"USB | RAW_READ | ERRO | Disk {diskNumber} | {ex.Message}").GetAwaiter().GetResult()
                Throw New InvalidOperationException($"Falha na leitura raw do USB: {ex.Message}")
            End Try
        End Function

        Private Async Function ReadUsbRawAndComputeSha256Async(diskNumber As Integer, bytesToRead As Long, cancellationToken As CancellationToken) As Task(Of String)
            If bytesToRead <= 0 Then
                Throw New ArgumentOutOfRangeException(NameOf(bytesToRead), "Quantidade de bytes para validacao RAW invalida.")
            End If

            Try
                _logService.WriteAsync($"USB | RAW_READ_PARTIAL | INICIO | Disk {diskNumber} | Bytes={bytesToRead}").GetAwaiter().GetResult()

                Using sha256 = System.Security.Cryptography.SHA256.Create()
                    Dim devicePath = $"\\.\PhysicalDrive{diskNumber}"
                    Using stream = New FileStream(devicePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, bufferSize:=1048576, useAsync:=True)
                        Dim buffer(1048575) As Byte
                        Dim totalBytesRead As Long = 0

                        While totalBytesRead < bytesToRead
                            cancellationToken.ThrowIfCancellationRequested()
                            Dim remaining = bytesToRead - totalBytesRead
                            Dim requested = CInt(Math.Min(buffer.Length, remaining))
                            Dim bytesRead = Await stream.ReadAsync(buffer, 0, requested, cancellationToken)
                            If bytesRead = 0 Then
                                Throw New EndOfStreamException($"USB terminou antes de ler {bytesToRead:N0} bytes. Lidos: {totalBytesRead:N0} bytes.")
                            End If

                            sha256.TransformBlock(buffer, 0, bytesRead, Nothing, 0)
                            totalBytesRead += bytesRead

                            If totalBytesRead Mod (100L * 1024L * 1024L) = 0 OrElse totalBytesRead = bytesToRead Then
                                Dim progress = CInt((totalBytesRead * 100L) \ bytesToRead)
                                _logService.WriteAsync($"USB | RAW_READ_PARTIAL | PROGRESS | Disk {diskNumber} | {progress}% | {totalBytesRead}/{bytesToRead} bytes").GetAwaiter().GetResult()
                            End If
                        End While

                        sha256.TransformFinalBlock(New Byte() {}, 0, 0)
                        Dim hashString = BitConverter.ToString(sha256.Hash).Replace("-", "").ToLowerInvariant()
                        _logService.WriteAsync($"USB | RAW_READ_PARTIAL | OK | Disk {diskNumber} | Bytes={bytesToRead} | SHA256={hashString}").GetAwaiter().GetResult()
                        Return hashString
                    End Using
                End Using
            Catch ex As Exception
                _logService.WriteAsync($"USB | RAW_READ_PARTIAL | ERRO | Disk {diskNumber} | Bytes={bytesToRead} | {ex.Message}").GetAwaiter().GetResult()
                Throw New InvalidOperationException($"Falha na leitura raw parcial do USB: {ex.Message}", ex)
            End Try
        End Function

        Private Async Function VerifyUsbWriteAsync(item As CacheItem, drive As UsbDriveOption, imageSha256 As String, cancellationToken As CancellationToken) As Task(Of UsbVerificationResult)
            Try
                _logService.WriteAsync($"USB | VERIFY | INICIO | {item.DisplayName} -> Disk {drive.DiskNumber}").GetAwaiter().GetResult()
                
                ' Passo 3: Flush + Lock
                FlushUsbVolume(drive.DriveLetter)
                
                ' Passo 4: Verificação baseada no tipo de cópia
                Dim result As UsbVerificationResult
                
                ' Esta rotina é chamada após o fluxo lógico BIOS/UEFI. RAW/DD tem verificação própria.
                If item.Extension = ".iso" Then
                    result = Await VerifyLogicalCopyAsync(item, drive, cancellationToken)
                Else
                    ' Para cópia bruta, usar comparação binária
                    Dim rawOk = Await CompareImageWithUsbAsync(item.FullPath, drive.DiskNumber, cancellationToken)
                    result = New UsbVerificationResult With {
                        .Status = If(rawOk, UsbVerificationStatus.Success, UsbVerificationStatus.Failed),
                        .Message = If(rawOk, "Verificação raw concluída.", "Falha na comparação binária.")
                    }
                End If
                
                ' Passo 5: Atualizar status
                Dim statusMap = LoadDownloadStatusMap()
                Dim catalogName = FindCatalogNameForPath(statusMap, item.FullPath)
                
                If Not String.IsNullOrWhiteSpace(catalogName) AndAlso statusMap.ContainsKey(catalogName) Then
                    Dim entry = statusMap(catalogName)
                    entry.UsbSha256 = If(result.Status = UsbVerificationStatus.Success,
                                         imageSha256,
                                         If(result.Status = UsbVerificationStatus.Inconclusive, "VERIFICATION_INCONCLUSIVE", "VERIFICATION_FAILED"))
                    entry.UsbVerified = (result.Status = UsbVerificationStatus.Success)
                    entry.UsbVerifiedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
                    statusMap(catalogName) = entry
                    SaveDownloadStatusMap(statusMap)
                End If
                
                If result.Status = UsbVerificationStatus.Success Then
                    _logService.WriteAsync($"USB | VERIFY | OK | {item.DisplayName} | Verificação bem-sucedida").GetAwaiter().GetResult()
                ElseIf result.Status = UsbVerificationStatus.Inconclusive Then
                    _logService.WriteAsync($"USB | VERIFY | INCONCLUSIVE | {item.DisplayName} | {result.Message}").GetAwaiter().GetResult()
                Else
                    _logService.WriteAsync($"USB | VERIFY | ERRO | {item.DisplayName} | Falha na verificação").GetAwaiter().GetResult()
                End If
                
                Return result
            Catch ex As OperationCanceledException
                Throw
            Catch ex As Exception
                _logService.WriteAsync($"USB | VERIFY | ERRO | {item.DisplayName} | {ex.Message}").GetAwaiter().GetResult()
                Return New UsbVerificationResult With {
                    .Status = UsbVerificationStatus.Failed,
                    .Message = ex.Message
                }
            End Try
        End Function

        ''' <summary>
        ''' Verifica cópia lógica calculando SHA-256 de todos os arquivos no USB
        ''' </summary>
        Private Async Function VerifyLogicalCopyAsync(item As CacheItem, drive As UsbDriveOption, cancellationToken As CancellationToken) As Task(Of UsbVerificationResult)
            Try
                _logService.WriteAsync($"USB | VERIFY_LOGICAL | INICIO | {item.DisplayName} -> {drive.RootPath}").GetAwaiter().GetResult()

                Dim expectedFiles As New List(Of IsoFileEntry)()
                Dim totalBytes As Long = 0

                Using isoStream As New FileStream(item.FullPath, FileMode.Open, FileAccess.Read, FileShare.Read, 4 * 1024 * 1024, useAsync:=True)
                    Dim useUdf As Boolean = False
                    Try
                        Using testUdf As New UdfReader(isoStream)
                            useUdf = True
                        End Using
                        isoStream.Seek(0, SeekOrigin.Begin)
                    Catch
                        isoStream.Seek(0, SeekOrigin.Begin)
                    End Try

                    Dim openFs As Func(Of DiscUtils.DiscFileSystem)
                    If useUdf Then
                        openFs = Function() CType(New UdfReader(isoStream), DiscUtils.DiscFileSystem)
                    Else
                        openFs = Function() CType(New CDReader(isoStream, True), DiscUtils.DiscFileSystem)
                    End If

                    Using fs As DiscUtils.DiscFileSystem = openFs()
                        EnumerateDiscUtilsFiles(fs.Root, String.Empty, expectedFiles, totalBytes)
                        _logService.WriteAsync($"USB | VERIFY_LOGICAL | EXPECTED_FILES | {expectedFiles.Count}").GetAwaiter().GetResult()

                        Dim mismatchCount As Long = 0
                        Dim missingCount As Integer = 0
                        Dim verifiedCount As Integer = 0
                        Dim blockedByAvCount As Integer = 0
                        Dim readErrorCount As Integer = 0

                        ' Percentual real: cada arquivo e lido duas vezes (ISO e USB).
                        Const verifyText As String = "Verificando gravacao USB..."
                        Dim workTotal = totalBytes * 2
                        Dim workDone As Long = 0
                        Dim startedBytes As Long = 0
                        Dim lastPercent = ShowProgressPercent(verifyText, 0, -1)
                        Dim onBytesRead As Action(Of Integer) =
                            Sub(bytesRead)
                                workDone += bytesRead
                                lastPercent = ShowProgressPercent(verifyText, If(workTotal > 0, CInt(Math.Min(100, workDone * 100L \ workTotal)), 100), lastPercent)
                            End Sub

                        For Each entry In expectedFiles
                            cancellationToken.ThrowIfCancellationRequested()
                            ' Arquivo ausente ou com erro conta como processado.
                            workDone = 2 * startedBytes
                            startedBytes += entry.Size

                            Dim relPath = If(String.IsNullOrEmpty(entry.IsoDir), entry.Entry, Path.Combine(entry.IsoDir, entry.Entry))
                            Dim usbPath = Path.Combine(drive.RootPath, relPath)

                            If Not File.Exists(usbPath) Then
                                missingCount += 1
                                If missingCount <= 20 Then
                                    _logService.WriteAsync($"USB | VERIFY_LOGICAL | MISSING | {relPath}").GetAwaiter().GetResult()
                                End If
                                Continue For
                            End If

                            Try
                                Dim openedPath As String = String.Empty
                                Using isoFileStream As IO.Stream = OpenIsoFileWithFallback(fs, relPath, openedPath)
                                    Dim isoHash = Await ComputeSha256FromStreamAsync(isoFileStream, cancellationToken, onBytesRead)
                                    Dim usbHash As String
                                    Using usbStream As New FileStream(usbPath, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024, useAsync:=True)
                                        usbHash = Await ComputeSha256FromStreamAsync(usbStream, cancellationToken, onBytesRead)
                                    End Using
                                    If Not String.Equals(isoHash, usbHash, StringComparison.OrdinalIgnoreCase) Then
                                        mismatchCount += 1
                                        If mismatchCount <= 20 Then
                                            _logService.WriteAsync($"USB | VERIFY_LOGICAL | MISMATCH | {relPath} | ISO={isoHash} | USB={usbHash}").GetAwaiter().GetResult()
                                        End If
                                    Else
                                        verifiedCount += 1
                                    End If
                                End Using
                            Catch ex As Exception
                                If IsAntivirusLockMessage(ex.Message) Then
                                    blockedByAvCount += 1
                                    If blockedByAvCount <= 20 Then
                                        _logService.WriteAsync($"USB | VERIFY_LOGICAL | BLOCKED_AV | {relPath} | {ex.Message}").GetAwaiter().GetResult()
                                    End If
                                Else
                                    readErrorCount += 1
                                    If readErrorCount <= 20 Then
                                        _logService.WriteAsync($"USB | VERIFY_LOGICAL | READ_ERROR | {relPath} | {ex.Message}").GetAwaiter().GetResult()
                                    End If
                                End If
                            End Try
                        Next

                        _logService.WriteAsync($"USB | VERIFY_LOGICAL | RESULT | Verified={verifiedCount} | Missing={missingCount} | Mismatch={mismatchCount} | AvBlocked={blockedByAvCount} | ReadErrors={readErrorCount}").GetAwaiter().GetResult()

                        Dim outcome = LogicalCopyCheck.ClassifyHashCheck(missingCount, mismatchCount, blockedByAvCount, readErrorCount)
                        If outcome = UsbCheckStatus.Success Then
                            Return New UsbVerificationResult With {
                                .Status = UsbVerificationStatus.Success,
                                .Message = "Verificação por manifesto concluída com sucesso."
                            }
                        End If

                        If outcome = UsbCheckStatus.Inconclusive Then
                            Return New UsbVerificationResult With {
                                .Status = UsbVerificationStatus.Inconclusive,
                                .Message = $"Verificação inconclusiva: {blockedByAvCount} arquivo(s) bloqueado(s) por antivírus e {readErrorCount} erro(s) de leitura."
                            }
                        End If

                        Return New UsbVerificationResult With {
                            .Status = UsbVerificationStatus.Failed,
                            .Message = $"Falha na verificação: {missingCount} faltando e {mismatchCount} divergente(s)."
                        }
                    End Using
                End Using
                
            Catch ex As OperationCanceledException
                Throw
            Catch ex As Exception
                _logService.WriteAsync($"USB | VERIFY_LOGICAL | ERRO | {ex.Message}").GetAwaiter().GetResult()
                _logService.WriteAsync($"USB | VERIFY_LOGICAL | TRACE | {ex}").GetAwaiter().GetResult()
                Dim status = If(IsAntivirusLockMessage(ex.Message), UsbVerificationStatus.Inconclusive, UsbVerificationStatus.Failed)
                Return New UsbVerificationResult With {
                    .Status = status,
                    .Message = ex.Message
                }
            End Try
        End Function

        Private Shared Function IsAntivirusLockMessage(message As String) As Boolean
            Dim lower = If(message, String.Empty).ToLowerInvariant()
            Return lower.Contains("vírus") OrElse
                   lower.Contains("virus") OrElse
                   lower.Contains("potentially unwanted") OrElse
                   lower.Contains("possivelmente indesejado") OrElse
                   lower.Contains("acesso negado") OrElse
                   lower.Contains("access is denied")
        End Function

        Private Shared Async Function ComputeSha256FromStreamAsync(input As IO.Stream, cancellationToken As CancellationToken,
                                                                   Optional onBytesRead As Action(Of Integer) = Nothing) As Task(Of String)
            input.Position = 0
            Using sha256 = System.Security.Cryptography.SHA256.Create()
                Dim buffer(1024 * 1024 - 1) As Byte
                Do
                    cancellationToken.ThrowIfCancellationRequested()
                    Dim bytesRead = Await input.ReadAsync(buffer, 0, buffer.Length, cancellationToken)
                    If bytesRead = 0 Then Exit Do
                    sha256.TransformBlock(buffer, 0, bytesRead, Nothing, 0)
                    onBytesRead?.Invoke(bytesRead)
                Loop
                sha256.TransformFinalBlock(Array.Empty(Of Byte)(), 0, 0)
                Return BitConverter.ToString(sha256.Hash).Replace("-", "").ToLowerInvariant()
            End Using
        End Function

        Private Async Function CompareImageWithUsbAsync(imagePath As String, diskNumber As Integer, cancellationToken As CancellationToken) As Task(Of Boolean)
            Dim result = Await CompareImageWithUsbDetailedAsync(imagePath, diskNumber, cancellationToken)
            Return result IsNot Nothing AndAlso result.MatchesImage
        End Function

        ' Compara byte a byte e separa o que o Windows altera ao reconhecer o disco (assinatura do MBR) e ao montar o
        ' volume FAT/EFI da imagem das divergencias reais.
        Private Const CompareStatusText As String = "Comparando imagem com o USB..."

        ''' <summary>Comparacao binaria imagem x disco, com o percentual real na barra e na status.</summary>
        Private Async Function CompareImageWithUsbDetailedAsync(imagePath As String, diskNumber As Integer, cancellationToken As CancellationToken) As Task(Of RawCompareResult)
            Try
                _logService.WriteAsync($"USB | COMPARE | INICIO | {imagePath} -> Disk {diskNumber}").GetAwaiter().GetResult()
                Dim imageSize As Long = New FileInfo(imagePath).Length
                Dim logged = 0
                Dim nextProgress As Long = 100L * 1024 * 1024
                Dim lastPercent = ShowProgressPercent(CompareStatusText, 0, -1)
                Dim result As RawCompareResult
                Using imageStream = New FileStream(imagePath, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize:=RawImageVerifier.BufferSize, useAsync:=True)
                    Using usbStream = New FileStream($"\\.\PhysicalDrive{diskNumber}", FileMode.Open, FileAccess.Read, FileShare.ReadWrite, bufferSize:=RawImageVerifier.BufferSize, useAsync:=True)
                        result = Await RawImageVerifier.CompareAsync(imageStream, usbStream, imageSize,
                            Sub(offset, imageByte, usbByte)
                                logged += 1
                                If logged <= 10 Then
                                    _logService.WriteAsync($"USB | COMPARE | DIVERGENCIA | Offset={offset} | Imagem={imageByte:X2} | USB={usbByte:X2}").GetAwaiter().GetResult()
                                End If
                            End Sub,
                            Sub(compared)
                                lastPercent = ShowProgressPercent(CompareStatusText, If(imageSize > 0, CInt(Math.Min(100, compared * 100L \ imageSize)), 100), lastPercent)
                                If compared >= nextProgress Then
                                    nextProgress += 100L * 1024 * 1024
                                    _logService.WriteAsync($"USB | COMPARE | PROGRESS | Disk {diskNumber} | {CInt(compared * 100 / imageSize)}% | {compared}/{imageSize} bytes | Divergências: {logged}").GetAwaiter().GetResult()
                                End If
                            End Sub,
                            cancellationToken)
                    End Using
                End Using
                _logService.WriteAsync($"USB | COMPARE | RESULTADO | Disk {diskNumber} | Total comparado: {result.BytesCompared} bytes | Divergências: {result.SignatureDifferences + result.VolumeDifferences + result.OtherDifferences} | Assinatura={result.SignatureDifferences} | VolumeFat={result.VolumeDifferences} | Outras={result.OtherDifferences}").GetAwaiter().GetResult()
                Return result
            Catch ex As OperationCanceledException
                Throw
            Catch ex As Exception
                _logService.WriteAsync($"USB | COMPARE | ERRO | Disk {diskNumber} | {ex.Message}").GetAwaiter().GetResult()
                Throw New InvalidOperationException($"Falha na comparação binária: {ex.Message}")
            End Try
        End Function

        Private Class DownloadStatusEntry
            Public Property Downloaded As Boolean
            Public Property Md5Valid As Boolean?
            Public Property Md5Cache As String
            Public Property Path As String
            Public Property UpdatedAt As String
            Public Property ImageSha256 As String
            Public Property UsbSha256 As String
            Public Property UsbVerified As Boolean
            Public Property UsbVerifiedAt As String
            Public Property UsbWrittenByApp As Boolean
            Public Property UsbWriteMethod As String
            Public Property UsbWrittenAt As String
            Public Property UsbImageName As String
            Public Property UsbImageSizeBytes As Long
            Public Property UsbDiskNumber As Integer
            Public Property UsbDiskModel As String
            Public Property UsbDiskSerialNumber As String
            Public Property UsbDiskSizeBytes As Long
            Public Property UsbBootEvidence As String
            ' Novas propriedades para cache de tipo de ISO
            Public Property IsoType As String = "Unknown"
            Public Property IsoTypeAnalyzedAt As String = ""
            Public Property IsoTypeAnalyzed As Boolean = False
            ' Campos que esta tela nao usa voltam intactos ao gravar.
            <System.Text.Json.Serialization.JsonExtensionData>
            Public Property Extra As Dictionary(Of String, System.Text.Json.JsonElement)
        End Class

        Private Class CacheItem
            Implements System.ComponentModel.INotifyPropertyChanged
            
            Public ReadOnly Property FullPath As String
            Public ReadOnly Property Extension As String
            Public ReadOnly Property IsPlaceholder As Boolean
            Private _displayName As String
            Public Property IsoType As IsoType = IsoType.Unknown
            Public Property TypeIcon As String = "📀"
            Public Property TypeName As String = "ISO"
            Public Property IsAnalyzed As Boolean = False
            Public Property IsoAnalysis As IsoAnalysisResult

            Public Event PropertyChanged(sender As Object, e As System.ComponentModel.PropertyChangedEventArgs) Implements System.ComponentModel.INotifyPropertyChanged.PropertyChanged

            Public Property DisplayName As String
                Get
                    Return _displayName
                End Get
                Private Set(value As String)
                    _displayName = value
                    NotifyPropertyChanged(nameof(DisplayName))
                End Set
            End Property

            Public Sub New(filePath As String)
                FullPath = filePath
                Extension = IO.Path.GetExtension(filePath).ToLowerInvariant()
                IsPlaceholder = False
                Dim size = New FileInfo(filePath).Length / (1024.0 * 1024.0)
                
                ' Inicializar display name basico (sem deteccao)
                If Extension = ".iso" Then
                    DisplayName = $"📀 {IO.Path.GetFileName(filePath)} ({size:N1} MB)"
                Else
                    DisplayName = $"💾 {IO.Path.GetFileName(filePath)} ({size:N1} MB) [TIBX]"
                    TypeIcon = "💾"
                    TypeName = "TIBX"
                    IsAnalyzed = True ' TIBX não precisa análise
                End If
            End Sub

            Private Sub New(displayText As String, isPlaceholder As Boolean)
                FullPath = String.Empty
                Extension = String.Empty
                IsPlaceholder = isPlaceholder
                DisplayName = displayText
            End Sub

            Public Shared Function CreatePlaceholder(displayText As String) As CacheItem
                Return New CacheItem(displayText, True)
            End Function
            
            ''' <summary>
            ''' Atualiza informações do tipo de ISO após detecção
            ''' </summary>
            Public Sub UpdateIsoType(detectedType As IsoType)
                IsoType = detectedType
                IsAnalyzed = True
                
                Select Case detectedType
                    Case IsoType.Windows
                        TypeIcon = "🪟"
                        TypeName = "Windows"
                    Case IsoType.Linux
                        TypeIcon = "🐧"
                        TypeName = "Linux"
                    Case IsoType.Unknown
                        TypeIcon = "❓"
                        TypeName = "Desconhecido"
                    Case Else
                        TypeIcon = "📀"
                        TypeName = "ISO"
                End Select
                
                ' Atualizar display name com informações detectadas
                Dim size = New FileInfo(FullPath).Length / (1024.0 * 1024.0)
                DisplayName = $"{TypeIcon} {IO.Path.GetFileName(FullPath)} ({size:N1} MB) [{TypeName}]"
            End Sub

            Public Sub UpdateIsoAnalysis(analysis As IsoAnalysisResult, detectedType As IsoType)
                IsoAnalysis = analysis
                IsoType = detectedType
                IsAnalyzed = True

                Select Case analysis.Recommendation
                    Case IsoBootRecommendation.BiosAndUefi
                        TypeIcon = "🖥️"
                    Case IsoBootRecommendation.Bios
                        TypeIcon = "💻"
                    Case IsoBootRecommendation.Uefi
                        TypeIcon = "⚙️"
                    Case IsoBootRecommendation.RawRecommended
                        TypeIcon = "🧩"
                    Case IsoBootRecommendation.Unsupported
                        TypeIcon = "⚠️"
                    Case IsoBootRecommendation.UefiNtfs
                        TypeIcon = "💾"
                    Case Else
                        TypeIcon = "❓"
                End Select
                TypeName = analysis.RecommendationText

                Dim size = New FileInfo(FullPath).Length / (1024.0 * 1024.0)
                DisplayName = $"{TypeIcon} {IO.Path.GetFileName(FullPath)} ({size:N1} MB) [{TypeName}]"
            End Sub
            
            Private Sub NotifyPropertyChanged(propertyName As String)
                RaiseEvent PropertyChanged(Me, New System.ComponentModel.PropertyChangedEventArgs(propertyName))
            End Sub
        End Class

        Private Class UsbDriveOption
            Private ReadOnly _diskNumber As Integer
            Private ReadOnly _model As String
            Private ReadOnly _sizeBytes As Long
            Private _driveLetter As String
            Public ReadOnly Property DriveLetter As String
                Get
                    Return _driveLetter
                End Get
            End Property
            Public ReadOnly Property DiskNumber As Integer
                Get
                    Return _diskNumber
                End Get
            End Property
            Public ReadOnly Property Model As String
                Get
                    Return _model
                End Get
            End Property
            Public ReadOnly Property SizeBytes As Long
                Get
                    Return _sizeBytes
                End Get
            End Property

            ''' <summary>Identidade lida do dispositivo quando a lista foi montada.</summary>
            Public Property Identity As DiskIdentity

            Public Sub New(diskNumber As Integer, model As String, sizeBytes As Long, driveLetter As String)
                _diskNumber = diskNumber
                _model = model
                _sizeBytes = sizeBytes
                _driveLetter = NormalizeDriveLetter(driveLetter)
            End Sub

            Public ReadOnly Property RootPath As String
                Get
                    If String.IsNullOrWhiteSpace(DriveLetter) Then
                        Return String.Empty
                    End If
                    Return $"{DriveLetter}\"
                End Get
            End Property

            Public ReadOnly Property DisplayName As String
                Get
                    Dim sizeGb = SizeBytes / (1024.0 * 1024.0 * 1024.0)
                    Dim letterText = If(String.IsNullOrWhiteSpace(DriveLetter), "Sem letra", DriveLetter)
                    Dim diskText = If(DiskNumber < 0, "USB", $"Disk {DiskNumber}")
                    Return $"{diskText} - {Model} ({sizeGb:N1} GB) - {letterText}"
                End Get
            End Property

            Public ReadOnly Property DriveInfo As DriveInfo
                Get
                    If String.IsNullOrWhiteSpace(DriveLetter) Then
                        Return Nothing
                    End If
                    Return New DriveInfo(DriveLetter)
                End Get
            End Property

            Public Sub SetDriveLetter(letter As String)
                _driveLetter = NormalizeDriveLetter(letter)
            End Sub

            Private Function NormalizeDriveLetter(letter As String) As String
                If String.IsNullOrWhiteSpace(letter) Then
                    Return String.Empty
                End If
                Dim trimmed = letter.Trim()
                If trimmed.Length = 1 Then
                    Return trimmed & ":"
                End If
                Return trimmed
            End Function

            Public Overrides Function ToString() As String
                Return DisplayName
            End Function
        End Class
    End Class
End Namespace


' -----------------------------------------------------------------------------
' qdisk | UI / Services | AppUpdateHelper.vb
' Verificacao de atualizacao do aplicativo via manifesto JSON remoto.
'
' Copyright (c) Quallit.
' Licensed under GPL-3.0-or-later. See LICENSE.
' -----------------------------------------------------------------------------

Imports System.Diagnostics
Imports System.Drawing
Imports System.IO
Imports System.Reflection
Imports System.Security.Cryptography
Imports System.Text
Imports System.Threading
Imports System.Threading.Tasks
Imports System.Windows.Forms
Imports qdisk.Core.Services
Imports qdisk.Infrastructure.Infrastructure

Namespace Services
    Friend Module AppUpdateHelper
        Private Enum UpdateUserAction
            Cancel
            Later
            DownloadOnly
            DownloadAndInstall
        End Enum

        ' P4: a verificacao da abertura e a do botao em Configuracoes podiam rodar juntas e baixar o mesmo instalador
        ' temporario (uma recusada com "arquivo em uso"). So uma verificacao por vez.
        Private _checkRunning As Integer

        Public Async Function CheckAndNotifyAsync(owner As IWin32Window,
                                                  showNoUpdateMessage As Boolean,
                                                  cancellationToken As CancellationToken) As Task
            If Interlocked.Exchange(_checkRunning, 1) = 1 Then
                Await New LogService().WriteAsync("APP_UPDATE | CHECK | IGNORADA | Ja existe uma verificacao em andamento")
                If showNoUpdateMessage Then
                    MessageBox.Show("Ja existe uma verificacao de atualizacao em andamento.",
                                    "Atualizacao do aplicativo", MessageBoxButtons.OK, MessageBoxIcon.Information)
                End If
                Return
            End If
            Try
                Await CheckAndNotifyCoreAsync(owner, showNoUpdateMessage, cancellationToken)
            Finally
                Interlocked.Exchange(_checkRunning, 0)
            End Try
        End Function

        Private Async Function CheckAndNotifyCoreAsync(owner As IWin32Window,
                                                       showNoUpdateMessage As Boolean,
                                                       cancellationToken As CancellationToken) As Task
            Dim logService As New LogService()
            Dim currentVersion = Assembly.GetExecutingAssembly().GetName().Version
            Dim client As New UpdateClient()

            Await logService.WriteAsync($"APP_UPDATE | CHECK | INICIO | Current={AppVersionFormat.Format(currentVersion)}")
            Dim result = Await client.CheckForUpdatesAsync(currentVersion, cancellationToken)

            If Not result.Success Then
                Await logService.WriteAsync($"APP_UPDATE | CHECK | INDISPONIVEL | {result.ErrorMessage}")
                If showNoUpdateMessage Then
                    MessageBox.Show($"Nao foi possivel verificar atualizacao agora.{Environment.NewLine}{result.ErrorMessage}",
                                    "Atualizacao do aplicativo",
                                    MessageBoxButtons.OK,
                                    MessageBoxIcon.Information)
                End If
                Return
            End If

            If Not result.UpdateAvailable Then
                Await logService.WriteAsync($"APP_UPDATE | CHECK | ATUAL | Current={AppVersionFormat.Format(result.CurrentVersion)} | Latest={AppVersionFormat.Format(result.LatestVersion)}")
                If showNoUpdateMessage Then
                    MessageBox.Show($"O aplicativo ja esta atualizado.{Environment.NewLine}Versao atual: {AppVersionFormat.Format(result.CurrentVersion)}",
                                    "Atualizacao do aplicativo",
                                    MessageBoxButtons.OK,
                                    MessageBoxIcon.Information)
                End If
                Return
            End If

            Await logService.WriteAsync($"APP_UPDATE | CHECK | DISPONIVEL | Current={AppVersionFormat.Format(result.CurrentVersion)} | Latest={AppVersionFormat.Format(result.LatestVersion)} | Url={result.InstallerUrl}")
            Dim action = ShowUpdateDialog(owner, result)
            If action = UpdateUserAction.Cancel Then
                Await logService.WriteAsync("APP_UPDATE | ACTION | CANCEL")
                Return
            End If
            If action = UpdateUserAction.Later Then
                Await logService.WriteAsync("APP_UPDATE | ACTION | LATER")
                Return
            End If

            Dim installerPath As String = Nothing
            Dim rejection As Exception = Nothing
            Try
                If action = UpdateUserAction.DownloadOnly Then
                    Dim destination = AskDownloadDestination(owner, result)
                    If String.IsNullOrWhiteSpace(destination) Then
                        Await logService.WriteAsync("APP_UPDATE | DOWNLOAD | CANCELADO_PELO_USUARIO")
                        Return
                    End If

                    installerPath = Await DownloadInstallerAsync(result, logService, cancellationToken, destination)
                    ValidateInstaller(installerPath, result)
                    Await logService.WriteAsync($"APP_UPDATE | DOWNLOAD_ONLY | OK | {installerPath}")
                    MessageBox.Show($"Instalador baixado e validado com sucesso:{Environment.NewLine}{installerPath}",
                                    "Atualizacao do aplicativo",
                                    MessageBoxButtons.OK,
                                    MessageBoxIcon.Information)
                    Return
                End If

                installerPath = Await DownloadInstallerAsync(result, logService, cancellationToken)
                ValidateInstaller(installerPath, result)
            Catch ex As Exception When Not TypeOf ex Is OperationCanceledException
                rejection = ex
            End Try

            If rejection IsNot Nothing Then
                ' Instalador que nao passou na verificacao nunca e executado nem mantido em disco.
                Await logService.WriteAsync($"APP_UPDATE | INSTALADOR_RECUSADO | {rejection.Message} | {installerPath}")
                If Not String.IsNullOrWhiteSpace(installerPath) Then
                    Try
                        File.Delete(installerPath)
                    Catch
                    End Try
                End If
                MessageBox.Show($"A atualizacao foi cancelada por seguranca.{Environment.NewLine}{Environment.NewLine}{rejection.Message}",
                                "Atualizacao do aplicativo",
                                MessageBoxButtons.OK,
                                MessageBoxIcon.Warning)
                Return
            End If

            Await logService.WriteAsync($"APP_UPDATE | INSTALLER_DOWNLOADED | {installerPath}")
            StartInstallerAndRelaunchApp(installerPath)
            Await logService.WriteAsync($"APP_UPDATE | INSTALLER_STARTED | {installerPath} | Relaunch={Application.ExecutablePath}")
            Application.Exit()
        End Function

        Private Sub StartInstallerAndRelaunchApp(installerPath As String)
            Dim command = $"/d /c start """" /wait ""{installerPath}"" /SILENT /SUPPRESSMSGBOXES /NORESTART /CLOSEAPPLICATIONS && start """" ""{Application.ExecutablePath}"""
            Process.Start(New ProcessStartInfo("cmd.exe", command) With {
                .UseShellExecute = False,
                .CreateNoWindow = True,
                .WindowStyle = ProcessWindowStyle.Hidden
            })
        End Sub

        Private Async Function DownloadInstallerAsync(result As UpdateCheckResult,
                                                      logService As LogService,
                                                      cancellationToken As CancellationToken,
                                                      Optional destinationPath As String = Nothing) As Task(Of String)
            Dim fileName = GetInstallerFileName(result)

            Dim destination As String
            If String.IsNullOrWhiteSpace(destinationPath) Then
                Dim downloadDir = Path.Combine(Path.GetTempPath(), "qdisk", "updates")
                Directory.CreateDirectory(downloadDir)
                destination = Path.Combine(downloadDir, fileName)
            Else
                destination = destinationPath
                Dim directoryName = Path.GetDirectoryName(destination)
                If Not String.IsNullOrWhiteSpace(directoryName) Then
                    Directory.CreateDirectory(directoryName)
                End If
            End If

            Await logService.WriteAsync($"APP_UPDATE | DOWNLOAD | INICIO | {result.InstallerUrl}")
            Dim downloadService As New DownloadService()
            Return Await downloadService.DownloadAsync(result.InstallerUrl, destination, Nothing, cancellationToken)
        End Function

        ''' <summary>
        ''' Nome do instalador vindo do manifesto ou da URL, sanitizado: so o nome, sem pastas,
        ''' e sempre com extensao .exe. Fora disso, usa um nome padrao.
        ''' </summary>
        Private Function GetInstallerFileName(result As UpdateCheckResult) As String
            Dim candidate = result.Manifest?.FileName
            If String.IsNullOrWhiteSpace(candidate) Then
                candidate = Path.GetFileName(New Uri(result.InstallerUrl).LocalPath)
            End If

            Dim safeName = DownloadService.SanitizeFileName(candidate)
            If String.IsNullOrWhiteSpace(safeName) OrElse Not safeName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) Then
                safeName = $"Setup_qdisk_{AppVersionFormat.Format(result.LatestVersion)}.exe"
            End If
            Return safeName
        End Function

        Private Function AskDownloadDestination(owner As IWin32Window, result As UpdateCheckResult) As String
            Dim fileName = GetInstallerFileName(result)

            Dim downloads = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads")
            If Not Directory.Exists(downloads) Then
                downloads = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory)
            End If

            Using dialog As New SaveFileDialog()
                dialog.Title = "Salvar instalador da atualizacao"
                dialog.InitialDirectory = downloads
                dialog.FileName = fileName
                dialog.Filter = "Instalador (*.exe)|*.exe|Todos os arquivos (*.*)|*.*"
                dialog.OverwritePrompt = True
                dialog.AddExtension = True
                dialog.DefaultExt = "exe"
                If dialog.ShowDialog(owner) = DialogResult.OK Then
                    Return dialog.FileName
                End If
            End Using

            Return String.Empty
        End Function

        Private Function ShowUpdateDialog(owner As IWin32Window, result As UpdateCheckResult) As UpdateUserAction
            Using form As New Form()
                form.Text = "Atualizacao disponivel"
                form.StartPosition = FormStartPosition.CenterParent
                form.FormBorderStyle = FormBorderStyle.FixedDialog
                form.MinimizeBox = False
                form.MaximizeBox = False
                form.ClientSize = New Size(640, 520)
                form.Font = New Font("Segoe UI", 9.0!)
                form.BackColor = Color.White

                Dim root As New TableLayoutPanel() With {
                    .Dock = DockStyle.Fill,
                    .ColumnCount = 1,
                    .RowCount = 4,
                    .Padding = New Padding(24),
                    .BackColor = Color.White
                }
                root.RowStyles.Add(New RowStyle(SizeType.Absolute, 122.0!))
                root.RowStyles.Add(New RowStyle(SizeType.Absolute, 158.0!))
                root.RowStyles.Add(New RowStyle(SizeType.Percent, 100.0!))
                root.RowStyles.Add(New RowStyle(SizeType.Absolute, 54.0!))

                Dim logoHost As New Panel() With {
                    .Dock = DockStyle.Fill,
                    .BackColor = Color.White
                }

                Dim logo As New PictureBox() With {
                    .Size = New Size(108, 108),
                    .SizeMode = PictureBoxSizeMode.Zoom,
                    .BackColor = Color.White
                }
                Dim logoPath = Path.Combine(AppPaths.AssetsDir, "logo-quallit-512x512.png")
                If File.Exists(logoPath) Then
                    logo.Image = Image.FromFile(logoPath)
                End If
                AddHandler logoHost.Resize,
                    Sub()
                        logo.Left = Math.Max(0, (logoHost.ClientSize.Width - logo.Width) \ 2)
                        logo.Top = Math.Max(0, (logoHost.ClientSize.Height - logo.Height) \ 2)
                    End Sub
                logoHost.Controls.Add(logo)

                Dim summary As New TableLayoutPanel() With {
                    .Dock = DockStyle.Fill,
                    .ColumnCount = 1,
                    .RowCount = 5,
                    .BackColor = Color.White
                }
                For index = 0 To 4
                    summary.RowStyles.Add(New RowStyle(SizeType.AutoSize))
                Next

                Dim title As New Label() With {
                    .AutoSize = True,
                    .Dock = DockStyle.Fill,
                    .MaximumSize = New Size(560, 0),
                    .Text = "Nova versao disponivel",
                    .UseMnemonic = False,
                    .BackColor = Color.White,
                    .Font = New Font("Segoe UI", 12.0!, FontStyle.Bold),
                    .ForeColor = Color.Black,
                    .Margin = New Padding(0, 0, 0, 8),
                    .TextAlign = ContentAlignment.TopCenter
                }
                Dim subtitle As New Label() With {
                    .AutoSize = True,
                    .Dock = DockStyle.Fill,
                    .MaximumSize = New Size(560, 0),
                    .Text = "Q-DISK pode ser atualizado agora ou o instalador pode ser salvo para uso posterior.",
                    .UseMnemonic = False,
                    .BackColor = Color.White,
                    .Font = New Font("Segoe UI", 10.0!),
                    .ForeColor = Color.Gray,
                    .Margin = New Padding(0, 0, 0, 8),
                    .TextAlign = ContentAlignment.TopCenter
                }
                Dim currentVersion As New Label() With {
                    .AutoSize = True,
                    .Dock = DockStyle.Fill,
                    .Text = $"Versao atual: {AppVersionFormat.Format(result.CurrentVersion)}",
                    .UseMnemonic = False,
                    .BackColor = Color.White,
                    .Font = New Font("Segoe UI", 9.0!),
                    .ForeColor = Color.Gray,
                    .Margin = New Padding(0, 0, 0, 4),
                    .TextAlign = ContentAlignment.TopCenter
                }
                Dim latestVersion As New Label() With {
                    .AutoSize = True,
                    .Dock = DockStyle.Fill,
                    .Text = $"Versao disponivel: {AppVersionFormat.Format(result.LatestVersion)}",
                    .UseMnemonic = False,
                    .BackColor = Color.White,
                    .Font = New Font("Segoe UI", 10.0!, FontStyle.Bold),
                    .ForeColor = Color.FromArgb(60, 70, 80),
                    .Margin = New Padding(0, 0, 0, 6),
                    .TextAlign = ContentAlignment.TopCenter
                }
                Dim releaseInfo As New Label() With {
                    .AutoSize = True,
                    .Dock = DockStyle.Fill,
                    .Text = BuildUpdateReleaseInfo(result),
                    .UseMnemonic = False,
                    .BackColor = Color.White,
                    .Font = New Font("Segoe UI", 9.0!),
                    .ForeColor = Color.Gray,
                    .Margin = New Padding(0, 0, 0, 8),
                    .TextAlign = ContentAlignment.TopCenter
                }

                summary.Controls.Add(title, 0, 0)
                summary.Controls.Add(subtitle, 0, 1)
                summary.Controls.Add(currentVersion, 0, 2)
                summary.Controls.Add(latestVersion, 0, 3)
                summary.Controls.Add(releaseInfo, 0, 4)

                Dim notesPanel As New Panel() With {
                    .Dock = DockStyle.Fill,
                    .AutoScroll = True,
                    .BackColor = Color.White,
                    .Padding = New Padding(8, 14, 8, 4)
                }

                Dim notesLabel As New Label() With {
                    .AutoSize = True,
                    .MaximumSize = New Size(560, 0),
                    .Text = BuildUpdateNotesText(result),
                    .UseMnemonic = False,
                    .BackColor = Color.White,
                    .ForeColor = Color.Gray,
                    .Font = New Font("Segoe UI", 9.0!)
                }
                notesPanel.Controls.Add(notesLabel)

                Dim buttons As New FlowLayoutPanel() With {
                    .Dock = DockStyle.Fill,
                    .FlowDirection = FlowDirection.RightToLeft,
                    .Padding = New Padding(0, 12, 0, 0),
                    .BackColor = Color.White
                }

                Dim action = UpdateUserAction.Cancel
                AddActionButton(buttons, "Baixar e instalar", Sub() action = UpdateUserAction.DownloadAndInstall, form)
                AddActionButton(buttons, "Apenas baixar", Sub() action = UpdateUserAction.DownloadOnly, form)
                AddActionButton(buttons, "Depois", Sub() action = UpdateUserAction.Later, form)
                AddActionButton(buttons, "Cancelar", Sub() action = UpdateUserAction.Cancel, form)

                root.Controls.Add(logoHost, 0, 0)
                root.Controls.Add(summary, 0, 1)
                root.Controls.Add(notesPanel, 0, 2)
                root.Controls.Add(buttons, 0, 3)
                form.Controls.Add(root)
                form.AcceptButton = TryCast(buttons.Controls(0), Button)
                form.CancelButton = TryCast(buttons.Controls(3), Button)
                form.ShowDialog(owner)
                Return action
            End Using
        End Function

        Private Function BuildUpdateReleaseInfo(result As UpdateCheckResult) As String
            If result.Manifest Is Nothing Then
                Return String.Empty
            End If

            Dim parts As New List(Of String)()
            If Not String.IsNullOrWhiteSpace(result.Manifest.ReleaseDate) Then
                parts.Add($"Data: {result.Manifest.ReleaseDate}")
            End If
            If result.Manifest.Mandatory Then
                parts.Add("Atualizacao obrigatoria")
            End If
            Return String.Join("  |  ", parts)
        End Function

        Private Function BuildUpdateNotesText(result As UpdateCheckResult) As String
            If result.Manifest Is Nothing OrElse result.Manifest.Notes Is Nothing OrElse result.Manifest.Notes.Count = 0 Then
                Return "Notas da versao nao foram informadas no manifesto."
            End If

            Dim builder As New StringBuilder()
            builder.AppendLine("Notas da versao:")
            For Each note In result.Manifest.Notes.Take(6)
                builder.AppendLine($"- {note}")
            Next
            Return builder.ToString().TrimEnd()
        End Function

        Private Sub AddActionButton(panel As FlowLayoutPanel, text As String, setAction As Action, owner As Form)
            Dim button As New Button() With {
                .Text = text,
                .Width = 125,
                .Height = 30,
                .Margin = New Padding(4)
            }
            AddHandler button.Click,
                Sub()
                    setAction()
                    owner.Close()
                End Sub
            panel.Controls.Add(button)
        End Sub

        ''' <summary>
        ''' Checagens obrigatorias antes de executar ou entregar o instalador:
        ''' SHA-256 do manifesto e assinatura Authenticode de um certificado autorizado.
        ''' </summary>
        Private Sub ValidateInstaller(installerPath As String, result As UpdateCheckResult)
            ValidateInstallerHash(installerPath, result)
            AuthenticodeVerifier.EnsureTrusted(installerPath)
        End Sub

        Private Sub ValidateInstallerHash(installerPath As String, result As UpdateCheckResult)
            Dim expected = result.Manifest?.Sha256
            If String.IsNullOrWhiteSpace(expected) Then
                Throw New InvalidOperationException("O manifesto de atualizacao nao informa o SHA-256 do instalador.")
            End If

            Using stream = File.OpenRead(installerPath)
                Using sha = SHA256.Create()
                    Dim hash = sha.ComputeHash(stream)
                    Dim actual = BitConverter.ToString(hash).Replace("-", String.Empty).ToLowerInvariant()
                    If Not String.Equals(expected.Trim(), actual, StringComparison.OrdinalIgnoreCase) Then
                        Throw New InvalidOperationException("SHA-256 do instalador baixado nao confere com o manifesto.")
                    End If
                End Using
            End Using
        End Sub

        Private Function BuildUpdateMessage(result As UpdateCheckResult) As String
            Dim builder As New StringBuilder()
            builder.AppendLine("Existe uma nova versao do Q-DISK.")
            builder.AppendLine()
            builder.AppendLine($"Versao atual: {AppVersionFormat.Format(result.CurrentVersion)}")
            builder.AppendLine($"Versao disponivel: {AppVersionFormat.Format(result.LatestVersion)}")

            If result.Manifest IsNot Nothing Then
                If Not String.IsNullOrWhiteSpace(result.Manifest.ReleaseDate) Then
                    builder.AppendLine($"Data: {result.Manifest.ReleaseDate}")
                End If
                If result.Manifest.Mandatory Then
                    builder.AppendLine("Atualizacao marcada como obrigatoria.")
                End If
                If result.Manifest.Notes IsNot Nothing AndAlso result.Manifest.Notes.Count > 0 Then
                    builder.AppendLine()
                    builder.AppendLine("Notas:")
                    For Each note In result.Manifest.Notes.Take(5)
                        builder.AppendLine($"- {note}")
                    Next
                End If
            End If

            builder.AppendLine()
            builder.AppendLine("Escolha se deseja baixar e instalar agora, apenas salvar o instalador, deixar para depois ou cancelar.")
            Return builder.ToString()
        End Function
    End Module
End Namespace

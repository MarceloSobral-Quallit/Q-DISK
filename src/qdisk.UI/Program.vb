' ─────────────────────────────────────────────────────────────────────────────
' qdisk  |  UI  |  Program.vb
' Ponto de entrada — resolve token via args, valida perfil e inicializa MainForm.
'
' © Quallit — Licenciado sob GPL-3.0-or-later.
' ─────────────────────────────────────────────────────────────────────────────

Imports System.Drawing
Imports System.Diagnostics
Imports System.IO
Imports System.Reflection
Imports System.Threading
Imports System.Windows.Forms
Imports System.Security.Principal
Imports qdisk.UI.Views
Imports qdisk.Core.Services
Imports qdisk.Core

''' <summary>
''' Centraliza os caminhos de runtime dos artefatos extraídos para AppData.
''' </summary>
Friend Module AppPaths
    Private ReadOnly _dataRoot As String = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "qdisk")

    ''' <summary>Raiz: %LOCALAPPDATA%\qdisk\</summary>
    Public ReadOnly Property DataRoot As String
        Get
            Return _dataRoot
        End Get
    End Property

    ''' <summary>%LOCALAPPDATA%\qdisk\assets\</summary>
    Public ReadOnly Property AssetsDir As String
        Get
            Return Path.Combine(_dataRoot, "assets")
        End Get
    End Property

    ''' <summary>%LOCALAPPDATA%\qdisk\Data\</summary>
    Public ReadOnly Property DataDir As String
        Get
            Return Path.Combine(_dataRoot, "Data")
        End Get
    End Property

    ''' <summary>Pasta configurável para cache de imagens ISO/TIBX.</summary>
    Public ReadOnly Property CacheDir As String
        Get
            Return SettingsService.ResolveImageCacheDir(New SettingsService().Load().ImageCacheDir)
        End Get
    End Property

    ''' <summary>%LOCALAPPDATA%\qdisk\config\</summary>
    Public ReadOnly Property ConfigDir As String
        Get
            Return Path.Combine(_dataRoot, "config")
        End Get
    End Property

    ''' <summary>%LOCALAPPDATA%\qdisk\manifests\</summary>
    Public ReadOnly Property ManifestCacheDir As String
        Get
            Return Path.Combine(_dataRoot, "manifests")
        End Get
    End Property

    ''' <summary>%LOCALAPPDATA%\qdisk\config	oken_attempts.dat (limite de tentativas do token)</summary>
    Public ReadOnly Property TokenAttemptsPath As String
        Get
            Return Path.Combine(_dataRoot, "config", "token_attempts.dat")
        End Get
    End Property

    ''' <summary>%LOCALAPPDATA%\qdisk\config\download_status.json</summary>
    Public ReadOnly Property DownloadStatusPath As String
        Get
            Return Path.Combine(_dataRoot, "config", "download_status.json")
        End Get
    End Property

    ''' <summary>%LOCALAPPDATA%\qdisk\logs\app.log</summary>
    Public ReadOnly Property LogPath As String
        Get
            Return Path.Combine(_dataRoot, "logs", "app.log")
        End Get
    End Property
End Module

Friend Module Program

    ' Prefixo dos recursos embarcados (EmbeddedResource com LogicalName)
    Private Const EmbeddedPrefix As String = "_embedded\"
    Private Const SingleInstanceMutexName As String = "Local\qdisk.SingleInstance"

    <STAThread()>
    Friend Sub Main(args As String())
        Dim singleInstanceMutex As Mutex = Nothing
        Dim ownsSingleInstanceMutex As Boolean = False

        Try
            Application.SetHighDpiMode(HighDpiMode.SystemAware)
            Application.EnableVisualStyles()
            Application.SetCompatibleTextRenderingDefault(False)

            If Not TryAcquireSingleInstance(singleInstanceMutex, ownsSingleInstanceMutex) Then
                Return
            End If

            ' Garante que assets e catálogos estejam extraídos em AppData
            If Not EnsureAssetsExtracted() Then Return

            ' Verificar se token foi fornecido via linha de comando
            Dim tokenFromArgs = ExtractTokenFromArgs(args)

            If Not String.IsNullOrEmpty(tokenFromArgs) Then
                Console.WriteLine($"Token fornecido via linha de comando: {tokenFromArgs.Substring(0, Math.Min(3, tokenFromArgs.Length))}***")

                Dim tokenService As New TokenValidationService()
                Dim limiter As New TokenAttemptLimiter(AppPaths.TokenAttemptsPath)
                Dim profile = Core.Models.UserProfile.None
                If limiter.RemainingLock(DateTime.UtcNow) > TimeSpan.Zero Then
                    Console.WriteLine("Token via linha de comando ignorado: muitas tentativas incorretas, aguarde.")
                Else
                    profile = tokenService.ResolveProfile(tokenFromArgs)
                    If profile = Core.Models.UserProfile.None Then
                        limiter.RegisterFailure(DateTime.UtcNow)
                    Else
                        limiter.RegisterSuccess()
                    End If
                End If

                If profile <> Core.Models.UserProfile.None Then
                    TokenManager.SetTokenWithProfile(tokenFromArgs, True, profile)
                    Console.WriteLine($"Token válido via linha de comando. Perfil: {profile}. Iniciando aplicativo principal...")

                    If Not RunMainFormAndCheckProfileChange() Then
                        Return
                    End If

                    ' Usuário pediu para trocar de perfil — limpa o token e cai no laço abaixo,
                    ' que reexibe o TokenPromptForm em vez de reaproveitar o token da linha de comando.
                    TokenManager.ClearToken()
                Else
                    Console.WriteLine("Token inválido fornecido via linha de comando.")
                End If
            End If

            ' Mostra o formulário de token; repete enquanto o usuário pedir para trocar de perfil.
            Dim requestAnotherRound As Boolean = True
            Do While requestAnotherRound
                requestAnotherRound = False

                Using tokenForm As New TokenPromptForm()
                    Dim result = tokenForm.ShowDialog()

                    If result = DialogResult.OK AndAlso tokenForm.IsTokenValid() Then
                        Console.WriteLine("Iniciando MainForm...")
                        requestAnotherRound = RunMainFormAndCheckProfileChange()
                        If requestAnotherRound Then
                            TokenManager.ClearToken()
                        End If
                    Else
                        ' Token inválido ou cancelado, encerrar aplicativo
                        Console.WriteLine("Token inválido ou cancelado")
                        Return
                    End If
                End Using
            Loop
        Catch ex As Exception
            Console.WriteLine($"ERRO GERAL: {ex.Message}")
            Console.WriteLine($"STACK TRACE: {ex.StackTrace}")
            Console.WriteLine($"INNER EXCEPTION: {If(ex.InnerException?.Message, "Nenhuma")}")
            MessageBox.Show($"Erro ao iniciar o aplicativo: {ex.Message}{Environment.NewLine}{Environment.NewLine}Stack Trace:{Environment.NewLine}{ex.StackTrace}", 
                          "Erro de Inicialização", MessageBoxButtons.OK, MessageBoxIcon.Error)
        Finally
            If ownsSingleInstanceMutex AndAlso singleInstanceMutex IsNot Nothing Then
                Try
                    singleInstanceMutex.ReleaseMutex()
                Catch
                End Try
            End If

            singleInstanceMutex?.Dispose()
        End Try
    End Sub

    ''' <summary>
    ''' Cria e executa a MainForm; retorna True se o usuário pediu para trocar de perfil
    ''' (botão "Alterar Perfil"), para que o chamador reexiba o TokenPromptForm.
    ''' </summary>
    Private Function RunMainFormAndCheckProfileChange() As Boolean
        Dim mainForm As New Views.MainForm()
        Try
            Application.Run(mainForm)
        Catch mainEx As Exception
            Console.WriteLine($"ERRO NO MAINFORM: {mainEx.Message}")
            Console.WriteLine($"STACK TRACE: {mainEx.StackTrace}")
            Console.WriteLine($"INNER EXCEPTION: {If(mainEx.InnerException?.Message, "Nenhuma")}")
            Throw
        End Try
        Return mainForm.ProfileChangeRequested
    End Function

    Private Function TryAcquireSingleInstance(ByRef singleInstanceMutex As Mutex, ByRef ownsSingleInstanceMutex As Boolean) As Boolean
        singleInstanceMutex = New Mutex(False, SingleInstanceMutexName)

        Try
            ownsSingleInstanceMutex = singleInstanceMutex.WaitOne(0, False)
        Catch ex As AbandonedMutexException
            ownsSingleInstanceMutex = True
        End Try

        If ownsSingleInstanceMutex Then
            Return True
        End If

        Dim result = MessageBox.Show(
            "Ja existe uma instancia do Q-DISK em execucao." & Environment.NewLine & Environment.NewLine &
            "Clique em Sim para encerrar a outra instancia e continuar nesta." & Environment.NewLine &
            "Clique em Nao para sair desta nova abertura.",
            "Q-DISK ja esta aberto",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning,
            MessageBoxDefaultButton.Button2)

        If result <> DialogResult.Yes Then
            Return False
        End If

        If Not KillOtherInstances() Then
            Return False
        End If

        Try
            ownsSingleInstanceMutex = singleInstanceMutex.WaitOne(TimeSpan.FromSeconds(5), False)
        Catch ex As AbandonedMutexException
            ownsSingleInstanceMutex = True
        End Try

        If ownsSingleInstanceMutex Then
            Return True
        End If

        MessageBox.Show(
            "Nao foi possivel liberar a instancia anterior. Feche o Q-DISK manualmente e tente novamente.",
            "Q-DISK ja esta aberto",
            MessageBoxButtons.OK,
            MessageBoxIcon.Warning)
        Return False
    End Function

    Private Function KillOtherInstances() As Boolean
        Dim currentProcess = Process.GetCurrentProcess()
        Dim currentProcessId = currentProcess.Id
        Dim currentProcessName = currentProcess.ProcessName
        Dim failures As New List(Of String)()

        For Each otherProcess As Process In Process.GetProcessesByName(currentProcessName)
            If otherProcess.Id = currentProcessId Then
                Continue For
            End If

            Try
                If Not otherProcess.HasExited Then
                    otherProcess.Kill()
                    otherProcess.WaitForExit(5000)
                End If
            Catch ex As Exception
                failures.Add($"PID {otherProcess.Id}: {ex.Message}")
            Finally
                otherProcess.Dispose()
            End Try
        Next

        If failures.Count = 0 Then
            Return True
        End If

        MessageBox.Show(
            "Falha ao encerrar a instancia anterior:" & Environment.NewLine & String.Join(Environment.NewLine, failures),
            "Q-DISK ja esta aberto",
            MessageBoxButtons.OK,
            MessageBoxIcon.Error)
        Return False
    End Function
    
    ''' <summary>
    ''' Extrai recursos embarcados (assets/ e Data/) para AppData se a versão
    ''' instalada diferir da versão do executável. Executado uma única vez por versão.
    ''' </summary>
    Private Function EnsureAssetsExtracted() As Boolean
        Dim asm = Assembly.GetEntryAssembly()
        ' Versao + MVID: o MVID muda a cada build com conteudo diferente, entao um manual ou catalogo
        ' atualizado sem trocar a versao tambem e extraido de novo.
        Dim currentVersion = $"{asm.GetName().Version}|{asm.ManifestModule.ModuleVersionId}"
        Dim versionFile = Path.Combine(AppPaths.DataRoot, "app_version.txt")
        Dim resourceNames = asm.GetManifestResourceNames()

        ' Nenhuma extração necessária se a versão já está instalada
        If File.Exists(versionFile) Then
            Dim storedVersion = File.ReadAllText(versionFile, System.Text.Encoding.UTF8).Trim()
            If storedVersion = currentVersion AndAlso EmbeddedResourcesArePresent(resourceNames) Then Return True
        End If

        Try
            Directory.CreateDirectory(AppPaths.DataRoot)
            Dim count As Integer = 0

            For Each name In resourceNames
                If Not name.StartsWith(EmbeddedPrefix, StringComparison.Ordinal) Then Continue For

                ' "assets\APP_BG.jpg" ou "Data\qdisk.catalog.rd.json"
                Dim relativePath = name.Substring(EmbeddedPrefix.Length)
                Dim targetPath = Path.Combine(AppPaths.DataRoot, relativePath)
                Directory.CreateDirectory(Path.GetDirectoryName(targetPath))

                Using stream = asm.GetManifestResourceStream(name)
                    Using fs As New FileStream(targetPath, FileMode.Create, FileAccess.Write, FileShare.None)
                        stream.CopyTo(fs)
                    End Using
                End Using
                count += 1
            Next

            File.WriteAllText(versionFile, currentVersion, System.Text.Encoding.UTF8)
            Console.WriteLine($"[AppPaths] {count} recurso(s) extraído(s) → {AppPaths.DataRoot}")
            Return True

        Catch ex As Exception
            MessageBox.Show(
                $"Falha ao extrair recursos do aplicativo:{Environment.NewLine}{ex.Message}" &
                $"{Environment.NewLine}{Environment.NewLine}Caminho de destino: {AppPaths.DataRoot}",
                "Q-DISK — Erro de inicialização",
                MessageBoxButtons.OK, MessageBoxIcon.Error)
            Return False
        End Try
    End Function

    Private Function EmbeddedResourcesArePresent(resourceNames As String()) As Boolean
        For Each name In resourceNames
            If Not name.StartsWith(EmbeddedPrefix, StringComparison.Ordinal) Then Continue For
            Dim relativePath = name.Substring(EmbeddedPrefix.Length)
            If Not File.Exists(Path.Combine(AppPaths.DataRoot, relativePath)) Then Return False
        Next
        Return True
    End Function

    ''' <summary>
    ''' Extrai o token dos argumentos de linha de comando
    ''' Suporta os formatos: --token=SEU_TOKEN, /token=SEU_TOKEN, --token SEU_TOKEN
    ''' </summary>
    ''' <param name="args">Array de argumentos de linha de comando</param>
    ''' <returns>Token extraído ou Nothing se não encontrado</returns>
    Private Function ExtractTokenFromArgs(args As String()) As String
        If args Is Nothing OrElse args.Length = 0 Then
            Return Nothing
        End If
        
        For i As Integer = 0 To args.Length - 1
            Dim arg = args(i).Trim()
            
            ' Verificar formatos: --token=SEU_TOKEN ou /token=SEU_TOKEN
            If arg.StartsWith("--token=", StringComparison.OrdinalIgnoreCase) OrElse 
               arg.StartsWith("/token=", StringComparison.OrdinalIgnoreCase) Then
                Return arg.Substring(arg.IndexOf("="c) + 1).Trim()
            End If
            
            ' Verificar formatos: --token SEU_TOKEN ou /token SEU_TOKEN
            If (arg.Equals("--token", StringComparison.OrdinalIgnoreCase) OrElse 
                arg.Equals("/token", StringComparison.OrdinalIgnoreCase)) AndAlso 
               i + 1 < args.Length Then
                Return args(i + 1).Trim()
            End If
        Next
        
        Return Nothing
    End Function

End Module


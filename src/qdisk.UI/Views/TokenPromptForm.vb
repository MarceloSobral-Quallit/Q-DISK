' ─────────────────────────────────────────────────────────────────────────────
' qdisk  |  UI / Views  |  TokenPromptForm.vb
' Formulário de entrada e validação do token diário de acesso.
'
' © Quallit — Licenciado sob GPL-3.0-or-later.
' ─────────────────────────────────────────────────────────────────────────────

Imports System.Drawing
Imports System.IO
Imports System.Reflection
Imports System.Windows.Forms
Imports qdisk.UI.Theme
Imports qdisk.Core.Services
Imports qdisk.Core

Namespace Views
    Public Class TokenPromptForm
        Inherits Form
        
        ' Controles do formulário
        Private WithEvents picLogo As PictureBox
        Private WithEvents lblTitle As Label
        Private WithEvents lblVersion As Label
        Private WithEvents lblInstruction As Label
        Private WithEvents txtToken As TextBox
        Private WithEvents btnValidate As Button
        Private WithEvents btnExit As Button
        Private WithEvents lblInfo As Label
        Private lblError As Label
        
        Private ReadOnly _tokenService As TokenValidationService
        Private ReadOnly _attemptLimiter As New TokenAttemptLimiter(AppPaths.TokenAttemptsPath)
        Private _isTokenValid As Boolean = False
        
        Public Sub New()
            _tokenService = New TokenValidationService()
            InitializeComponent()
            SetupForm()
        End Sub
        
        Private Sub InitializeComponent()
            ' Configurações do formulário
            Text = "Autenticação"
            Size = New Size(420, 280)
            FormBorderStyle = FormBorderStyle.FixedDialog
            MaximizeBox = False
            MinimizeBox = False
            StartPosition = FormStartPosition.CenterScreen
            Font = New Font("Segoe UI", 9)
            
            ' Logo Quallit
            picLogo = New PictureBox() With {
                .Size = New Size(80, 80),
                .Location = New Point(20, 15),
                .SizeMode = PictureBoxSizeMode.Zoom,
                .BorderStyle = BorderStyle.None
            }
            
            ' Carregar e redimensionar logo
            Try
                Dim logoPath = Path.Combine(AppPaths.AssetsDir, "logo-quallit-512x512.png")
                If File.Exists(logoPath) Then
                    Using originalImage As Image = Image.FromFile(logoPath)
                        ' Redimensionar proporcionalmente para 80x80
                        Dim resizedImage As New Bitmap(80, 80)
                        Using g As Graphics = Graphics.FromImage(resizedImage)
                            g.InterpolationMode = Drawing2D.InterpolationMode.HighQualityBicubic
                            g.DrawImage(originalImage, 0, 0, 80, 80)
                        End Using
                        picLogo.Image = resizedImage
                    End Using
                End If
            Catch
                ' Se não carregar, continua sem logo
            End Try
            
            ' Título
            ' // .Font = New Font("Segoe UI", 12, FontStyle.Bold),
            lblTitle = New Label() With {
                .Text = "Q-DISK",
                .Font = New Font("Segoe UI", 12, FontStyle.Bold),
                .Location = New Point(110, 30),
                .Size = New Size(320, 25),
                .TextAlign = ContentAlignment.MiddleLeft
            }

            lblVersion = New Label() With {
                .Text = GetVersionInfo(),
                .Font = New Font("Segoe UI", 8),
                .Location = New Point(110, 58),
                .Size = New Size(290, 35),
                .ForeColor = Color.FromArgb(108, 117, 125),
                .TextAlign = ContentAlignment.MiddleLeft
            }
            
            ' Instrução principal
            lblInstruction = New Label() With {
                .Text = "Insira o Token.",
                .Font = New Font("Segoe UI", 10),
                .Location = New Point(20, 110),
                .Size = New Size(400, 25),
                .ForeColor = Color.FromArgb(33, 37, 41)
            }
            
            ' Campo de token
            txtToken = New TextBox() With {
                .Location = New Point(20, 140),
                .Size = New Size(188, 30),
                .MaxLength = 6,
                .TextAlign = HorizontalAlignment.Center,
                .Font = New Font("Segoe UI", 10)
            }
            
            ' Botão validar
            btnValidate = New Button() With {
                .Text = "Validar",
                .Location = New Point(220, 140),
                .Size = New Size(75, 30),
                .Font = New Font("Segoe UI", 9),
                .BackColor = Color.FromArgb(40, 167, 69),
                .ForeColor = Color.White,
                .FlatStyle = FlatStyle.Flat
            }
            
            ' Botão sair
            btnExit = New Button() With {
                .Text = "Sair",
                .Location = New Point(305, 140),
                .Size = New Size(75, 30),
                .Font = New Font("Segoe UI", 9),
                .BackColor = Color.FromArgb(220, 53, 69),
                .ForeColor = Color.White,
                .FlatStyle = FlatStyle.Flat
            }
            
            ' Informações
            lblInfo = New Label() With {
                .Text = "* O Token do Q-DISK pode ser obtido no Aplicativo do Tecnico Quallit.",
                .Location = New Point(20, 180),
                .Size = New Size(380, 40),
                .ForeColor = Color.FromArgb(108, 117, 125),
                .Font = New Font("Segoe UI", 8)
            }
            
            ' Adicionar controles
            Controls.AddRange({picLogo, lblTitle, lblVersion, lblInstruction, txtToken, btnValidate, btnExit, lblInfo})
            
            ' Configurações finais
            AcceptButton = btnValidate
            txtToken.Select()
        End Sub
        
        Private Sub SetupForm()
            ' Event handlers já configurados via WithEvents
            
            ' Aplicar bordas arredondadas
            ' Redesenho (HANDOFF 5.1): cantos retos, botoes do tema e erro abaixo do campo.
            ApplyRedesign()
        End Sub

        Private Sub ApplyRedesign()
            SuspendLayout()
            BackColor = Color.White
            picLogo.Size = New Size(56, 56)
            picLogo.Location = New Point(20, 18)
            lblTitle.Location = New Point(90, 20)
            lblTitle.ForeColor = UiTheme.Navy
            lblVersion.Location = New Point(90, 44)
            lblVersion.Size = New Size(290, 34)
            lblVersion.ForeColor = UiTheme.MutedText

            Dim divider = UiTheme.CreateDivider()
            divider.Dock = DockStyle.None
            divider.Bounds = New Rectangle(20, 88, ClientSize.Width - 40, 1)
            Controls.Add(divider)

            lblInstruction.Location = New Point(20, 100)
            lblInstruction.ForeColor = UiTheme.MainText

            ' Campo em contorno arredondado de 32 px (mesma altura dos botoes); a borda muda no erro
            txtToken.Font = New Font("Segoe UI", 11.0F)
            Dim fieldBox = RoundedInputHost.Wrap(txtToken, 192)
            fieldBox.Location = New Point(20, 128)
            AddHandler txtToken.TextChanged, Sub()
                                                 If txtToken.Text.Length > 0 AndAlso lblError.Text.Length > 0 Then ShowTokenError(String.Empty)
                                             End Sub

            UiTheme.StyleButton(btnValidate, ButtonKind.Primary)
            btnValidate.Bounds = New Rectangle(220, 128, 84, UiTheme.ButtonHeight)
            UiTheme.StyleButton(btnExit, ButtonKind.Secondary)
            btnExit.Bounds = New Rectangle(312, 128, 72, UiTheme.ButtonHeight)

            lblError = New Label() With {
                .Location = New Point(20, 164), .Size = New Size(364, 20), .Font = New Font("Segoe UI", 8.0F, FontStyle.Bold),
                .ForeColor = UiTheme.WarnText, .ImageAlign = ContentAlignment.MiddleLeft, .TextAlign = ContentAlignment.MiddleLeft, .Text = String.Empty
            }
            Controls.Add(lblError)
            _fieldBox = fieldBox

            lblInfo.Location = New Point(20, 192)
            lblInfo.Size = New Size(370, 34)
            ResumeLayout(True)
            txtToken.Select()
        End Sub

        Private _fieldBox As RoundedInputHost

        ''' <summary>Mostra (ou limpa) a mensagem de erro abaixo do campo e destaca a borda.</summary>
        Private Sub ShowTokenError(message As String)
            Dim hasError = Not String.IsNullOrEmpty(message)
            lblError.Image = If(hasError, UiTheme.Icon16(Me, "alertc", "warn"), Nothing)
            lblError.Text = If(hasError, "     " & message, String.Empty)
            If _fieldBox IsNot Nothing Then
                _fieldBox.NormalBorderColor = If(hasError, UiTheme.WarnText, UiRounded.InputBorder)
                _fieldBox.BorderColor = If(hasError, UiTheme.WarnText, If(txtToken.Focused, UiRounded.InputFocusBorder, UiRounded.InputBorder))
            End If
        End Sub
        
        Private Sub ApplyRoundedBorder(control As Control, radius As Integer)
            ' Criar região arredondada
            Dim path As New Drawing2D.GraphicsPath()
            Dim rect As New Rectangle(0, 0, control.Width, control.Height)
            
            ' Adicionar arcos para cantos arredondados
            path.AddArc(rect.X, rect.Y, radius, radius, 180, 90)
            path.AddArc(rect.Right - radius, rect.Y, radius, radius, 270, 90)
            path.AddArc(rect.Right - radius, rect.Bottom - radius, radius, radius, 0, 90)
            path.AddArc(rect.X, rect.Bottom - radius, radius, radius, 90, 90)
            path.CloseFigure()
            
            control.Region = New Region(path)

            ' Desenhar borda cinza para botões
            If TypeOf control Is Button Then
                AddHandler control.Paint, Sub(sender As Object, e As PaintEventArgs)
                    e.Graphics.SmoothingMode = Drawing2D.SmoothingMode.AntiAlias
                    Using pen As New Pen(Color.FromArgb(200, 200, 200), 1)
                        e.Graphics.DrawPath(pen, path)
                    End Using
                End Sub
            End If
        End Sub

        Private Function GetVersionInfo() As String
            Dim version = Assembly.GetExecutingAssembly().GetName().Version
            Dim versionText = If(AppVersionFormat.Format(version), "desconhecida")

            Dim buildDate = "desconhecida"
            For Each attr As AssemblyMetadataAttribute In Assembly.GetExecutingAssembly().GetCustomAttributes(GetType(AssemblyMetadataAttribute), False)
                If attr.Key = "BuildDate" Then
                    buildDate = attr.Value
                    Exit For
                End If
            Next

            Return $"Versao: {versionText}{Environment.NewLine}Build: {buildDate}"
        End Function
        
        Private Sub btnValidate_Click(sender As Object, e As EventArgs) Handles btnValidate.Click
            ValidateToken()
        End Sub
        
        Private Sub btnExit_Click(sender As Object, e As EventArgs) Handles btnExit.Click
            DialogResult = DialogResult.Cancel
            Close()
        End Sub
        
        Private Sub txtToken_KeyDown(sender As Object, e As KeyEventArgs) Handles txtToken.KeyDown
            If e.KeyCode = Keys.Enter Then
                ValidateToken()
            ElseIf e.KeyCode = Keys.Escape Then
                DialogResult = DialogResult.Cancel
                Close()
            End If
        End Sub
        
        Private Sub txtToken_TextChanged(sender As Object, e As EventArgs) Handles txtToken.TextChanged
            ' Remove caracteres não numéricos automaticamente
            Dim cursorPos = txtToken.SelectionStart
            Dim cleanText = System.Text.RegularExpressions.Regex.Replace(txtToken.Text, "\D", "")
            
            If cleanText.Length > 6 Then
                cleanText = cleanText.Substring(0, 6)
            End If
            
            If txtToken.Text <> cleanText Then
                txtToken.Text = cleanText
                txtToken.SelectionStart = Math.Min(cursorPos, cleanText.Length)
            End If
        End Sub
        
        Private Sub ValidateToken()
            Dim inputToken = txtToken.Text.Trim()
            
            If String.IsNullOrWhiteSpace(inputToken) Then
                ShowTokenError("Por favor, insira um token.")
                MessageBox.Show("Por favor, insira um token.", "Token obrigatório", MessageBoxButtons.OK, MessageBoxIcon.Warning)
                txtToken.Select()
                Return
            End If
            
            If inputToken.Length <> 6 Then
                ShowTokenError("O token deve ter exatamente 6 dígitos.")
                MessageBox.Show("O token deve ter exatamente 6 dígitos.", "Token inválido", MessageBoxButtons.OK, MessageBoxIcon.Warning)
                txtToken.Select()
                Return
            End If
            
            ' Limite de tentativas: depois de alguns erros seguidos, espera crescente (vale entre reinicios do app).
            Dim remaining = _attemptLimiter.RemainingLock(DateTime.UtcNow)
            If remaining > TimeSpan.Zero Then
                Dim waitText = FormatWait(remaining)
                ShowTokenError($"Muitas tentativas incorretas. Aguarde {waitText}.")
                MessageBox.Show($"Muitas tentativas incorretas. Aguarde {waitText} para tentar de novo.", "Token bloqueado", MessageBoxButtons.OK, MessageBoxIcon.Warning)
                txtToken.Select()
                Return
            End If

            Dim profile = _tokenService.ResolveProfile(inputToken)
            If profile <> Core.Models.UserProfile.None Then
                _attemptLimiter.RegisterSuccess()
                _isTokenValid = True
                TokenManager.SetTokenWithProfile(inputToken, False, profile)

                DialogResult = DialogResult.OK
                Close()
            ElseIf _tokenService.VerifiersExpired Then
                ShowTokenError("Esta versao do aplicativo nao valida tokens de hoje.")
                MessageBox.Show("Esta versao do aplicativo nao reconhece os tokens da data de hoje. Instale a versao mais recente do Q-DISK.",
                                "Atualizacao necessaria", MessageBoxButtons.OK, MessageBoxIcon.Warning)
                _isTokenValid = False
            Else
                _attemptLimiter.RegisterFailure(DateTime.UtcNow)
                Dim locked = _attemptLimiter.RemainingLock(DateTime.UtcNow)
                Dim message = If(locked > TimeSpan.Zero,
                                 $"Token incorreto. Muitas tentativas: aguarde {FormatWait(locked)} para tentar de novo.",
                                 "Token incorreto. Por favor, verifique e tente novamente.")
                ShowTokenError(message)
                MessageBox.Show(message, "Token inválido", MessageBoxButtons.OK, MessageBoxIcon.Error)
                _isTokenValid = False
                txtToken.Clear()
                txtToken.Select()
            End If
        End Sub
        
        Private Shared Function FormatWait(value As TimeSpan) As String
            If value.TotalSeconds < 60 Then
                Return $"{Math.Ceiling(value.TotalSeconds)} s"
            End If
            Return $"{Math.Ceiling(value.TotalMinutes)} min"
        End Function

        Protected Overrides Sub OnFormClosing(e As FormClosingEventArgs)
            If Not _isTokenValid AndAlso DialogResult <> DialogResult.Abort Then
                DialogResult = DialogResult.Cancel
            End If
            MyBase.OnFormClosing(e)
        End Sub
        
        Public Function IsTokenValid() As Boolean
            Return _isTokenValid
        End Function
    End Class
End Namespace


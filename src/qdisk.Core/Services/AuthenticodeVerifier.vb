' ─────────────────────────────────────────────────────────────────────────────
' qdisk  |  Core / Services  |  AuthenticodeVerifier.vb
' Verifica a assinatura Authenticode de um executavel antes de executa-lo.
'
' © Quallit — Licenciado sob GPL-3.0-or-later.
' ─────────────────────────────────────────────────────────────────────────────

Imports System.IO
Imports System.Runtime.InteropServices
Imports System.Security.Cryptography.X509Certificates

Namespace Services
    ''' <summary>
    ''' Duas checagens, as duas obrigatorias:
    ''' 1. Integridade: o WinVerifyTrust confirma que o arquivo tem assinatura e que ela
    '''    nao foi alterada. Raiz nao confiavel continua aceita (maquina sem a raiz da
    '''    DigiCert atualizada): quem barra outro assinante e a checagem 2.
    ''' 2. Assinante: a impressao digital do certificado precisa estar em
    '''    <see cref="TrustedSignerThumbprints"/>. E isso que impede um instalador
    '''    assinado por outra pessoa.
    ''' </summary>
    Public NotInheritable Class AuthenticodeVerifier
        ''' <summary>
        ''' Certificados aceitos para os instaladores do qdisk.
        ''' Ao trocar ou renovar o certificado (a renovacao muda a impressao digital), inclua a nova
        ''' impressao digital aqui e publique essa versao do app ANTES de distribuir instaladores
        ''' com o certificado novo. O PFX autoassinado (C991A777...) saiu na 1.00.
        ''' </summary>
        Public Shared ReadOnly TrustedSignerThumbprints As IReadOnlyList(Of String) = New String() {
            "5D4730113CFC90891D3AEC06630F53EE6DF14B79" ' CN=QUALLIT SERVICOS DE INFORMATICA LTDA (OV DigiCert, chave no Cloud KMS), valido ate 21/10/2027
        }

        Private Const CERT_E_UNTRUSTEDROOT As Integer = &H800B0109
        Private Const CERT_E_CHAINING As Integer = &H800B010A
        Private Const TRUST_E_NOSIGNATURE As Integer = &H800B0100
        Private Const TRUST_E_BAD_DIGEST As Integer = &H80096010

        Private Shared ReadOnly WINTRUST_ACTION_GENERIC_VERIFY_V2 As New Guid("00AAC56B-CD44-11d0-8CC2-00C04FC295EE")

        Private Sub New()
        End Sub

        ''' <summary>Lanca <see cref="InvalidOperationException"/> se o arquivo nao passar nas duas checagens.</summary>
        Public Shared Sub EnsureTrusted(filePath As String)
            Dim result = Verify(filePath)
            If Not result.IsTrusted Then
                Throw New InvalidOperationException(result.Message)
            End If
        End Sub

        Public Shared Function Verify(filePath As String) As AuthenticodeResult
            If String.IsNullOrWhiteSpace(filePath) OrElse Not File.Exists(filePath) Then
                Return AuthenticodeResult.Fail("Arquivo para verificacao de assinatura nao encontrado.")
            End If

            Dim trustCode = WinVerifyTrustFile(filePath)
            Select Case trustCode
                Case 0, CERT_E_UNTRUSTEDROOT, CERT_E_CHAINING
                    ' Assinatura integra. A confianca vem da impressao digital abaixo.
                Case TRUST_E_NOSIGNATURE
                    Return AuthenticodeResult.Fail("O instalador nao tem assinatura digital.")
                Case TRUST_E_BAD_DIGEST
                    Return AuthenticodeResult.Fail("A assinatura digital do instalador nao confere: o arquivo foi alterado.")
                Case Else
                    Return AuthenticodeResult.Fail($"Assinatura digital do instalador invalida (codigo 0x{trustCode:X8}).")
            End Select

            Dim thumbprint As String
            Try
#Disable Warning SYSLIB0057
                Using signer = New X509Certificate2(X509Certificate.CreateFromSignedFile(filePath))
#Enable Warning SYSLIB0057
                    thumbprint = signer.Thumbprint
                End Using
            Catch ex As Exception
                Return AuthenticodeResult.Fail($"Nao foi possivel ler o certificado do instalador: {ex.Message}")
            End Try

            For Each trusted In TrustedSignerThumbprints
                If String.Equals(trusted, thumbprint, StringComparison.OrdinalIgnoreCase) Then
                    Return New AuthenticodeResult With {.IsTrusted = True, .SignerThumbprint = thumbprint, .Message = "Assinatura valida."}
                End If
            Next

            Return AuthenticodeResult.Fail($"O instalador foi assinado por um certificado nao autorizado ({thumbprint}).", thumbprint)
        End Function

        Private Shared Function WinVerifyTrustFile(filePath As String) As Integer
            Dim pathPtr = Marshal.StringToHGlobalUni(filePath)
            Dim fileInfoPtr = IntPtr.Zero
            Dim dataPtr = IntPtr.Zero
            Try
                Dim fileInfo As New WINTRUST_FILE_INFO With {
                    .cbStruct = CUInt(Marshal.SizeOf(GetType(WINTRUST_FILE_INFO))),
                    .pcwszFilePath = pathPtr
                }
                fileInfoPtr = Marshal.AllocHGlobal(Marshal.SizeOf(fileInfo))
                Marshal.StructureToPtr(fileInfo, fileInfoPtr, False)

                Dim data As New WINTRUST_DATA With {
                    .cbStruct = CUInt(Marshal.SizeOf(GetType(WINTRUST_DATA))),
                    .dwUIChoice = WTD_UI_NONE,
                    .fdwRevocationChecks = WTD_REVOKE_NONE,
                    .dwUnionChoice = WTD_CHOICE_FILE,
                    .pFile = fileInfoPtr,
                    .dwStateAction = WTD_STATEACTION_VERIFY,
                    .dwProvFlags = WTD_CACHE_ONLY_URL_RETRIEVAL
                }
                dataPtr = Marshal.AllocHGlobal(Marshal.SizeOf(data))
                Marshal.StructureToPtr(data, dataPtr, False)

                Dim result = WinVerifyTrust(IntPtr.Zero, WINTRUST_ACTION_GENERIC_VERIFY_V2, dataPtr)

                ' Libera o estado mantido pelo provedor.
                data = Marshal.PtrToStructure(Of WINTRUST_DATA)(dataPtr)
                data.dwStateAction = WTD_STATEACTION_CLOSE
                Marshal.StructureToPtr(data, dataPtr, True)
                WinVerifyTrust(IntPtr.Zero, WINTRUST_ACTION_GENERIC_VERIFY_V2, dataPtr)

                Return result
            Finally
                If dataPtr <> IntPtr.Zero Then Marshal.FreeHGlobal(dataPtr)
                If fileInfoPtr <> IntPtr.Zero Then Marshal.FreeHGlobal(fileInfoPtr)
                Marshal.FreeHGlobal(pathPtr)
            End Try
        End Function

        Private Const WTD_UI_NONE As UInteger = 2
        Private Const WTD_REVOKE_NONE As UInteger = 0
        Private Const WTD_CHOICE_FILE As UInteger = 1
        Private Const WTD_STATEACTION_VERIFY As UInteger = 1
        Private Const WTD_STATEACTION_CLOSE As UInteger = 2
        Private Const WTD_CACHE_ONLY_URL_RETRIEVAL As UInteger = &H1000

        <StructLayout(LayoutKind.Sequential)>
        Private Structure WINTRUST_FILE_INFO
            Public cbStruct As UInteger
            Public pcwszFilePath As IntPtr
            Public hFile As IntPtr
            Public pgKnownSubject As IntPtr
        End Structure

        <StructLayout(LayoutKind.Sequential)>
        Private Structure WINTRUST_DATA
            Public cbStruct As UInteger
            Public pPolicyCallbackData As IntPtr
            Public pSIPClientData As IntPtr
            Public dwUIChoice As UInteger
            Public fdwRevocationChecks As UInteger
            Public dwUnionChoice As UInteger
            Public pFile As IntPtr
            Public dwStateAction As UInteger
            Public hWVTStateData As IntPtr
            Public pwszURLReference As IntPtr
            Public dwProvFlags As UInteger
            Public dwUIContext As UInteger
            Public pSignatureSettings As IntPtr
        End Structure

        <DllImport("wintrust.dll", ExactSpelling:=True, SetLastError:=False, CharSet:=CharSet.Unicode)>
        Private Shared Function WinVerifyTrust(hwnd As IntPtr,
                                               <MarshalAs(UnmanagedType.LPStruct)> pgActionID As Guid,
                                               pWVTData As IntPtr) As Integer
        End Function
    End Class

    Public NotInheritable Class AuthenticodeResult
        Public Property IsTrusted As Boolean
        Public Property SignerThumbprint As String
        Public Property Message As String

        Friend Shared Function Fail(message As String, Optional thumbprint As String = Nothing) As AuthenticodeResult
            Return New AuthenticodeResult With {.IsTrusted = False, .Message = message, .SignerThumbprint = thumbprint}
        End Function
    End Class
End Namespace

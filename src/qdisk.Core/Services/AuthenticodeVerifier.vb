' ─────────────────────────────────────────────────────────────────────────────
' qdisk  |  Core / Services  |  AuthenticodeVerifier.vb
' Verifica a assinatura Authenticode de um executavel antes de executa-lo.
'
' © Quallit — Licenciado sob GPL-3.0-or-later.
' ─────────────────────────────────────────────────────────────────────────────

Imports System.IO
Imports System.Runtime.InteropServices
Imports System.Security.Cryptography
Imports System.Security.Cryptography.X509Certificates

Namespace Services
    ''' <summary>
    ''' Duas checagens, as duas obrigatorias:
    ''' 1. Integridade: o WinVerifyTrust confirma que o arquivo tem assinatura e que ela
    '''    nao foi alterada (raiz nao confiavel ainda passa aqui; a checagem 2 decide).
    ''' 2. Assinante autorizado, por um destes caminhos:
    '''    a. chave publica (SHA-256 do SubjectPublicKeyInfo) em <see cref="TrustedSignerPublicKeys"/>:
    '''       sobrevive a renovacao do certificado que reusa a chave do Cloud KMS;
    '''    b. impressao digital em <see cref="TrustedSignerThumbprints"/> (reserva);
    '''    c. renovacao com chave nova: cadeia validada ate uma raiz confiavel do Windows
    '''       (WinVerifyTrust = 0), titular O=<see cref="TrustedSignerOrganization"/>, C=BR, e emissor
    '''       O=<see cref="TrustedIssuerOrganization"/>.
    '''    Os caminhos a e b nao exigem a raiz no repositorio local (o WinVerifyTrust roda sem buscar
    '''    na rede); o c exige, porque nada nele esta fixado no app.
    ''' </summary>
    Public NotInheritable Class AuthenticodeVerifier
        ''' <summary>
        ''' Chaves publicas aceitas: SHA-256 (hex minusculo) do SubjectPublicKeyInfo do certificado
        ''' do assinante. Renovar o certificado com a mesma chave do KMS nao muda este valor.
        ''' </summary>
        Public Shared ReadOnly TrustedSignerPublicKeys As IReadOnlyList(Of String) = New String() {
            "b229b2351a3bfc41c139e0967839f58b2d52fb55f6d57f7e87d64494c2d3ab76" ' RSA 4096, chave quallitKey/1 do Cloud KMS (certificado OV DigiCert)
        }

        ''' <summary>
        ''' Certificados aceitos pela impressao digital (reserva). O PFX autoassinado (C991A777...) saiu na 1.00.
        ''' </summary>
        Public Shared ReadOnly TrustedSignerThumbprints As IReadOnlyList(Of String) = New String() {
            "5D4730113CFC90891D3AEC06630F53EE6DF14B79" ' CN=QUALLIT SERVICOS DE INFORMATICA LTDA (OV DigiCert, chave no Cloud KMS), valido ate 21/10/2027
        }

        ''' <summary>Titular (O) exigido no caminho c, com C=BR.</summary>
        Public Const TrustedSignerOrganization As String = "QUALLIT SERVICOS DE INFORMATICA LTDA"
        ''' <summary>Emissor (O) exigido no caminho c: a autoridade certificadora do certificado OV.</summary>
        Public Const TrustedIssuerOrganization As String = "DigiCert, Inc."

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

            Try
#Disable Warning SYSLIB0057
                Using signer = New X509Certificate2(X509Certificate.CreateFromSignedFile(filePath))
#Enable Warning SYSLIB0057
                    Return EvaluateSigner(trustCode, signer, TrustedSignerPublicKeys, TrustedSignerThumbprints)
                End Using
            Catch ex As Exception
                Return AuthenticodeResult.Fail($"Nao foi possivel ler o certificado do instalador: {ex.Message}")
            End Try
        End Function

        ''' <summary>
        ''' Checagem 2 (assinante), separada para os testes: <paramref name="trustCode"/> e o retorno do
        ''' WinVerifyTrust (0 = cadeia ate uma raiz confiavel).
        ''' </summary>
        Friend Shared Function EvaluateSigner(trustCode As Integer, signer As X509Certificate2,
                                              publicKeys As IEnumerable(Of String),
                                              thumbprints As IEnumerable(Of String)) As AuthenticodeResult
            Dim thumbprint = signer.Thumbprint
            Dim keyHash = PublicKeyHash(signer)

            If publicKeys.Any(Function(k) String.Equals(k, keyHash, StringComparison.OrdinalIgnoreCase)) Then
                Return AuthenticodeResult.Ok(thumbprint, "Assinatura valida (chave publica autorizada).")
            End If
            If thumbprints.Any(Function(t) String.Equals(t, thumbprint, StringComparison.OrdinalIgnoreCase)) Then
                Return AuthenticodeResult.Ok(thumbprint, "Assinatura valida (certificado autorizado).")
            End If
            If trustCode = 0 AndAlso
               String.Equals(NameAttribute(signer.SubjectName, OidOrganization), TrustedSignerOrganization, StringComparison.Ordinal) AndAlso
               String.Equals(NameAttribute(signer.SubjectName, OidCountry), "BR", StringComparison.Ordinal) AndAlso
               String.Equals(NameAttribute(signer.IssuerName, OidOrganization), TrustedIssuerOrganization, StringComparison.Ordinal) Then
                Return AuthenticodeResult.Ok(thumbprint, "Assinatura valida (certificado da Quallit emitido pela DigiCert, cadeia confiavel).")
            End If

            Return AuthenticodeResult.Fail($"O instalador foi assinado por um certificado nao autorizado ({thumbprint}).", thumbprint)
        End Function

        ''' <summary>SHA-256 (hex minusculo) do SubjectPublicKeyInfo do certificado.</summary>
        Public Shared Function PublicKeyHash(certificate As X509Certificate2) As String
            Return Convert.ToHexString(SHA256.HashData(certificate.PublicKey.ExportSubjectPublicKeyInfo())).ToLowerInvariant()
        End Function

        Private Const OidOrganization As String = "2.5.4.10"
        Private Const OidCountry As String = "2.5.4.6"

        ''' <summary>Valor do atributo (ex.: O, C) no nome; vazio se nao houver.</summary>
        Private Shared Function NameAttribute(name As X500DistinguishedName, oid As String) As String
            For Each rdn In name.EnumerateRelativeDistinguishedNames()
                If rdn.HasMultipleElements Then Continue For
                If rdn.GetSingleElementType().Value = oid Then Return If(rdn.GetSingleElementValue(), "")
            Next
            Return ""
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

        Friend Shared Function Ok(thumbprint As String, message As String) As AuthenticodeResult
            Return New AuthenticodeResult With {.IsTrusted = True, .Message = message, .SignerThumbprint = thumbprint}
        End Function
    End Class
End Namespace

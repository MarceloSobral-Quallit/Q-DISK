# Procedimento de assinatura de código (certificado OV da Quallit)

Este documento descreve como o Q-DISK assina os seus executáveis e instaladores com o certificado OV da Quallit e
como adotar o mesmo processo em outro projeto. Tudo o que está aqui foi implementado e verificado no Q-DISK em
23/10/2026 (releases 0.02.10.26 e 1.00.10.26). O que não foi testado está marcado como **não verificado**.

Kit usado: `signkit`, em `C:\QSI_DESENV\signtest` (repositório SignTest). Roteiro genérico do kit:
`C:\QSI_DESENV\signtest\docs\04-INTEGRACAO.md`.

## 1. Visão geral

```text
build do projeto ──> python -m signkit sign --backend signtool --config <cfg> <exe>
                         │
                         └─> signtool sign /fd sha256 /f <PEM da cadeia> /csp "Google Cloud KMS Provider"
                               /kc <versão da chave no KMS> /tr <carimbo RFC 3161> /td sha256 /d ... /du ...
                                 │
                                 └─> a assinatura (hash) é feita no HSM do Google Cloud KMS; a chave privada
                                     nunca sai do KMS
```

- **Certificado:** OV de assinatura de código, `CN=QUALLIT SERVICOS DE INFORMATICA LTDA`, emitido por
  `DigiCert Trusted G4 Code Signing RSA4096 SHA384 2021 CA1`, válido de 22/10/2026 a 21/10/2027. Impressão digital
  `5D4730113CFC90891D3AEC06630F53EE6DF14B79`.
- **Chave:** `projects/project-f639992e-e93f-4f50-a72/locations/global/keyRings/OVCodeSigning/cryptoKeys/quallitKey/cryptoKeyVersions/1`
  (RSA 4096, HSM). Hash da chave pública (SHA-256 do SubjectPublicKeyInfo):
  `b229b2351a3bfc41c139e0967839f58b2d52fb55f6d57f7e87d64494c2d3ab76`.
- **Credencial:** login do Google da estação (ADC). Não há senha nem arquivo de chave no repositório do projeto.
- **Carimbo de tempo:** `http://timestamp.digicert.com`, RFC 3161. Com ele, as assinaturas continuam válidas depois
  do vencimento do certificado (comportamento padrão do Authenticode; não testado no vencimento).

## 2. Regras que não mudam

- **Nunca alterar nem excluir** o keyring `OVCodeSigning`, a chave `quallitKey` ou o HSM no GCP. A emissora cobra
  para refazer a configuração e a homologação. O build só pede assinaturas.
- Nenhum segredo no git do projeto: nem JSON de conta de serviço, nem token. A config do signkit não tem segredo.
- O PEM da cadeia do certificado fica fora do git (não é segredo, mas é do ambiente de build).

## 3. Preparar a estação de build (uma vez)

1. Pré-requisitos, conferidos pelo próprio kit:

   ```powershell
   python -m signkit check --config <config do projeto>
   ```

   Itens verificados: Python 3.10 ou superior, Windows x64, VC++ Redistributable x64 14.34 ou superior,
   PowerShell 7 (`pwsh`), `signtool.exe` (Windows SDK), Cloud KMS CNG Provider, Google Cloud SDK (`gcloud`), a config,
   a chave KMS, o certificado e a credencial Google. O que faltar, `python -m signkit install` instala (com
   confirmação).

2. Instalar o signkit **no Python que roda o build**, em modo editável:

   ```powershell
   python -m pip install --user -e C:\QSI_DESENV\signtest
   ```

   - O `-e` é obrigatório: só assim o signkit acha a pasta do kit (`installers\`, `config\`).
   - Com o Python do sistema (`C:\Program Files\Python312`), use `--user`: funciona sem admin (verificado).

3. Credencial (login do Google, uma vez por estação):

   ```powershell
   gcloud auth application-default login
   ```

   O token fica em `%APPDATA%\gcloud\application_default_credentials.json` e é renovado sozinho. O navegador só volta
   a ser pedido se o login for revogado ou se a política do Google Workspace exigir.

## 4. Configurar o projeto

1. Copiar o PEM da cadeia para uma pasta do projeto **fora do git**. No Q-DISK:
   `tools/certs/quallit_servicos_de_informatica_ltda.pem` (o `.gitignore` tem `tools/certs/*`). O original está em
   `C:\QSI_DESENV\signtest\cert\`.

2. Criar a config do signkit no projeto (sem segredo, pode ir para o git). A do Q-DISK, `config/signkit.json`:

   ```json
   {
     "kmsKeyVersion": "projects/project-f639992e-e93f-4f50-a72/locations/global/keyRings/OVCodeSigning/cryptoKeys/quallitKey/cryptoKeyVersions/1",
     "certificateFile": "../tools/certs/quallit_servicos_de_informatica_ltda.pem",
     "timestampUrl": "http://timestamp.digicert.com",
     "timestampProtocol": "rfc3161",
     "description": "Q-DISK - Quallit Solucoes em TI",
     "descriptionUrl": "https://www.quallit.com.br",
     "credential": { "type": "adc", "name": "" }
   }
   ```

   - Caminhos relativos partem da pasta do JSON.
   - `description` e `descriptionUrl` viram o `/d` e o `/du` do signtool (nome e site exibidos nas propriedades do
     arquivo).

3. Conferir: `python -m signkit check --config config\signkit.json` sem nenhum FALTA.

## 5. Assinar no build

### 5.1 Executáveis

Contrato da linha de comando (do `signkit/cli.py`):

```text
python -m signkit sign   --config <cfg> [--backend auto|signtool|detached] [-v] <arquivo> [<arquivo> ...]
python -m signkit verify --config <cfg> [-v] <arquivo> [...]
python -m signkit check  --config <cfg>
```

- Saída: uma linha por arquivo, `[OK] sign <nome> (signtool)` ou `[FALHOU] ...`; na falha (ou com `-v`) vem a saída
  completa do signtool. Erros no stderr, prefixados com `ERRO`.
- Códigos de saída: 0 = tudo certo; 1 = algum arquivo falhou ou falta pré-requisito; 2 = uso incorreto.
- Chame pelo mesmo Python do build: `[sys.executable, "-m", "signkit", ...]`.
- **Use sempre `--backend signtool`.** Sem ele, o signkit escolhe pela extensão: `.exe .dll .sys .ocx .msi .msp .cab
  .cat .ps1 .psm1 .psd1 .appx .msix` vão para o Authenticode; qualquer outra extensão recebe só uma assinatura
  destacada (`.sig`), e o arquivo fica sem Authenticode.

No Q-DISK (`tools/build_release_OV.py`, classe `SignContext` e função `sign_exe`):

```python
cmd = [sys.executable, "-m", "signkit", "sign", "--backend", "signtool", "--config", str(signkit_config), str(exe)]
```

- Até 5 tentativas, com 2 s entre elas, quando a saída indica "arquivo em uso" (antivírus ou indexador segurando o
  exe recém-gerado).
- Assina, nesta ordem: o app (`qdisk.exe`), o QDisk_Adm, a bancada NetSpeedTest e o helper de manifesto.
- Antes de compilar, a etapa 1 roda `import signkit` e `python -m signkit check`; se falhar, o build para.

### 5.2 Instalador Inno Setup (Setup e desinstalador)

O Inno precisa assinar o desinstalador **durante** a compilação, então a assinatura é feita por ele, com o signkit
como "Sign Tool".

No `.iss` (Q-DISK: `tools/installer/qdisk.iss`):

```ini
#ifdef SignKit
SignTool=signkit
SignedUninstaller=yes
#endif
```

Na chamada do `iscc` (Q-DISK: função `build_installer`):

```python
command = f"$q{sys.executable}$q -m signkit sign --backend signtool --config $q{signkit_config}$q $f"
cmd = [iscc, f"/DAppVersion={version}", "/DSignKit=1", f"/Ssignkit={command}", str(iss_script)]
```

- `$q` vira aspas e `$f` vira o nome do arquivo entre aspas. Assim o argumento não leva aspas próprias, e o
  `subprocess` (`list2cmdline`) não precisa escapar nada (verificado no Inno Setup 6).
- `--backend signtool` é **obrigatório** aqui: o Inno entrega o desinstalador como `uninst.e32.tmp`, e pela extensão
  o signkit faria só um `.sig`. O `iscc` então falha com "The Sign Tool command returned an exit code of 0, but the
  file does not have a digital signature".
- O Inno assina o desinstalador e, no fim, o próprio `Setup_*.exe`. Não é preciso assinar o Setup de novo depois.
- Sem `/DSignKit` (compilação manual com `iscc`), o instalador sai sem assinatura.

### 5.3 Ordem no pipeline

Assinar **antes** de calcular qualquer hash publicado (manifesto de atualização, catálogos). No Q-DISK: etapa 7
(exes) → 8 (verificação) → 10 (Setup) → 13 (pacote de atualização com SHA-256 do Setup já assinado).

## 6. Verificar

No build, depois de assinar, para cada arquivo:

```powershell
Get-AuthenticodeSignature <arquivo>        # Status = Valid; SignerCertificate = QUALLIT ...
signtool verify /pa /v <arquivo>           # cadeia e carimbo de tempo
```

Esperado (verificado em todos os exes, no Setup e no desinstalador instalado):

- Status `Valid`;
- cadeia QUALLIT SERVICOS DE INFORMATICA LTDA → DigiCert Trusted G4 Code Signing RSA4096 SHA384 2021 CA1 →
  DigiCert Trusted Root G4;
- carimbo de tempo RFC 3161 da DigiCert.

Pelo Git Bash, o `/pa` é convertido em caminho (`C:/Program Files/Git/pa`). Rode o `signtool verify` pelo
PowerShell.

## 7. O app que se atualiza sozinho: aceitar só instaladores da Quallit

Se o projeto baixa e executa o próprio instalador de atualização, ele precisa conferir quem assinou. No Q-DISK
(`src/qdisk.Core/Services/AuthenticodeVerifier.vb`), antes de executar o instalador:

1. **SHA-256** do arquivo baixado igual ao do manifesto (HTTPS).
2. **Integridade** pelo `WinVerifyTrust` (assinatura presente e arquivo não alterado), sem buscar na rede
   (`WTD_CACHE_ONLY_URL_RETRIEVAL`, sem checagem de revogação).
3. **Assinante autorizado** por um destes caminhos:
   - a. **chave pública fixada**: SHA-256 do SubjectPublicKeyInfo do certificado do assinante numa lista do app.
     Sobrevive à renovação do certificado que reuse a mesma chave do KMS;
   - b. **impressão digital fixada** (reserva);
   - c. **renovação com chave nova**: `WinVerifyTrust` = 0 (cadeia até uma raiz confiável do Windows), titular
     `O=QUALLIT SERVICOS DE INFORMATICA LTDA`, `C=BR`, e emissor `O=DigiCert, Inc.`.

   Os caminhos a e b aceitam raiz não confiável, porque a máquina pode não ter a raiz da DigiCert e o
   `WinVerifyTrust` não busca na rede. O c exige a cadeia confiável, porque nada nele está fixado no app.

Hash da chave pública de um certificado (PowerShell 7):

```powershell
$c = [System.Security.Cryptography.X509Certificates.X509Certificate2]::CreateFromPem((Get-Content <cert.pem> -Raw))
[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($c.PublicKey.ExportSubjectPublicKeyInfo())).ToLower()
```

Testes do Q-DISK para essa regra: `tests/qdisk.Tests/AuthenticodeSignerPolicyTests.cs`. Os testes geram
certificados de teste com `CertificateRequest`, sem depender de arquivos assinados.

**Ao trocar de certificado** (ex.: de um PFX autoassinado para o OV), a versão do app que conhece o novo assinante
precisa chegar aos clientes **antes** do primeiro instalador assinado com ele. No Q-DISK: a 0.01 passou a aceitar o
OV e ainda saiu assinada com o PFX; a 0.02 foi a primeira assinada com o OV.

## 8. O que o usuário vê

- **Propriedades → Assinaturas Digitais:** signatário QUALLIT SERVICOS DE INFORMATICA LTDA; "Esta assinatura digital
  é válida".
- **SmartScreen:** ao abrir um instalador baixado da internet, o Windows mostrou "O Windows protegeu o computador",
  porque o certificado ainda não tem reputação. Em "Mais informações" aparece **Fornecedor: QUALLIT SERVICOS DE
  INFORMATICA LTDA** e o botão "Executar assim mesmo". A reputação cresce com o volume de downloads; enviar o arquivo
  para análise da Microsoft pode acelerar (**não verificado**).
- **UAC:** só aparece em instaladores que pedem elevação. O do Q-DISK é por usuário (`PrivilegesRequired=lowest`) e
  não mostra UAC.
- Manual para o usuário conferir o certificado: `docs/manual/Manual_Instalacao_Q-DISK.pdf` (fonte
  `assets/manual/instalacao.html`; telas refeitas por `tools/utils/capturar_instalador.ps1`).

## 9. Renovação do certificado (vence em 21/10/2027)

- **Mesma chave do KMS** (pedido de certificado gerado com a `quallitKey/1`; **a confirmar com a DigiCert**):
  trocar o PEM na pasta do projeto (e o `certificateFile` da config, se o nome mudar). O app aceita pela chave
  pública, sem nova versão.
- **Chave nova:** o app aceita pelo caminho c, desde que a máquina valide a cadeia. Para não depender disso, incluir o
  hash da nova chave (ou a nova impressão digital) numa versão publicada **antes**, ainda assinada com o certificado
  atual.
- Em todos os casos: atualizar o PEM, rodar `python -m signkit check`, reassinar o que já está gerado (no Q-DISK,
  `python tools\build_release_OV.py --sign-only`) e atualizar emissor, validade e impressão digital no manual de
  instalação e no README.

## 10. Checklist para adotar em outro projeto

1. Estação pronta: `python -m signkit check` sem FALTA (seção 3).
2. `python -m pip install --user -e C:\QSI_DESENV\signtest` no Python do build.
3. PEM da cadeia numa pasta fora do git; config JSON do signkit no projeto (seção 4).
4. No build: chamar `python -m signkit sign --backend signtool --config <cfg> <arquivo>` para cada exe/dll e tratar o
   código de saída (seção 5.1).
5. Instalador:
   - Inno Setup: `SignTool` + `SignedUninstaller` e `/S` no `iscc` (seção 5.2);
   - outros empacotadores: assinar o pacote final com o mesmo comando. `.msi` está na lista de extensões do
     Authenticode do signkit, mas não foi testado.
6. Assinar antes de calcular hashes publicados (seção 5.3).
7. Verificar cada artefato (seção 6) e registrar no projeto.
8. Se o app se atualiza sozinho: fixar a chave pública (e a impressão digital) e aceitar a renovação pela regra da
   seção 7, com testes.
9. Orientar o usuário sobre a verificação do certificado e o SmartScreen (seção 8).
10. Registrar o resultado em `C:\QSI_DESENV\signtest\docs\REGISTRO.md`.

Pendências conhecidas do kit (de `C:\QSI_DESENV\signtest\docs\REGISTRO.md`):
- CI com conta de serviço: falta confirmar se o CNG Provider lê `GOOGLE_APPLICATION_CREDENTIALS`.
- Ainda não testados: assinatura de `.msi` e `.ps1`, e o carimbo `legacy` (`/t`).

## Referências no Q-DISK

| Arquivo | Papel |
|---|---|
| `tools/build_release_OV.py` | Pipeline: `SignContext`, `validate_sign_prerequisites`, `sign_exe`, `verify_sign`, `build_installer` |
| `config/signkit.json` | Config do signkit (chave KMS, PEM, carimbo, `/d`, `/du`) |
| `tools/installer/qdisk.iss` | `SignTool=signkit` e `SignedUninstaller=yes` sob `#ifdef SignKit` |
| `src/qdisk.Core/Services/AuthenticodeVerifier.vb` | Regra do assinante na atualização automática |
| `tests/qdisk.Tests/AuthenticodeSignerPolicyTests.cs` | Testes da regra |
| `docs/DOCUMENTACAO_TECNICA.md` (§10 e §15) | Etapas do pipeline e receitas de renovação |

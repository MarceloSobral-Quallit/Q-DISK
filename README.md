<p align="center">
  <img src="assets/logo-quallit-512x512.png" width="120" alt="Quallit logo" />
</p>

<h1 align="center">qdisk</h1>

<p align="center">
  Ferramenta Windows para técnicos Quallit: baixa imagens (ISO e backups Acronis TIBX) com verificação de integridade,
  grava pendrives de boot e copia backups para restauração — executável único, instalado e atualizado online.
</p>

<p align="center">
  <img src="https://img.shields.io/badge/plataforma-Windows%2010%2F11%20x64-lightgrey" alt="Plataforma" />
  <img src="https://img.shields.io/badge/.NET-8.0-purple" alt=".NET" />
  <img src="https://img.shields.io/badge/linguagem-VB.NET-blue" alt="Linguagem" />
  <img src="https://img.shields.io/badge/licença-GPLv3--or--later-green" alt="Licença" />
</p>

> A versão atual está em [`version_info.txt`](version_info.txt) e o histórico em [`docs/RoadMap.md`](docs/RoadMap.md).

## O que faz

- **Token do dia por perfil** (RD, PNB, DevTeam): cada perfil vê o seu catálogo de imagens mais o catálogo de
  ferramentas.
- **Downloads** do SharePoint, Dropbox ou mateweb, com várias conexões, troca de conexão lenta e SHA-256 conferido.
- **Gravar ISO Bootável**: cópia FAT32 (BIOS e/ou UEFI), RAW/DD e UEFI:NTFS (Windows 11, só UEFI); o método de cada
  imagem vem do catálogo; verificação depois de gravar; identificação de pendrive já gravado ("Imagem do USB").
- **Copiar Backup TIBX** para pendrive NTFS (a mídia de boot Acronis restaura a partir dele), com verificação.
- **Teste de velocidade** das origens, **Logs**, **Configurações**, **Manual** embutido e **atualização online**
  assinada.

O **QDisk_Adm** (`QDisk_Adm/`) é a ferramenta de administração: mantém os catálogos, gera os manifestos USB, decide o
método de gravação de cada ISO, verifica as imagens no Microsoft Defender e publica.

## Documentação

| Documento | Conteúdo |
|---|---|
| [docs/FLUXOS_APP.md](docs/FLUXOS_APP.md) | Fluxos do aplicativo, tela a tela (diagramas) |
| [docs/FLUXOS_QDisk_Adm.md](docs/FLUXOS_QDisk_Adm.md) | Fluxos do administrador de catálogos (diagramas) |
| [docs/DOCUMENTACAO_TECNICA.md](docs/DOCUMENTACAO_TECNICA.md) | Guia do desenvolvedor: estrutura, arquitetura, configuração, build, release, testes, tarefas comuns, catálogos e servidores, atualização online, método de gravação por imagem e regra do token para o site |
| [docs/pendencias.md](docs/pendencias.md) | O que está em aberto |
| [docs/RoadMap.md](docs/RoadMap.md) | Histórico de versões |
| [docs/RESULTADOS_TESTES.md](docs/RESULTADOS_TESTES.md) | Resultados de todos os testes reais (bancadas e testes pelo app) |
| [QDisk_Adm/README.md](QDisk_Adm/README.md) | Uso do QDisk_Adm, botão a botão |
| [docs/arquivo/](docs/arquivo/README.md) | Documentos antigos, só para consulta |

## Início rápido (desenvolvimento)

```powershell
dotnet build src\qdisk.UI -c Release          # app
dotnet test tests\qdisk.Tests                 # testes unitários
.\QDisk_Adm\run_qdisk_adm.cmd                                   # administrador de catálogos
python tools\build_release_dotnet_4.py                      # release completa (bump, build, assinatura, publicação)
```

Requisitos, segredos (`config/secret.md`) e a lista das 17 etapas do pipeline estão em
[docs/DOCUMENTACAO_TECNICA.md](docs/DOCUMENTACAO_TECNICA.md).

## Licença

qdisk é distribuído sob a GNU General Public License v3.0 ou posterior (`GPL-3.0-or-later`). Consulte
[LICENSE](LICENSE) e [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).

O projeto incorpora artefatos compatíveis com GPL do Rufus para criação de mídia USB bootável (detalhes em
`THIRD_PARTY_NOTICES.md`). Ao distribuir binários, publique também o código-fonte correspondente nos termos da GPLv3.
Não é permitido redistribuir, modificar ou utilizar este software fora do contexto autorizado pela Quallit.

## Dev Team / Contato

**Quallit Dev Team** — equipe de desenvolvimento e manutenção do qdisk.

| Assunto | Contato |
|---|---|
| Dúvidas técnicas e suporte ao desenvolvimento | desenv@quallit.com.br |
| Reportar vulnerabilidades de segurança | desenv@quallit.com.br |
| Solicitação de autorização de uso | desenv@quallit.com.br |

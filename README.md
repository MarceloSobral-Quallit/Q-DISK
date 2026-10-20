<p align="center">
  <img src="assets/logo-quallit-512x512.png" width="120" alt="Quallit logo" />
</p>

<h1 align="center">qdisk</h1>

<p align="center">
  Ferramenta Windows para técnicos Quallit: baixa imagens (ISO e backups Acronis TIBX) com verificação de integridade,
  grava pendrives de boot e copia backups para restauração — executável único, instalado e atualizado online.
</p>

<p align="center">
  <img src="https://img.shields.io/badge/plataforma-Windows%20x64-lightgrey" alt="Plataforma" />
  <img src="https://img.shields.io/badge/.NET-8.0-purple" alt=".NET" />
  <img src="https://img.shields.io/badge/linguagem-VB.NET-blue" alt="Linguagem" />
  <img src="https://img.shields.io/badge/licença-GPLv3--or--later-green" alt="Licença" />
</p>

> Versão atual: [`version_info.txt`](version_info.txt) (definida em [`Directory.Build.props`](Directory.Build.props)).
> Histórico: [`docs/RoadMap.md`](docs/RoadMap.md).

No programa, o nome exibido é **Q-DISK**; arquivos, pastas, projetos e o executável usam `qdisk` (`qdisk.exe`).

## O que faz

- **Token do dia por perfil** (RD, PNB, DevTeam): cada perfil vê o seu catálogo de imagens mais o catálogo de
  ferramentas.
- **Downloads** a partir dos links dos catálogos (SharePoint, Dropbox ou mateweb), com várias conexões, troca de
  conexão lenta e SHA-256 conferido.
- **Gravar ISO Bootável**: cópia FAT32 (BIOS e/ou UEFI), RAW/DD e UEFI:NTFS; o método de cada imagem vem do catálogo;
  verificação depois de gravar; identificação de pendrive já gravado ("Imagem do USB").
- **Copiar Backup TIBX** para pendrive NTFS, com verificação.
- **Teste de velocidade** das origens, **Logs**, **Configurações**, **Manual** embutido e **atualização online** com
  verificação de SHA-256 e assinatura Authenticode do instalador.

O **QDisk_Adm** ([`QDisk_Adm/`](QDisk_Adm/README.md)) é a ferramenta de administração: mantém os catálogos, gera os
manifestos USB, decide o método de gravação de cada ISO, verifica as imagens no Microsoft Defender e publica.

## Estrutura

| Pasta / arquivo | Conteúdo |
|---|---|
| [`qdisk.sln`](qdisk.sln) | Solução .NET |
| [`src/qdisk.Core`](src/qdisk.Core) | Modelos e serviços (catálogos, downloads, token, USB, logs) |
| [`src/qdisk.Infrastructure`](src/qdisk.Infrastructure) | Cliente de atualização online |
| [`src/qdisk.UI`](src/qdisk.UI) | Aplicativo WinForms (`qdisk.exe`) |
| [`tests/qdisk.Tests`](tests/qdisk.Tests) | Testes unitários (xUnit) |
| [`config/`](config) | Catálogos `qdisk.catalog.*.json`, endpoints, tokens e manifestos |
| [`QDisk_Adm/`](QDisk_Adm) | Administrador de catálogos (Python) |
| [`tools/`](tools) | Pipeline de release, instalador, utilitários, NetSpeedTest e IsoManifestTool |
| [`assets/`](assets) | Ícones, imagens, manual e artefatos UEFI:NTFS embutidos no executável |

## Documentação

| Documento | Conteúdo |
|---|---|
| [docs/DOCUMENTACAO_TECNICA.md](docs/DOCUMENTACAO_TECNICA.md) | Guia do desenvolvedor: estrutura, arquitetura, configuração, build, release, testes e servidores |
| [docs/FLUXOS_APP.md](docs/FLUXOS_APP.md) | Fluxos do aplicativo, tela a tela (diagramas) |
| [docs/FLUXOS_QDisk_Adm.md](docs/FLUXOS_QDisk_Adm.md) | Fluxos do administrador de catálogos (diagramas) |
| [QDisk_Adm/README.md](QDisk_Adm/README.md) | Uso do QDisk_Adm, botão a botão e pela linha de comando |
| [tools/Quallit.NetSpeedTest/README.md](tools/Quallit.NetSpeedTest/README.md) | Bancada do teste de velocidade |
| [tools/git/README.md](tools/git/README.md) | Sincronização com o GitHub |
| [docs/RESTRICOES_WINFORMS.md](docs/RESTRICOES_WINFORMS.md) | Restrições de design da interface |
| [docs/RoadMap.md](docs/RoadMap.md) | Histórico de versões |

## Início rápido (desenvolvimento)

```powershell
dotnet build src\qdisk.UI -c Release      # app
dotnet test tests\qdisk.Tests             # testes unitários
.\QDisk_Adm\run_qdisk_adm.cmd             # administrador de catálogos
python tools\build_release_dotnet_4.py    # release completa (versão, build, assinatura, instalador, publicação)
```

Requisitos, segredos (`config/secret.md`, modelo em [`config/secret.md.example`](config/secret.md.example)) e as
17 etapas do pipeline estão em [docs/DOCUMENTACAO_TECNICA.md](docs/DOCUMENTACAO_TECNICA.md).

## Repositório

https://github.com/MarceloSobral-Quallit/Q-DISK

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

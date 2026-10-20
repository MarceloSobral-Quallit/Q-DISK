# Quallit NetSpeedTest

Bancada para evoluir o **Teste de velocidade** do qdisk. É uma cópia da tela do app principal (`src/qdisk.UI/Views/SpeedTestView.vb`), com o mesmo tema e o mesmo tamanho de janela (800 × 650). A diferença está no modelo de medição: em vez de baixar amostras de poucos MiB, mede a vazão sustentada por tempo fixo, com 1, 4 e 8 conexões, pelo mesmo caminho do download real. Se os resultados forem aprovados, o modelo vai para o app principal.

## Como executar

É um aplicativo autônomo, como o GUI_ADM: um exe só, que já traz o .NET, os ícones e o logo, e roda em qualquer Windows x64 sem instalar nada. Não entra no instalador nem na atualização online.

- **Pelo pipeline:** o passo 6 do `tools/build_release_dotnet_4.py` gera `dist\NetSpeedTest\Quallit.NetSpeedTest.exe`, com a versão do projeto (`Directory.Build.props`, mostrada no título da janela). Os passos 7 e 8 assinam e verificam o exe junto com o app principal e o GUI_ADM, e o passo 9 o inclui no ZIP de distribuição.
- **Só a bancada:**

  ```powershell
  dotnet publish tools\Quallit.NetSpeedTest\Quallit.NetSpeedTest.vbproj -c Release -o dist\NetSpeedTest
  ```

- **Durante o desenvolvimento:** `dotnet build tools\Quallit.NetSpeedTest\Quallit.NetSpeedTest.vbproj -c Release` e abrir `tools\Quallit.NetSpeedTest\bin\Release\net8.0-windows\win-x64\Quallit.NetSpeedTest.exe`.

Não pede token. Ao abrir, a bancada lê os 4 catálogos publicados (RD, PNB, DevTeam e Ferramentas) no servidor principal. Se o principal falhar, lê do mateweb (`/qdisk/`).

## O que a tela mostra

- **Origens:** Dropbox e SharePoint, um cartão para cada. A caixa no título liga ou desliga a origem. Os itens são classificados pelo sufixo do nome (`-dropbox`, `-sharepoint`) ou pelo link (`-db`, `-sp`). O item padrão é o maior da origem, e pode ser trocado na lista.
- **Duração:** 10, 20 (padrão), 30 ou 60 s por medição.
- **Conexões:** 1, 4 e 8. Cada quantidade é uma medição separada.
- **Link final:**
  - desmarcado (padrão): cada conexão pede o link tirado da página do quallit.link (por exemplo `www.dropbox.com/...`) e segue os redirecionamentos sozinha;
  - marcado: todas as conexões pedem o link final já resolvido (`uc…dl.dropboxusercontent.com`), como o app principal faz hoje (`SegmentedDownloadService.ResolveFinalUrlAsync`).
- **Progresso durante a medição:**
  - velocidade do último segundo e média, atualizadas a cada 250 ms;
  - conexões ativas e MiB lidos;
  - gráfico por segundo: os 2 primeiros segundos em cinza, a linha tracejada é a média sustentada;
  - a linha da tabela em medição fica destacada;
  - barras "Medição atual" e "Teste completo" e uma linha de status.
- **Resultado:**
  - por origem, a tabela tem média, mínimo–máximo, conexões ativas e tempo até o 1º byte para 1, 4 e 8 conexões;
  - a melhor quantidade de conexões fica em verde, e o rodapé do cartão dá a recomendação (por exemplo "8 conexões: +114% sobre 1 conexão" ou "Várias conexões não ajudam nesta origem");
  - a origem mais rápida recebe o selo **Melhor**.
- **Exportar CSV:** uma linha por medição, inclusive os MiB/s de cada segundo e o modo de link. Separador `;`, UTF-8.

## Modelo de medição (`src/qdisk.Core/Services/NetSpeedTestService.vb`)

1. **Resolução do link:** igual ao download real. O atalho quallit.link devolve uma página com `redirect("...")`, o endereço é extraído com as mesmas expressões do teste de velocidade anterior (parecidas com as do `DownloadService`), e depois o `HttpClient` segue os redirecionamentos.
2. **Medição com N > 1:** antes, um `Range: bytes=0-0` precisa responder 206 com `Content-Range`. Senão o resultado é "Range não suportado". Cada conexão pede `bytes=início-`, com o início em `i × tamanho / N`.
3. **Leitura:** as conexões leem durante o tempo escolhido e nada é gravado em disco. Os bytes são somados em baldes de 1 segundo.
4. **Média sustentada:** média dos baldes sem os 2 primeiros segundos (abertura das conexões e aceleração do TCP). Se o arquivo terminar antes do tempo, contam só os segundos completos.
5. **Conexões ativas:** conexões que receberam pelo menos 1 MiB. Uma conexão que cai é registrada nas observações e não anula a medição.
6. **Isolamento:** cada medição usa um `HttpClient` novo (uma conexão já aquecida não favorece a seguinte). Os cookies são compartilhados, porque o SharePoint pode exigir o cookie do redirecionamento.

O serviço fica no Core e os cartões e o gráfico em `src/qdisk.UI/Theme/UiSpeedTest.vb`; a bancada e a aba Teste de velocidade do app usam os mesmos arquivos.

## Resultados de 16/10/2026 (rede da Quallit, 20 s por medição)

Itens: `rd-win-atual-dropbox` e `rd-win-atual-sharepoint` (19,4 GiB). Médias sustentadas em MiB/s, tiradas do log da bancada.

| Origem | Link | 1 conexão | 4 conexões | 8 conexões |
|---|---|---|---|---|
| Dropbox | origem (padrão) | 23,7 | 43,7 (4/4 ativas) | **50,8** (8/8 ativas) |
| Dropbox | final (como o app) | 403 Forbidden ¹ | 46,8 (1/4 ativas, 3 com 403) ¹ | Range não suportado ¹ ² |
| SharePoint | origem (padrão) | 12,2 | 16,4 | **21,8** (8/8 ativas) |
| SharePoint | final (como o app) | 0,39 ² | 17,3 | 18,5 (8/8 ativas) |

¹ Primeira rodada, com a versão inicial da bancada (sem a correção da tela ao vivo). Os valores são do log.
² A segunda rodada com link final repetiu "Range não suportado" no Dropbox com 8 conexões. Nela, o Dropbox com 4 conexões deu 48,3.

O que os números indicam (para validar com mais rodadas antes de mudar o app):

- **Dropbox:** pedir o link final já resolvido explica as falhas do app (403 e "RangeNaoSuportado", que leva ao fallback de 1 conexão). Com o link de origem, as 8 conexões funcionaram.
- **SharePoint:** com várias conexões ficou bem mais rápido do que com 1 nas duas rodadas. Hoje o app só usa várias conexões no Dropbox (`SegmentedDownloadService.IsEligibleOrigin`).
- **Variação:** o SharePoint com 1 conexão variou muito entre as rodadas (0,39 a 12,2 MiB/s). Uma medição curta, como a do teste atual do app (amostras de 1 a 25 MiB), não representa a origem.

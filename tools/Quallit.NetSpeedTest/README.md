# Quallit NetSpeedTest

Bancada do **Teste de velocidade** do qdisk. Mede a vazão das origens de download (Dropbox e SharePoint) por tempo fixo, com 1, 4 e 8 conexões, pelo mesmo caminho do download real, sem gravar nada em disco.

A bancada compila os mesmos arquivos da aba Teste de velocidade do app:

- medição: `src/qdisk.Core/Services/NetSpeedTestService.vb` (referência ao projeto `qdisk.Core`);
- cartões das origens e gráfico: `src/qdisk.UI/Theme/UiSpeedTest.vb`, junto com o resto de `src/qdisk.UI/Theme/*.vb`, incluídos como arquivos ligados no `.vbproj`.

Em relação à aba do app, a bancada acrescenta as opções **Link final** e **Trocar lenta** (veja abaixo) e lê os catálogos por conta própria, sem token.

## Como executar

Executável único autônomo para Windows x64 (`win-x64`, `SelfContained`, `PublishSingleFile`): traz o .NET, os ícones e o logo dentro do exe e não precisa de nada instalado. Não faz parte do instalador do qdisk (`tools/installer/qdisk.iss`).

- **Pelo pipeline:** o passo 6 do `tools/build_release_OV.py` ("Compilar QDisk_Adm e NetSpeedTest") gera `dist\NetSpeedTest\Quallit.NetSpeedTest.exe` com a versão do projeto (`Directory.Build.props`), mostrada no título da janela. Os passos 7 e 8 assinam e verificam o exe junto com os demais executáveis, e o passo 9 o inclui no ZIP de distribuição (o ZIP leva todo o conteúdo de `dist\`).
- **Só a bancada:**

  ```powershell
  dotnet publish tools\Quallit.NetSpeedTest\Quallit.NetSpeedTest.vbproj -c Release -o dist\NetSpeedTest
  ```

- **Durante o desenvolvimento:** `dotnet build tools\Quallit.NetSpeedTest\Quallit.NetSpeedTest.vbproj -c Release` e abrir `tools\Quallit.NetSpeedTest\bin\Release\net8.0-windows\win-x64\Quallit.NetSpeedTest.exe`.

Ao abrir, a bancada lê 4 catálogos publicados (`qdisk.catalog.rd.json`, `qdisk.catalog.pnb.json`, `qdisk.catalog.devteam.json` e `qdisk.catalog.tools.json`) no servidor principal (`primary.baseUrl` de `config/qdisk.endpoints.json`). Para cada catálogo que falhar no principal, tenta o fallback (`fallback.publicBaseUrl`). Entram os itens habilitados, com link, sem repetir o mesmo link.

## O que a tela mostra

Janela de 800 × 650 (mesmo tamanho e mínimo da janela do app), com o tema do app.

- **Origens:** um cartão para Dropbox e outro para SharePoint. A caixa no título liga ou desliga a origem.
  - Classificação do item: Dropbox se o nome termina em `dropbox` ou `-db`, ou se o link termina em `-db` ou contém `dropbox`; SharePoint pela mesma regra com `sharepoint` e `-sp`. Os demais itens não aparecem.
  - A lista vem do maior para o menor arquivo, e o maior fica selecionado; pode ser trocado na lista.
- **Duração:** 10, 20 (padrão), 30 ou 60 s por medição.
- **Conexões:** 1, 4 e 8 (todas marcadas por padrão). Cada quantidade é uma medição separada.
- **Link final:**
  - desmarcado (padrão): cada conexão pede o link tirado da página do atalho (por exemplo `www.dropbox.com/...`) e segue os redirecionamentos HTTP;
  - marcado: todas as conexões pedem o link final, já depois dos redirecionamentos (por exemplo `uc…dl.dropboxusercontent.com`).
- **Trocar lenta** (desmarcado por padrão): liga a troca de conexão lenta descrita no modelo de medição.
- **Durante a medição:**
  - velocidade do último segundo e média até o momento, atualizadas a cada 250 ms;
  - conexões ativas, MiB lidos e, quando houver, o número de trocas de conexão lenta;
  - gráfico por segundo: os 2 primeiros segundos em cinza e a linha tracejada com a média sustentada;
  - a linha da tabela em medição fica destacada;
  - barras "Medição atual" e "Teste completo", uma linha de status e o log.
- **Resultado por origem:**
  - tabela com MiB/s (média sustentada), mín–máx, conexões ativas e tempo até o 1º byte para cada quantidade de conexões;
  - a melhor quantidade fica em verde, e o rodapé do cartão traz a recomendação, por exemplo "8 conexões: +114% sobre 1 conexão", "Ganho pequeno com 4 conexões (+6%)" ou "Várias conexões não ajudam nesta origem.";
  - a origem mais rápida recebe o selo **Melhor**.
- **Exportar CSV** (botão com ícone, à direita): uma linha por medição, com data, origem, item, arquivo, host, conexões, status, médias, 1º byte, conexões ativas, bytes, segundos, MiB/s de cada segundo, modo de link, troca ligada e número de trocas, e observações. Separador `;`, UTF-8 com BOM.

## Modelo de medição (`NetSpeedTestService.vb`)

1. **Resolução do link:** o link do catálogo é pedido; enquanto a resposta for HTML (até 4 respostas), o endereço do arquivo é extraído da página (`redirect("...")`, `window.location.replace("...")` ou `href="https://..."`, só HTTPS) e pedido em seguida. O `HttpClient` segue os redirecionamentos HTTP. O resultado tem o link de origem (tirado da página) e o link final (depois dos redirecionamentos).
2. **Novas tentativas:** respostas 429 ou 503 são repetidas até 3 tentativas, esperando o `Retry-After` (no máximo 10 s) ou 2 s e 4 s.
3. **Medição com N > 1:** antes, um pedido `Range: bytes=0-0` precisa responder 206 com `Content-Range` e tamanho total; senão o resultado é "Range não suportado". Cada conexão pede `bytes=início-`, com início em `i × tamanho / N`, e precisa receber 206. Com 1 conexão (e sem troca), o arquivo é pedido sem `Range`.
4. **Leitura:** as conexões leem até acabar o tempo escolhido; nada é gravado em disco. Os bytes são somados em baldes de 1 segundo.
5. **Média sustentada:** média dos baldes sem os 2 primeiros segundos (`WarmupSeconds`). Se a leitura parar antes do tempo, contam só os segundos completos; mínimo e máximo saem dos mesmos baldes.
6. **Conexões ativas:** as que receberam pelo menos 1 MiB. Uma conexão que cai é registrada nas observações e não anula a medição; a medição só falha se nenhum byte chegar.
7. **Isolamento:** cada medição usa um `HttpClient` novo, para que uma conexão já aberta não favoreça a medição seguinte. Os cookies são compartilhados entre as medições, porque o SharePoint pode exigir o cookie recebido no redirecionamento.
8. **Troca de conexão lenta** (`SlowConnectionPolicy`): com a troca ligada, toda conexão pede `Range` a partir da posição atual. Se a vazão de uma conexão ficar abaixo de 1 MiB/s numa janela de 3 s, ela é fechada e reaberta do mesmo ponto. A janela dobra a cada troca seguida (até 60 s) e volta a 3 s depois de uma janela rápida, e há no máximo uma troca por segundo somando as conexões da medição.

Na aba Teste de velocidade do app, as conexões sempre pedem o link de origem, e a troca de conexão lenta é ligada automaticamente quando o host final é do `sharepoint.com`.

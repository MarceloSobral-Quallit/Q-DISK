# qdisk: restrições de design (WinForms)

Este documento descreve a plataforma em que as telas são implementadas e o que é viável nela.
Toda proposta de design deve respeitar estas regras. Os valores abaixo foram tirados do código
do aplicativo em `src/qdisk.UI` (tema em `Theme/*.vb`, telas em `Views/*.vb`).

## 1. Plataforma

- Aplicativo desktop Windows em **WinForms**, **.NET 8** (`net8.0-windows`), escrito em **VB.NET**.
- Apenas controles do WinForms, com desenho próprio onde indicado na seção 7. Não há biblioteca
  visual de terceiros (os pacotes do projeto de interface são DiscUtils e System.Management, sem
  relação com a interface).
- DPI: `Application.SetHighDpiMode(HighDpiMode.SystemAware)` e `EnableVisualStyles()` em
  `Program.vb`; `AutoScaleMode.Font` na janela principal e nas telas que têm `.Designer.vb`.
- Janela principal: tamanho inicial e **mínimo de 800 × 650 px**. As propostas devem funcionar
  nesse tamanho e crescer com a janela (Dock/Anchor), sem rolagem horizontal.
- O app não tem tema escuro.
- Textos da interface em português do Brasil. Nomes de botões e telas aparecem no manual do
  usuário (`assets/manual/manual.html`); se forem alterados, o manual precisa ser atualizado junto.

## 2. Estrutura da janela principal

| Área | Implementação |
| --- | --- |
| Cabeçalho | `Panel` no topo, altura 56 px, fundo Navy. À esquerda, o logo (28 px de largura, margem de 16 px); depois o nome da tela em Segoe UI 14 negrito, branco. À direita, o botão **Alterar Perfil** no estilo Header (32 px de altura, ícone `swap`, largura pelo texto, margem direita de 16 px) |
| Menu lateral | `Panel` à esquerda, 200 px, fundo NavBack, com borda de 1 px BorderLight à direita. Itens de 184 × 34 px (espaço de 2 px entre eles), `FlatStyle.Flat` sem borda, ícone de 16 px e texto à esquerda, Segoe UI 9. Passar o mouse: fundo ActiveBack. Item ativo: fundo ActiveBack, texto em negrito, ícone na variante `teal` e barra de 4 px TealDark à esquerda. Separadores de 1 px BorderLight depois de Inicio, Teste de velocidade e Configurações |
| Conteúdo | `Panel` com fundo branco, ocupa o restante |
| Barra de status | `StatusStrip` inferior, fundo NavBack; mostra **Modo Administrador** (Segoe UI 9 negrito, WarnText, ícone `shield`) só quando o app está elevado |

Telas no menu, nesta ordem: Inicio, Downloads, Copiar Backup TIBX, Gravar ISO Bootável,
Teste de velocidade, Logs, Configurações, Sobre, Manual. Antes delas aparece a janela
**Autenticação** (token), de tamanho fixo (420 × 280 px, `FixedDialog`).

## 3. Cores em uso

Cores definidas em `Theme/UiTheme.vb`:

| Nome | Cor | Onde é usada |
| --- | --- | --- |
| Navy | `rgb(32, 45, 64)` | Fundo do cabeçalho; botões Primary e Header; títulos (Sobre, Autenticação, cartões do Inicio); valor ao vivo e linha da média no Teste de velocidade; texto de status "em andamento" |
| NavyLight | `rgb(70, 85, 105)` | Borda e hover do botão Header; hover do botão Primary |
| NavBack | `rgb(245, 247, 250)` | Menu lateral, barra de status, cabeçalho da grade, botão desabilitado, quadro da licença na tela Sobre |
| ActiveBack | `rgb(230, 236, 245)` | Item ativo e hover do menu, hover do botão Secondary, seleção da grade, hover dos cartões do Inicio, linha em medição no Teste de velocidade |
| BorderLight | `rgb(220, 224, 230)` | Bordas de painéis (grade, Logs, Manual, listas), divisores, separadores do menu, linhas da grade, borda do botão desabilitado, barras de aquecimento do gráfico |
| TitleText | `rgb(60, 70, 80)` | Título "Configurações", títulos de seção, texto do cabeçalho da grade |
| StatusText | `rgb(80, 90, 100)` | Textos de status e de seleção ("Selecionado:"), descrições (Sobre, Inicio, imagem selecionada), legendas |
| MetricText | `rgb(73, 80, 87)` | Rótulos das barras e do host no Teste de velocidade; texto dos selos neutros |
| MutedText | `rgb(108, 117, 125)` | Borda dos botões Secondary e dos campos; versão na Autenticação; cabeçalhos e legendas pequenas do Teste de velocidade |
| MainText | `rgb(33, 37, 41)` | Texto principal: menu, grade, botão Secondary, instrução da Autenticação |
| GreenBack | `rgb(225, 245, 232)` | Fundo do cartão com melhor resultado no Teste de velocidade |
| GreenDark | `rgb(30, 126, 52)` | Selo "Melhor", texto de sucesso na linha de status, linha da melhor medição |
| Red | `rgb(220, 53, 69)` | Texto e borda do botão Danger; borda da faixa de aviso e da linha de status em erro |
| WarnText | `rgb(200, 40, 40)` | "Modo Administrador" na barra de status, texto de aviso e de erro, erro do token, falhas no Teste de velocidade |
| WarningBack | `rgb(252, 234, 236)` | Fundo da faixa de aviso do Modo Administrador, da linha de status em erro e do selo "Falha"; hover do botão Danger |
| NeutralBadge | `rgb(233, 236, 239)` | Fundo dos selos neutros ("Aguardando", "Concluído", "Cancelado") |
| LogBack | `rgb(248, 250, 252)` | Fundo do registro e do gráfico no Teste de velocidade; linhas alternadas na tela Logs |
| TealDark | `rgb(14, 116, 128)` | Barra do item ativo do menu, números das etapas (1, 2), borda de campo com foco, anel de foco dos botões, selo "Medindo", barras do gráfico |

Cores escritas direto nas telas:

| Cor | Onde é usada |
| --- | --- |
| `#FFFFFF` | Fundo do conteúdo, dos cartões e do botão Secondary; texto sobre Navy, TealDark e GreenDark |
| `rgb(40, 167, 69)` | Ícone OK na grade de Downloads |
| `rgb(220, 53, 69)` | Ícone de erro na grade de Downloads |
| `rgb(108, 117, 125)` | Ícone "não verificado" na grade de Downloads |
| `rgb(0, 102, 204)` | Link "Baixar instalador da atualizacao" na tela Sobre |
| `rgb(246, 248, 250)` | Fundo da descrição da imagem selecionada (Copiar Backup TIBX / Gravar ISO Bootável) |
| `rgb(20, 29, 42)` | Botão Primary pressionado |
| `rgb(248, 215, 218)` | Botão Danger pressionado |
| `Color.Gray` | Textos secundários da janela "Nova versao disponivel" |

O azul-petróleo do logo é `rgb(27, 155, 168)`; nos controles o app usa a versão escura TealDark,
porque o tom do logo tem pouco contraste com branco. Uma cor nova deve vir em RGB, com o motivo.

## 4. Tipografia em uso

- **Segoe UI** em toda a interface (fonte do sistema). Tamanhos definidos no tema: 8 (pequena),
  9 (padrão), 9 negrito, 10 (leitura), 11 negrito (título de cartão), 12 negrito (título de
  conteúdo) e 14 negrito (título do cabeçalho). Também aparecem 10 negrito (títulos dos cartões
  do Inicio), 11 (campo do token) e 16 negrito (valor ao vivo no Teste de velocidade).
- **Consolas** nas áreas de log: 9 na coluna Evento da tela Logs e 8,5 no registro do Teste de
  velocidade.
- Não usar fontes da web nem fontes que precisem ser embutidas no app.

## 5. Medidas e componentes do tema

Medidas em px a 96 DPI (`Theme/UiTheme.vb` e `Theme/UiRounded.vb`):

| Medida | Valor |
| --- | --- |
| Altura de botão | 32 (28 na versão compacta) |
| Altura de campo (TextBox, NumericUpDown, ComboBox) | 32 |
| Raio dos cantos de botões, campos e cartões do Inicio | 6 |
| Margem interna do conteúdo | 16 |
| Espaço entre blocos | 12 |
| Espaço entre botões | 8 |
| Item do menu | 184 × 34 |
| Linha da grade / cabeçalho da grade | 30 / 32 |

Tipos de botão (`ButtonKind`), todos `FlatStyle.Flat` com borda de 1 px e cantos arredondados:

| Tipo | Fundo | Texto | Borda | Hover |
| --- | --- | --- | --- | --- |
| Primary (ação principal da tela) | Navy | branco | Navy | NavyLight |
| Secondary (demais ações) | branco | MainText | MutedText | ActiveBack |
| Danger (Zerar USB, Limpar cache de imagens) | branco | Red | Red | WarningBack |
| Header (Alterar Perfil) | Navy | branco | NavyLight | NavyLight |
| Desabilitado (qualquer tipo) | NavBack | cinza do sistema | BorderLight | — |

Ícones: PNG em `assets/icons/<tamanho>/<nome>-<variante>.png` (tamanhos 16, 24, 32 e 48). Os
botões usam 16 px, trocados pela versão de 32 px acima de 144 DPI. Variantes de cor: `dark`,
`green2`, `link`, `muted`, `navy`, `red`, `teal`, `text`, `warn`, `white`.

Outras peças do tema: linha de status com ícone por tipo (informação, andamento, sucesso, erro;
em erro, fundo WarningBack com borda Red), faixa de aviso do Modo Administrador (fundo WarningBack,
borda Red, ícone, texto e botão), número de etapa (quadro de 20 × 20 px TealDark com número
branco), painel com borda de 1 px e divisor de 1 px.

## 6. Controles disponíveis

Usados hoje: Button, Label, LinkLabel, Panel, TableLayoutPanel, FlowLayoutPanel, GroupBox,
CheckBox, RadioButton, ComboBox, ListBox, ListView (tela Logs), TextBox, NumericUpDown,
DataGridView, ProgressBar, PictureBox, StatusStrip, ToolTip, WebBrowser (tela Manual), além das
caixas de diálogo de arquivo e pasta.

Também disponíveis sem bibliotecas extras: TabControl, SplitContainer, ToolStrip, MenuStrip,
ContextMenuStrip, TreeView, ErrorProvider.

## 7. Viabilidade: classifique cada sugestão

**[Nativo]: baixo custo, pode propor livremente**
- Cores sólidas de fundo, texto e borda.
- Botões nos tipos da seção 5, com cor de hover e de clique.
- Cantos arredondados de 6 px em botões, campos e cartões (como os do Inicio): o desenho já existe no app
  (`Theme/UiRounded.vb`) e pode ser reaproveitado.
- Reorganização de layout com painéis, Dock, Anchor, Padding e Margin; agrupamento com GroupBox.
- Ícones PNG do pacote `assets/icons` em botões e PictureBox.
- DataGridView: cores do cabeçalho, linhas alternadas, cor de seleção, altura de linha,
  linhas de grade, colunas com ícone.
- Tamanho, peso e cor de fonte; ToolTips; textos de ajuda; estados habilitado/desabilitado.

**[OnPaint]: possível, mas exige desenho próprio (custo médio)**
O app já desenha: botões e campos arredondados, cartões do Inicio (`RoundedPanel`), contorno do
ComboBox, barra do item ativo do menu, imagem de fundo da tela Inicio, gráfico do Teste de
velocidade e os ícones de status da grade de Downloads.
- Cartões com borda ou cabeçalho diferentes dos existentes.
- Itens de lista com ícone ou duas linhas (ListBox/ListView owner-draw).
- Barra de progresso com cor própria.
- Interruptores (toggle) no lugar de CheckBox.

**[Evitar]: não recomendado nesta plataforma**
- Sombras, desfoque, transparência sobreposta e gradientes.
- Animações e transições.
- Efeitos de hover além da troca de cor.
- Layouts responsivos de web (breakpoints, reflow de colunas).
- Fontes da web ou tema escuro.

## 8. Regras para as propostas

1. Manter todas as funções e textos de cada tela. Se sugerir renomear algo, deixe isso explícito.
2. Desenhar cada tela em 800 × 650 px, o tamanho mínimo da janela.
3. Marcar cada sugestão com [Nativo], [OnPaint] ou [Evitar].
4. Usar as cores, fontes e medidas das seções 3, 4 e 5. Uma cor nova deve vir em RGB, com o motivo.
5. Contraste de texto de pelo menos 4,5:1.
6. Mostrar os estados relevantes: sem Modo Administrador (aviso visível), botão desabilitado,
   operação em andamento (barra de progresso) e erro.

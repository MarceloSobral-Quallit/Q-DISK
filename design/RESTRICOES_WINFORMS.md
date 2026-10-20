# qdisk: restrições de design (WinForms)

Este documento descreve a plataforma em que as telas são implementadas e o que é viável nela.
Toda proposta de design deve respeitar estas regras. Os valores abaixo foram tirados do código
do aplicativo (pasta `codigo-ui/`), não de suposições.

## 1. Plataforma

- Aplicativo desktop Windows em **WinForms**, **.NET 8** (`net8.0-windows`), escrito em **VB.NET**.
- Apenas os **controles padrão do WinForms**. Não há biblioteca visual de terceiros
  (os únicos pacotes são DiscUtils e System.Management, sem relação com a interface).
- DPI: `Application.SetHighDpiMode(HighDpiMode.SystemAware)`, `EnableVisualStyles()` e
  `AutoScaleMode.Font` em todas as telas.
- Janela principal: tamanho inicial e **mínimo de 800 × 650 px**. As propostas devem funcionar
  nesse tamanho e crescer com a janela (Dock/Anchor), sem rolagem horizontal.
- Sem tema escuro: o WinForms do .NET 8 não oferece modo escuro nativo.
- Textos da interface em português do Brasil. Nomes de botões e telas aparecem no manual do
  usuário; se forem alterados, o manual precisa ser atualizado junto.

## 2. Estrutura atual da janela principal

| Área | Implementação atual |
| --- | --- |
| Cabeçalho | `Panel` no topo, altura 56 px, fundo `rgb(32, 45, 64)`; título da tela em Segoe UI 14 negrito, branco; botão **Alterar Perfil** à direita (150 px de largura, flat, borda `rgb(70, 85, 105)`) |
| Menu lateral | `Panel` à esquerda, 200 px, fundo `rgb(245, 247, 250)`; botões de 170 × 36 px, `FlatStyle.Flat`, fundo branco, borda `rgb(220, 224, 230)`, texto à esquerda; item ativo com fundo `rgb(230, 236, 245)` e texto em negrito |
| Conteúdo | `Panel` com fundo branco, ocupa o restante |
| Barra de status | `StatusStrip` inferior; mostra **Modo Administrador** em vermelho quando o app está elevado |

Telas no menu (ordem atual): Inicio, Downloads, Copiar Backup TIBX, Gravar ISO Bootável,
Teste de velocidade, Logs, Configurações, Sobre, Manual. Antes delas aparece a janela
**Autenticação** (token), de tamanho fixo.

## 3. Cores em uso

| Cor | Onde é usada |
| --- | --- |
| `rgb(32, 45, 64)` | Fundo do cabeçalho e do botão Alterar Perfil; títulos |
| `rgb(70, 85, 105)` | Borda do botão Alterar Perfil |
| `rgb(245, 247, 250)` | Fundo do menu lateral |
| `rgb(230, 236, 245)` | Fundo do item ativo do menu |
| `rgb(220, 224, 230)` | Borda dos botões do menu |
| `#FFFFFF` | Fundo do conteúdo e dos botões do menu |
| `rgb(60, 70, 80)` | Títulos de conteúdo (ex.: "Configurações") |
| `rgb(80, 90, 100)` | Textos de status e de seleção ("Selecionado: …", "Pronto.") |
| `rgb(73, 80, 87)` | Rótulos das métricas no Teste de velocidade |
| `rgb(108, 117, 125)` | Textos secundários; ícone "status desconhecido" na grade |
| `rgb(33, 37, 41)` | Texto principal da janela de Autenticação |
| `rgb(40, 167, 69)` | Verde: botão Validar, ícone OK na grade, selo "Melhor" |
| `rgb(225, 245, 232)` | Fundo do cartão com melhor resultado no Teste de velocidade |
| `rgb(220, 53, 69)` | Vermelho: botão Sair, ícone de erro na grade |
| `rgb(200, 40, 40)` | Texto do aviso "Ative o Modo Administrador" |
| `rgb(233, 236, 239)` | Fundo dos selos neutros ("Aguardando", "Disponivel") |
| `rgb(0, 102, 204)` | Link "Baixar instalador da atualizacao" |
| `Color.Gray` | Textos descritivos na tela Sobre |

A marca usa o azul-petróleo e o laranja do logo (`marca/logo-quallit-512x512.png`). Esses tons
não aparecem hoje como cores de controles; se forem propostos, informe os valores em RGB.

## 4. Tipografia em uso

- **Segoe UI** em toda a interface (fonte do sistema). Tamanhos usados: 8, 9 (padrão), 10,
  12 negrito, 14 negrito (título do cabeçalho) e 18.
- **Consolas** nas áreas de log (tela Logs e registro do Teste de velocidade).
- Não usar fontes da web nem fontes que precisem ser embutidas no app.

## 5. Controles disponíveis

Usados hoje: Button, Label, LinkLabel, Panel, TableLayoutPanel, FlowLayoutPanel, GroupBox,
CheckBox, RadioButton, ComboBox, ListBox, TextBox, NumericUpDown, DataGridView, ProgressBar,
PictureBox, StatusStrip, WebBrowser (tela Manual).

Também disponíveis sem bibliotecas extras: TabControl, SplitContainer, ToolTip, ToolStrip,
MenuStrip, ContextMenuStrip, ListView, TreeView, ErrorProvider.

## 6. Viabilidade: classifique cada sugestão

**[Nativo]: baixo custo, pode propor livremente**
- Cores sólidas de fundo, texto e borda.
- Botões `FlatStyle.Flat` com cor de borda, espessura da borda, cor ao passar o mouse
  (`MouseOverBackColor`) e ao clicar (`MouseDownBackColor`).
- Reorganização de layout com painéis, Dock, Anchor, Padding e Margin; agrupamento com GroupBox.
- Ícones PNG em botões e PictureBox.
- DataGridView: cores do cabeçalho, linhas alternadas, cor de seleção, altura de linha,
  linhas de grade, colunas com ícone.
- Tamanho, peso e cor de fonte; ToolTips; textos de ajuda; estados habilitado/desabilitado.

**[OnPaint]: possível, mas exige desenho próprio (custo médio)**
O app já faz isso em poucos lugares: rótulo justificado na tela Sobre, imagem de fundo da tela
Inicio, borda dos botões na Autenticação e ícones de status desenhados na grade de Downloads.
- Cantos arredondados em botões, cartões ou selos.
- Cartões com borda ou cabeçalho personalizado.
- Itens de lista com ícone ou duas linhas (ListBox/ListView owner-draw).
- Barra de progresso com cor própria.
- Interruptores (toggle) no lugar de CheckBox.

**[Evitar]: não recomendado nesta plataforma**
- Sombras, desfoque, transparência sobreposta e gradientes.
- Animações e transições.
- Efeitos de hover além da troca de cor.
- Layouts responsivos de web (breakpoints, reflow de colunas).
- Fontes da web ou tema escuro.

## 7. Padrão visual adotado (out/2026)

O redesenho aprovado está implementado em `src/qdisk.UI/Theme/`
(`UiTheme.vb`, `UiControls.vb`, `UiStatus.vb`, `UiRounded.vb`). Novas propostas partem dele:

- Botões e campos com **cantos arredondados de 6 px**, desenhados com suavização (`UiRounded.vb`).
  Esse desenho próprio já existe: propor cantos de 6 px em botões e campos é **[Nativo]** para este app.
- Campos de entrada (TextBox, NumericUpDown, ComboBox) com **32 px de altura**, a mesma dos botões.
- Botões: Primary (azul-marinho), Secondary (branco, borda cinza) e Danger (texto vermelho).
- Grade, listas e painéis de aviso continuam com cantos retos.

## 8. Regras para as propostas

1. Manter todas as funções e textos de cada tela. Se sugerir renomear algo, deixe isso explícito.
2. Desenhar cada tela em 800 × 650 px, o tamanho mínimo da janela.
3. Marcar cada sugestão com [Nativo], [OnPaint] ou [Evitar].
4. Usar as cores e fontes das seções 3 e 4. Uma cor nova deve vir em RGB, com o motivo.
5. Contraste de texto de pelo menos 4,5:1.
6. Mostrar os estados relevantes: sem Modo Administrador (aviso visível), botão desabilitado,
   operação em andamento (barra de progresso) e erro.

Quero sugestões de melhoria do design das telas do qdisk, um aplicativo desktop Windows.

IMPORTANTE: o aplicativo é feito em WinForms (.NET 8, VB.NET), só com os controles padrão, sem bibliotecas visuais de terceiros. As regras completas estão em RESTRICOES_WINFORMS.md. Resumo:
- Tamanho mínimo da janela: 800 × 650 px. Desenhe cada tela nesse tamanho.
- Fonte Segoe UI (Consolas nas áreas de log). Sem fontes da web.
- Use as cores que o app já usa (seção 3 do documento). Uma cor nova deve vir em RGB, com o motivo.
- Viável sem custo: cores sólidas, botões flat com borda e cor de hover, reorganização de layout, ícones PNG, estilos da DataGridView.
- Exige desenho próprio (OnPaint): cantos arredondados, cartões personalizados, listas com ícone, toggles.
- Evitar: sombras, gradientes, transparência, animações, layout responsivo de web, tema escuro.

O que está no pacote:
- telas/: capturas atuais de cada tela, sem o Modo Administrador ativo.
- codigo-ui/: código das telas (arquivos .vb e .Designer.vb), com cores, fontes e tamanhos reais.
- marca/: logo, ícone e imagem de fundo da tela Inicio.

O que eu preciso:
1. Para cada tela (Autenticação, Inicio, Downloads, Copiar Backup TIBX, Gravar ISO Bootável, Teste de velocidade, Logs, Configurações, Sobre e Manual), uma prancha com a tela atual ao lado de uma proposta melhorada.
2. Comece pelas telas mais usadas: Downloads, Copiar Backup TIBX e Gravar ISO Bootável.
3. Em cada proposta, uma lista curta das mudanças, cada uma marcada como [Nativo], [OnPaint] ou [Evitar], conforme o documento de restrições.
4. Mantenha todas as funções e os textos das telas. Se sugerir renomear algo, indique explicitamente.
5. Mostre também os estados: aviso de Modo Administrador visível, botões desabilitados, operação em andamento e erro.
6. Uma proposta coerente entre as telas: o mesmo cabeçalho, menu lateral, estilo de botões e espaçamentos.

Ao final, prepare o pacote de handoff para o Claude Code implementar em WinForms.

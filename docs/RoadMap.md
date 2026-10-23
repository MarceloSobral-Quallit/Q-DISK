# RoadMap

Histórico de versões do qdisk. Formato da versão: `Maior.Menor.Mês.Ano` (ver `bump_version()` em
[`tools/build_release_OV.py`](../tools/build_release_OV.py); `--set-version` fixa `Maior.Menor`).

## 1.00.10.26 — 23/10/2026

Primeira versão oficial.

- Instaladores e executáveis assinados só com o certificado OV da Quallit (QUALLIT SERVICOS DE INFORMATICA LTDA,
  DigiCert, chave no Google Cloud KMS). A atualização automática deixa de aceitar o certificado autoassinado.
- A atualização automática reconhece o assinante pela chave pública do certificado e aceita a renovação do
  certificado da Quallit emitida pela DigiCert, sem exigir uma versão intermediária (republicada sem bump).
- Manual de instalação, com a verificação do certificado do instalador, e PDFs dos manuais em
  [`docs/manual`](manual).
- Desenvolvimento: um só pipeline de release (`tools/build_release_OV.py`), usado também pelo QDisk_Adm; sai o
  pipeline v4 com o PFX. Novas opções `--set-version` e `--fallback-only`.

## 0.02.10.26 — 23/10/2026

- Primeira versão assinada com o certificado OV (executáveis, Setup e desinstalador), com o código da 0.01.

## 0.01.10.26 — 23/10/2026

- O app passa a aceitar instaladores de atualização assinados com o certificado OV, além do autoassinado (versão de
  transição, ainda assinada com o PFX).

## 0.00.10.26 — 20/10/2026

- Versão inicial do projeto qdisk.

:: ─────────────────────────────────────────────────────────────────────────────
:: qdisk  |  Tools / Utils  |  Build-OneFile-Sign_OV.bat
:: Wrapper de entrada do pipeline OV (assinatura com o certificado OV via signkit),
:: chamado pelo QDisk_Adm_OV no "Executar build completo".
::
:: © Quallit — Licenciado sob GPL-3.0-or-later.
:: ─────────────────────────────────────────────────────────────────────────────
@echo off
cd /d "%~dp0..\.."

python tools\build_release_OV.py %*
if errorlevel 1 exit /b %errorlevel%

exit /b 0

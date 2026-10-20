:: ─────────────────────────────────────────────────────────────────────────────
:: qdisk  |  Tools / Utils  |  Build-OneFile-Sign_v4.bat
:: Wrapper de entrada para build, assinatura, publicacao online e envio Git installer.
::
:: © Quallit — Licenciado sob GPL-3.0-or-later.
:: ─────────────────────────────────────────────────────────────────────────────
@echo off
cd /d "%~dp0..\.."

python tools\build_release_dotnet_4.py %*
if errorlevel 1 exit /b %errorlevel%

exit /b 0

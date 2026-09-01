@echo off
setlocal
cd /d "%~dp0"
title NosAi - banco di prova

rem Leggere la memoria del client richiede privilegi da amministratore:
rem senza, l'aggancio fallisce con access_denied e non si capisce perche'.
fltmc >nul 2>&1
if errorlevel 1 (
  echo Servono i privilegi di amministratore: conferma la richiesta di Windows.
  powershell -NoProfile -Command "Start-Process -FilePath '%~f0' -Verb RunAs"
  exit /b
)

echo Aggiorno il runtime con l'ultimo codice...
dotnet build src\NosAi.Runtime\NosAi.Runtime.csproj -v q --nologo
if errorlevel 1 goto failed

"src\NosAi.Runtime\bin\Debug\net8.0-windows\NosAi.Runtime.exe" --menu
goto done

:failed
echo.
echo *** La compilazione e' fallita: non avvio niente. ***
echo Copia l'errore qui sopra e mandalo a Claude.
echo.
pause
exit /b 1

:done
endlocal

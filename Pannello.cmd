@echo off
setlocal
cd /d "%~dp0"
title NosAi - pannello di controllo

rem Il pannello legge la memoria del client (aggancio, posizione, campioni
rem schermo): senza privilegi da amministratore l'aggancio fallisce con
rem access_denied e dal pannello sembra che il client non ci sia.
fltmc >nul 2>&1
if errorlevel 1 (
  echo Servono i privilegi di amministratore: conferma la richiesta di Windows.
  powershell -NoProfile -Command "Start-Process -FilePath '%~f0' -Verb RunAs"
  exit /b
)

echo Aggiorno il pannello con l'ultimo codice...
dotnet build src\NosAi.ControlPanel\NosAi.ControlPanel.csproj -c Release -v q --nologo
if errorlevel 1 goto failed

rem Il pannello parte da bin e non da una copia temporanea, al contrario del
rem banco: la radice del repository la trova risalendo da dove sta l'eseguibile
rem fino a NosAi.sln, ed e' quella radice che decide dove finiscono i campioni
rem schermo e le registrazioni. Da %TEMP% non la troverebbe, e scriverebbe
rem altrove -- che e' esattamente come il 2026-09-07 i campioni sono finiti in
rem C:\WINDOWS\system32\data\. In cambio: finche' il pannello e' aperto non si
rem puo' ricompilare il pannello. Chiudilo prima.
set "PANEL=src\NosAi.ControlPanel\bin\Release\net8.0-windows\NosAi.ControlPanel.exe"
if not exist "%PANEL%" goto missing

start "" "%PANEL%"
goto done

:missing
echo.
echo *** Compilato, ma %PANEL% non c'e'. ***
echo Manda questo messaggio a Claude.
echo.
pause
exit /b 1

:failed
echo.
echo *** La compilazione e' fallita: non avvio niente. ***
echo Copia l'errore qui sopra e mandalo a Claude.
echo.
pause
exit /b 1

:done
endlocal

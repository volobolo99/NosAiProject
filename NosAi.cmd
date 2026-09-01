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

rem Il banco gira da una copia usa e getta, mai da bin. Un exe in esecuzione non
rem si lascia sovrascrivere, quindi un banco lasciato aperto bloccherebbe la
rem compilazione successiva: la propria, e quella di chi sta scrivendo il codice.
set "RUNROOT=%TEMP%\NosAi-bench"
for /d %%D in ("%RUNROOT%\*") do rmdir /s /q "%%D" 2>nul
set "RUNDIR=%RUNROOT%\%RANDOM%%RANDOM%"
mkdir "%RUNDIR%" 2>nul
robocopy "src\NosAi.Runtime\bin\Debug\net8.0-windows" "%RUNDIR%" /E /NJH /NJS /NP /NDL /NFL >nul
if errorlevel 8 goto copyfailed

rem La cartella di lavoro resta il repository: e' li' che va scritto
rem data\mapid_candidates.txt, non accanto alla copia temporanea.
"%RUNDIR%\NosAi.Runtime.exe" --menu
goto done

:copyfailed
echo.
echo *** Non riesco a preparare la copia temporanea del banco. ***
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

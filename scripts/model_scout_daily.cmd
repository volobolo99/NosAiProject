@echo off
REM Ricognizione quotidiana del catalogo modelli.
REM Registrato in Utilita' di pianificazione come NosAi-ModelScout.
REM Il guardiano propone e non dispone: scrive rapporto, storico e proposte,
REM e non tocca mai un binding ne' un modello a pagamento.

setlocal
set REPO=C:\Users\volob\Desktop\NosAiProject
set PYEXE=C:\Users\volob\AppData\Local\Python\bin\python.exe
set LOGDIR=%REPO%\data\model_scout_logs

if not exist "%PYEXE%" (
  echo [%DATE% %TIME%] interprete assente: %PYEXE%
  exit /b 2
)
if not exist "%LOGDIR%" mkdir "%LOGDIR%"

cd /d "%REPO%" || exit /b 2

REM Un file di log per giorno, in append: due esecuzioni nello stesso giorno
REM restano entrambe leggibili.
for /f "tokens=1-3 delims=/-. " %%a in ("%DATE%") do set OGGI=%%c-%%b-%%a

echo. >> "%LOGDIR%\%OGGI%.log"
echo ===== avvio %DATE% %TIME% ===== >> "%LOGDIR%\%OGGI%.log"
"%PYEXE%" -u scripts\model_scout.py >> "%LOGDIR%\%OGGI%.log" 2>&1
set ESITO=%ERRORLEVEL%
echo ===== uscita %ESITO% alle %TIME% ===== >> "%LOGDIR%\%OGGI%.log"

exit /b %ESITO%

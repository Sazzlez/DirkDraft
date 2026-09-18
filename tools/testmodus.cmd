@echo off
rem ============================================================================
rem  DirkDraft Testmodus - spielt einen aufgezeichneten Draft ab, damit man das
rem  Fenster durchklicken kann, ohne in League zu sein.
rem
rem  Die Wiedergabe laeuft ab, danach bleibt das Fenster offen und bedienbar:
rem  Mitspieler anklicken, Zeilen aufklappen, Lane-Auswahl beim Gegner aendern.
rem  Der Testmodus umgeht die Einzelinstanz-Sperre, laeuft also neben dem
rem  richtigen DirkDraft - das darf ruhig weiterlaufen.
rem
rem  Die Daten sind echt (Snapshot und OP.GG), nur der Draft ist aufgezeichnet.
rem  Zum Beenden das Fenster schliessen; es legt sich wie gewohnt in den Tray,
rem  von dort "Beenden".
rem ============================================================================
setlocal
title DirkDraft Testmodus

set ROOT=%~dp0..
set EXE=%ROOT%\src\DraftPilot.App\bin\Debug\net9.0-windows\DirkDraft.exe
set REC=%ROOT%\recordings

if not exist "%EXE%" (
  echo.
  echo   Der Entwicklungs-Build fehlt:
  echo   %EXE%
  echo.
  echo   Einmal bauen: Menuepunkt B.
  echo.
  pause
)

:menu
cls
echo.
echo    DirkDraft Testmodus
echo    ===================
echo.
echo    Waehrend des Drafts
echo      1   Du bist am Zug - Pick         (Vorschlagsliste, Matchup-Zeile)
echo      2   Du bist am Zug - Ban          (Ban-Liste)
echo      3   Gegner bannt                  (Planungsphase)
echo      4   Nach deinem Lock              (Matchup-Karte und Build gross)
echo      5   Mitspieler pickt              (Liste fuer ihn, dein Matchup bleibt)
echo.
echo    Sonderfaelle
echo      6   Blind Pick - noch nicht gepickt
echo      7   Blind Pick - gelockt          (Ersatzgegner im Build)
echo      8   ARAM                          (Hinweis-Chip)
echo      9   Finalisierung                 (Warten auf Spielstart)
echo.
echo    Rund ums Spiel
echo      I   Im Spiel                      (Runen, Kaufen, Skills, Lanes)
echo      E   Nach dem Spiel                (zurueck zum Startbildschirm)
echo.
echo      B   Neu bauen
echo      Q   Beenden
echo.
choice /c 123456789IEBQ /n /m "   Auswahl: "
if errorlevel 255 exit /b 1
set wahl=%errorlevel%

if "%wahl%"=="1" call :start state-myturn "" & goto menu
if "%wahl%"=="2" call :start state-myban "" & goto menu
if "%wahl%"=="3" call :start state-enemyban "" & goto menu
if "%wahl%"=="4" call :start state-nachlock "" & goto menu
if "%wahl%"=="5" call :start build-demo "" & goto menu
if "%wahl%"=="6" call :start state-blindpick "" & goto menu
if "%wahl%"=="7" call :start state-blind-locked "" & goto menu
if "%wahl%"=="8" call :start state-aram "" & goto menu
if "%wahl%"=="9" call :start state-final "" & goto menu
if "%wahl%"=="10" call :start test-imspiel "InProgress" & goto menu
if "%wahl%"=="11" call :start test-imspiel "InProgress,EndOfGame" & goto menu
if "%wahl%"=="12" goto bauen
if "%wahl%"=="13" exit /b 0
goto menu

:bauen
cls
echo.
echo    Baue ...
echo.
pushd "%ROOT%"
dotnet build DraftPilot.sln -m:1 --nologo
popd
echo.
pause
goto menu

:start
rem  %1 = Aufnahme ohne Endung, %2 = Phasen in Anfuehrungszeichen ("" = keine)
if not exist "%REC%\%~1.jsonl" (
  echo.
  echo   Aufnahme fehlt: %REC%\%~1.jsonl
  echo.
  pause
  exit /b 0
)

if "%~2"=="" (
  rem  Zuegig durchspielen; die Aufnahme haelt am Ende an und das Bild steht.
  start "" "%EXE%" --demo "%REC%\%~1.jsonl" --frames 99 --speed 50
  exit /b 0
)

rem  Fuer die Spielansicht in Echtzeit: der Draft muss fertig sein UND der Build
rem  geladen, bevor die Phase gesetzt wird. Die Aufnahme laesst dem Abruf Zeit,
rem  die App wartet zusaetzlich auf den Build - bei kaltem Zwischenspeicher oder
rem  nach einem Patch dauert das ein paar Sekunden.
echo.
echo    Startet ... die Spielansicht erscheint, sobald der Build geladen ist.
echo.
start "" "%EXE%" --demo "%REC%\%~1.jsonl" --frames 99 --speed 1 --phase "%~2"
timeout /t 3 >nul
exit /b 0

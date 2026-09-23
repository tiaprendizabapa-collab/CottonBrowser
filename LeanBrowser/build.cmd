@echo off
setlocal
cd /d "%~dp0"
set "DOTNET=dotnet"
if exist "%~dp0..\.tools\dotnet\dotnet.exe" set "DOTNET=%~dp0..\.tools\dotnet\dotnet.exe"
set "RESULT=1"

REM ---------------------------------------------------------------
REM LeanBrowser - build de release
REM Requer: .NET 8 SDK  (winget install Microsoft.DotNet.SDK.8)
REM ---------------------------------------------------------------

"%DOTNET%" --version >nul 2>nul
if errorlevel 1 (
    echo.
    echo ERRO: nenhum SDK .NET utilizavel foi encontrado.
    echo.
    echo O .NET 8 SDK nao parece estar instalado. Instale com:
    echo     winget install Microsoft.DotNet.SDK.8
    echo Ou baixe em: https://dotnet.microsoft.com/download/dotnet/8.0
    echo.
    echo Depois de instalar, FECHE esta janela e clique 2x em build.cmd de novo
    echo ^(pode ser necessario reiniciar o Windows para o comando ser reconhecido^).
    goto :fim
)

echo Versao do dotnet encontrada:
"%DOTNET%" --version
echo.

echo [1/4] Restaurando pacotes...
"%DOTNET%" restore
if errorlevel 1 (
    echo.
    echo ERRO no "dotnet restore" ^(veja o texto acima^).
    echo Causa mais comum: sem internet, ou proxy/firewall bloqueando o NuGet.
    goto :fim
)

echo.
echo [2/4] Publicando ^(single-file, framework-dependent, ReadyToRun^)...
"%DOTNET%" publish -c Release -r win-x64 -o .\dist
if errorlevel 1 (
    echo.
    echo ERRO no "dotnet publish" ^(veja o texto acima para a causa exata^).
    goto :fim
)

echo.
echo [3/4] Publicando o atualizador...
"%DOTNET%" publish ..\CottonUpdater\CottonUpdater.csproj -c Release -r win-x64 -o ..\CottonUpdater\dist
if errorlevel 1 (
    echo ERRO ao publicar o atualizador.
    goto :fim
)
copy /y "..\CottonUpdater\dist\CottonUpdater.exe" ".\dist\CottonUpdater.exe" >nul
if errorlevel 1 (
    echo ERRO ao copiar o atualizador.
    goto :fim
)

echo [4/4] Pronto.
echo.
echo Executavel: %CD%\dist\CottonBrowser.exe
echo.
dir /b .\dist\*.exe
set "RESULT=0"

:fim
echo.
echo ---------------------------------------------------------------
if /i not "%~1"=="--no-pause" pause
exit /b %RESULT%

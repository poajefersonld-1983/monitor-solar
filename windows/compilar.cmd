@echo off
setlocal
cd /d "%~dp0"
set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" set "CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if not exist "%CSC%" (
    echo Compilador do .NET Framework indisponivel.
    exit /b 1
)
"%CSC%" /nologo /codepage:65001 /optimize+ /target:winexe /out:MonitorSolar.exe /reference:System.Windows.Forms.dll,System.Drawing.dll,System.Net.Http.dll,System.Web.Extensions.dll,System.Security.dll MonitorSolar.cs
exit /b %errorlevel%

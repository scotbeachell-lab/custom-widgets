@echo off
rem Builds WidgetHost.exe with the C# compiler that ships with Windows. Nothing to install.
setlocal
set FW=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319
set WPF=%FW%\WPF

taskkill /im WidgetHost.exe /f >nul 2>&1

"%FW%\csc.exe" /nologo /target:winexe /platform:x64 /optimize+ ^
  /out:"%~dp0WidgetHost.exe" ^
  /r:"%WPF%\PresentationFramework.dll" ^
  /r:"%WPF%\PresentationCore.dll" ^
  /r:"%WPF%\WindowsBase.dll" ^
  /r:"%FW%\System.Xaml.dll" ^
  /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Net.Http.dll /r:System.Web.Extensions.dll ^
  "%~dp0src\*.cs"
exit /b %errorlevel%

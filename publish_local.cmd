@echo off
rem Local build. Requires .NET 8 SDK: https://dotnet.microsoft.com/download/dotnet/8.0
where dotnet >nul 2>nul
if errorlevel 1 (
  echo [ERROR] dotnet SDK not found. Install .NET 8 SDK first.
  exit /b 1
)
dotnet publish "%~dp0MyFiles.csproj" -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -o "%~dp0publish"
if errorlevel 1 (
  echo [ERROR] Build failed.
  exit /b 1
)
echo.
echo Done. EXE: "%~dp0publish\MyFiles.exe"

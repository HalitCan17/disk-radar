@echo off
rem Builds a single self-contained DiskRadar.exe into ..\dist (no .NET install needed on the target PC)
dotnet publish "%~dp0DiskRadar.csproj" -c Release -r win-x64 --self-contained true ^
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true ^
  -p:DebugType=none -o "%~dp0..\dist"

@echo off
rem use .NET 3.5 to build
Prebuild.exe /target vs2008
IF  NOT ERRORLEVEL 0 GOTO FAIL

%WINDIR%\Microsoft.NET\Framework\v3.5\msbuild opensim.sln
IF  NOT ERRORLEVEL 0 GOTO FAIL

echo Build success, creating zip package
cd bin
rmdir /q /s fortis-opensim-autobuild
ren Debug fortis-opensim-autobuild
del /q fortis-opensim-autobuild.zip
7z -tzip a fortis-opensim-autobuild.zip fortis-opensim-autobuild
cd ..


:SUCCESS
exit /B 0

:FAIL
exit /B 1

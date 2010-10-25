@echo off

set makearch=
set makedist=

rem Set flags according to passed command line params
:ParamLoop
IF "%1"=="" GOTO ParamContinue
IF "%1"=="arch" set makearch=yes
IF "%1"=="dist" set makedist=yes
SHIFT
GOTO ParamLoop
:ParamContinue

rem use .NET 3.5 to build
Prebuild.exe /target vs2008
IF NOT ERRORLEVEL 0 GOTO FAIL

%WINDIR%\Microsoft.NET\Framework\v3.5\msbuild /t:Rebuild opensim.sln
IF NOT ERRORLEVEL 0 GOTO FAIL

IF NOT "%makearch%"=="yes" GOTO SkipArch
echo Build success, creating zip package
cd bin
rmdir /q /s fortis-opensim-autobuild
ren Debug fortis-opensim-autobuild
del /q fortis-opensim-autobuild.zip
7z -tzip a fortis-opensim-autobuild.zip fortis-opensim-autobuild
rmdir /q /s fortis-opensim-autobuild
cd ..
:SkipArch

:SUCCESS
exit /B 0

:FAIL
exit /B 1

@echo OFF

REM We forward the arguments to ZeroC.Slice.Generator, even though it currently rejects all arguments.
call dotnet "%~dp0\ZeroC.Slice.Generator.dll" -- %*

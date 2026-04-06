@echo OFF

REM We forward the arguments to ZeroC.Slice.Generator, even though it currently ignores all arguments.
call dotnet "%~dp0\ZeroC.Slice.Generator.dll" -- %*

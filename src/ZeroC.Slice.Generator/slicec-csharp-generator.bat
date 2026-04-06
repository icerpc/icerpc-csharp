@echo OFF

REM We forward the arguments to ZeroC.Slice.Generator, even though ZeroC.Slice.Generator currently rejects all arguments.
call dotnet "%~dp0\ZeroC.Slice.Generator.dll" -- %*

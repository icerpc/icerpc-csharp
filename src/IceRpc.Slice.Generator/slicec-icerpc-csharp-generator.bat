@echo OFF

REM We forward the arguments to IceRpc.Slice.Generator, even though it currently rejects all arguments.
call dotnet "%~dp0\IceRpc.Slice.Generator.dll" -- %*

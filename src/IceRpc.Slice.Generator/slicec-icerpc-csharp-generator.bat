@echo OFF

REM We forward the arguments to IceRpc.Slice.Generator, even though IceRpc.Slice.Generator currently rejects all arguments.
call dotnet "%~dp0\IceRpc.Slice.Generator.dll" -- %*

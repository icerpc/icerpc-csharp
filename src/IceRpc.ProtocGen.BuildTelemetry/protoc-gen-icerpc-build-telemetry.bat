@echo OFF

call dotnet %~dp0\IceRpc.ProtocGen.BuildTelemetry.dll -- %*

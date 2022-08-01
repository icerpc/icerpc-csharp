Server:

dotnet run --project Server

dotnet-counters monitor --name Server --counters IceRpc.Dispatch,IceRpc.Invocation

or

dotnet-trace collect --name Server --providers IceRpc.Dispatch,IceRpc.Invocation

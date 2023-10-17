// Copyright (c) ZeroC, Inc.

namespace IceRpc.Protobuf.Generators.Internal;

/// <summary>Represents an RPC operation annotated with the <c>IceRpc.Protobuf.ProtobufOperationAttribute</c>
/// attribute.</summary>
internal readonly record struct ServiceMethod
{
    // The fully qualified name of the generated dispatch helper method, for example:
    // "VisitorCenter.GreetAsync"
    internal string DispatchMethodName { get; }

    // The name of the service operation as defined in the Protobuf service, for example:
    // "Greet"
    internal string OperationName { get; }

    internal ServiceMethod(string dispatchMethodName, string operationName)
    {
        DispatchMethodName = dispatchMethodName;
        OperationName = operationName;
    }
}

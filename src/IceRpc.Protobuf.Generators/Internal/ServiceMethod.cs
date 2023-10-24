// Copyright (c) ZeroC, Inc.

namespace IceRpc.Protobuf.Generators.Internal;

/// <summary>Represents an RPC method on a Protobuf service.</summary>
internal readonly record struct ServiceMethod
{
    // The fully qualified input type name (in C#). For example: "global::VisitorCenter.GreetRequest".
    internal string InputTypeName { get; }

    // The fully qualified name of the mapped C# Service interface. For example:
    // "global::VisitorCenter.IGreeterService".
    internal string InterfaceName { get; }

    // Indicates if the client streams multiple requests.
    internal bool IsClientStreaming { get; }

    // Indicates if the server streams multiple responses.
    internal bool IsServerStreaming { get; }

    // The name of the mapped C# method on the Service interface. For example: "GreetAsync".
    internal string MethodName { get; }

    // The name of the Protobuf rpc method as specified in the Protobuf file. It's also used as the IceRPC operation
    // name. For example: "Greet".
    internal string OperationName { get; }

    internal ServiceMethod(
        string operationName,
        string interfaceName,
        string methodName,
        string inputTypeName,
        bool isClientStreaming,
        bool isServerStreaming)
    {
        OperationName = operationName;
        InterfaceName = interfaceName;
        MethodName = methodName;
        InputTypeName = inputTypeName;
        IsClientStreaming = isClientStreaming;
        IsServerStreaming = isServerStreaming;
    }
}

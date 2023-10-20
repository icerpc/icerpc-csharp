// Copyright (c) ZeroC, Inc.

namespace IceRpc.Protobuf.Generators.Internal;

/// <summary>Represents a RPC method on a Protobuf Service.</summary>
internal readonly record struct ServiceMethod
{
    // The name of the Protobuf rpc method as specified in the Protobuf file. It's also used as the IceRPC operation
    // name. For example: "Greet".
    internal string OperationName { get; }

    // The fully qualified name of the mapped C# Service interface. For example:
    // "global::VisitorCenter.IGreeterService".
    internal string InterfaceName { get; }

    // The name of the mapped C# method on the Service interface. For example: "GreetAsync".
    internal string MethodName { get; }

    // The fully qualified input type name (in C#). For example: "global::Google.Protobuf.WellKnownTypes.Empty".
    internal string InputTypeName { get; }

    internal ServiceMethod(string operationName, string interfaceName, string methodName, string inputTypeName)
    {
        OperationName = operationName;
        InterfaceName = interfaceName;
        MethodName = methodName;
        InputTypeName = inputTypeName;
    }
}

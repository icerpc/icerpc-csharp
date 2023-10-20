// Copyright (c) ZeroC, Inc.

using Microsoft.CodeAnalysis;

namespace IceRpc.Protobuf.Generators.Internal;

internal static class DiagnosticDescriptors
{
    internal static DiagnosticDescriptor DuplicateRpcName { get; } = new DiagnosticDescriptor(
        id: "PROTO0001",
        title: "Multiple Protobuf rpc methods cannot have the same name within a service class",
        messageFormat: "Multiple Protobuf rpc methods named {0} in class {1}",
        category: "ProtobufServiceGenerator",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);
}

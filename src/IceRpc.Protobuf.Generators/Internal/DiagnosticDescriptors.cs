// Copyright (c) ZeroC, Inc.

using Microsoft.CodeAnalysis;

namespace IceRpc.Protobuf.Generators.Internal;

internal static class DiagnosticDescriptors
{
    internal static DiagnosticDescriptor DuplicateOperationNames { get; } = new DiagnosticDescriptor(
        id: "PROTO0001",
        title: "Multiple Protobuf operations cannot have the same name within a service class",
        messageFormat: "Multiple Protobuf operations named {0} in class {1}",
        category: "ProtobufServiceGenerator",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);
}

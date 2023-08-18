// Copyright (c) ZeroC, Inc.

using Microsoft.CodeAnalysis;

namespace IceRpc.Slice.Generators.Internal;

internal static class DiagnosticDescriptors
{
    internal static DiagnosticDescriptor ShouldntReuseOperationNames { get; } = new DiagnosticDescriptor(
        id: "SLICE1001",
        title: "Multiple RPC operatrions cannot use the same name within a service class",
        messageFormat: "Multiple RPC operations are using name {0} in class {1}",
        category: "SliceServiceGenerator",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);
}

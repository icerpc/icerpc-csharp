// Copyright (c) ZeroC, Inc.

using Microsoft.CodeAnalysis;

namespace IceRpc.Slice.Generators.Internal;

internal static class DiagnosticDescriptors
{
    internal static DiagnosticDescriptor DuplicateOperationNames { get; } = new DiagnosticDescriptor(
        id: "SLICE0001",
        title: "Multiple Slice operations cannot have the same name within a service class",
        messageFormat: "Multiple Slice operations named {0} in class {1}",
        category: "SliceServiceGenerator",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);
}

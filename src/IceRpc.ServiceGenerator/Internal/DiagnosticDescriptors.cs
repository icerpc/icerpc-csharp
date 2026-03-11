// Copyright (c) ZeroC, Inc.

using Microsoft.CodeAnalysis;

namespace IceRpc.ServiceGenerator.Internal;

internal static class DiagnosticDescriptors
{
    internal static DiagnosticDescriptor DuplicateOperationNames { get; } = new DiagnosticDescriptor(
        id: "IRSG0001",
        title: "Multiple operations cannot have the same name within a service class",
        messageFormat: "Multiple operations named {0} in class {1}",
        category: "ServiceGenerator",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);
}

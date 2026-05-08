// Copyright (c) ZeroC, Inc.

using Microsoft.CodeAnalysis;

namespace IceRpc.ServiceGenerator.Internal;

internal static class DiagnosticDescriptors
{
    internal static DiagnosticDescriptor DuplicateOperationNames { get; } =
        new DiagnosticDescriptor(
            id: "IRSG0001", // cspell:disable-line
            title: "Multiple operations cannot have the same name within a service class",
            messageFormat: "Multiple operations named {0} in class {1}",
            category: "ServiceGenerator",
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);

    internal static DiagnosticDescriptor InvalidServiceTypeShape { get; } =
        new DiagnosticDescriptor(
            id: "IRSG0002", // cspell:disable-line
            title: "The Service attribute can only be applied to a class or record class",
            messageFormat: "The Service attribute cannot be applied to {0} '{1}'",
            category: "ServiceGenerator",
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);
}

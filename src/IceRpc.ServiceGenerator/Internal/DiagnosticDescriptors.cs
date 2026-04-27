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

    internal static DiagnosticDescriptor InvalidRpcMethodAttributeShape { get; } =
        new DiagnosticDescriptor(
            id: "IRSG0002", // cspell:disable-line
            title: "Unexpected operation attribute shape",
            messageFormat: "The operation attribute on method '{0}' has an unexpected shape; this usually indicates a version mismatch between the service generator and the IceRPC assembly that defines the attribute",
            category: "ServiceGenerator",
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);

    internal static DiagnosticDescriptor InvalidRpcMethodSignature { get; } =
        new DiagnosticDescriptor(
            id: "IRSG0003", // cspell:disable-line
            title: "Unexpected operation method signature",
            messageFormat: "The signature of method '{0}' does not match the shape expected by the generated IceRPC dispatcher; {1}",
            category: "ServiceGenerator",
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);

    internal static DiagnosticDescriptor MissingGeneratedRequestClass { get; } =
        new DiagnosticDescriptor(
            id: "IRSG0004", // cspell:disable-line
            title: "Missing generator-produced Request class",
            messageFormat: "Interface '{0}' does not contain the generator-produced 'Request.Decode{1}Async' method expected for operation '{2}'; this usually indicates a version mismatch between the service generator and the Slice/Ice code generator",
            category: "ServiceGenerator",
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);
}

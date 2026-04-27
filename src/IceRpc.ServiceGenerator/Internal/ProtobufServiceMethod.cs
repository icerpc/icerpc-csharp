// Copyright (c) ZeroC, Inc.

using Microsoft.CodeAnalysis;
using System.Collections.Immutable;
using ZeroC.CodeBuilder;

namespace IceRpc.ServiceGenerator.Internal;

/// <summary>Implements <see cref="ServiceMethod" /> for the Protobuf IDL.</summary>
internal class ProtobufServiceMethod : ServiceMethod
{
    /// <inheritdoc />
    internal override string OperationName { get; }

    /// <inheritdoc />
    internal override IEnumerable<string> UsingDirectives => _usingDirectives;

    private static readonly string[] _usingDirectives = ["using IceRpc.Protobuf.RpcMethods;"];

    // The fully qualified input type name (in C#). For example: "VisitorCenter.GreetRequest".
    private readonly string _inputTypeName;

    // The fully qualified name of the mapped C# Service interface. For example:
    // "VisitorCenter.IGreeterService".
    private readonly string _interfaceName;

    // The kind of the RPC method: "Unary", "ClientStreaming", "ServerStreaming", or "BidiStreaming".
    private readonly string _methodKind;

    // The name of the mapped C# method on the Service interface. For example: "GreetAsync".
    private readonly string _methodName;

    internal static ProtobufServiceMethod? TryCreate(
        IMethodSymbol method,
        AttributeData attribute,
        INamedTypeSymbol? asyncEnumerableSymbol,
        INamedTypeSymbol? genericValueTaskSymbol,
        Action<Diagnostic> reportDiagnostic)
    {
        Location location = method.Locations.FirstOrDefault() ?? Location.None;

        ImmutableArray<TypedConstant> items = attribute.ConstructorArguments;
        if (items.Length != 1 || items[0].Value is not string operationName)
        {
            reportDiagnostic(Diagnostic.Create(
                DiagnosticDescriptors.InvalidRpcMethodAttributeShape,
                location,
                method.Name));
            return null;
        }

        // The generated dispatcher always invokes the user method as
        // service.OpAsync(input, features, cancellationToken), so the method must accept exactly three parameters.
        if (method.Parameters.Length != 3)
        {
            reportDiagnostic(Diagnostic.Create(
                DiagnosticDescriptors.InvalidRpcMethodSignature,
                location,
                method.Name,
                $"expected 3 parameters (input, features, cancellationToken), found {method.Parameters.Length}"));
            return null;
        }

        ITypeSymbol inputType = method.Parameters[0].Type;
        // An IAsyncEnumerable input parameter denotes a client streaming RPC.
        bool isClientStreaming;
        string inputTypeName;
        if (SymbolEqualityComparer.Default.Equals(inputType.OriginalDefinition, asyncEnumerableSymbol))
        {
            isClientStreaming = true;
            if (inputType is not INamedTypeSymbol genericType || genericType.TypeArguments.Length != 1)
            {
                reportDiagnostic(Diagnostic.Create(
                    DiagnosticDescriptors.InvalidRpcMethodSignature,
                    location,
                    method.Name,
                    "IAsyncEnumerable parameter must have exactly one type argument"));
                return null;
            }
            inputTypeName = genericType.TypeArguments[0].GetFullName();
        }
        else
        {
            isClientStreaming = false;
            inputTypeName = inputType.GetFullName();
        }

        // Methods with the RpcMethodAttribute always have a generic ValueTask<T> return type.
        // For server-streaming, the return type's generic argument is IAsyncEnumerable.
        if (method.ReturnType is not INamedTypeSymbol genericReturnType ||
            !SymbolEqualityComparer.Default.Equals(
                genericReturnType.OriginalDefinition,
                genericValueTaskSymbol))
        {
            reportDiagnostic(Diagnostic.Create(
                DiagnosticDescriptors.InvalidRpcMethodSignature,
                location,
                method.Name,
                "return type must be System.Threading.Tasks.ValueTask<T>"));
            return null;
        }
        bool isServerStreaming = SymbolEqualityComparer.Default.Equals(
            genericReturnType.TypeArguments[0].OriginalDefinition,
            asyncEnumerableSymbol);

        string methodKind = (isClientStreaming, isServerStreaming) switch
        {
            (false, false) => "Unary",
            (true, false) => "ClientStreaming",
            (false, true) => "ServerStreaming",
            (true, true) => "BidiStreaming",
        };

        return new ProtobufServiceMethod(
            operationName,
            method.ContainingType.GetFullName(),
            method.Name,
            inputTypeName,
            methodKind);
    }

    /// <inheritdoc />
    internal override CodeBlock GenerateDispatchCaseBody() =>
        $@"return request.Dispatch{_methodKind}Async(
    global::{_inputTypeName}.Parser,
    (global::{_interfaceName})this,
    static (service, input, features, cancellationToken) => service.{_methodName}(input, features, cancellationToken),
    cancellationToken);";

    private ProtobufServiceMethod(
        string operationName,
        string interfaceName,
        string methodName,
        string inputTypeName,
        string methodKind)
    {
        OperationName = operationName;
        _interfaceName = interfaceName;
        _methodName = methodName;
        _inputTypeName = inputTypeName;
        _methodKind = methodKind;
    }
}

internal class ProtobufServiceMethodFactory : ServiceMethodFactory
{
    private readonly INamedTypeSymbol? _asyncEnumerableSymbol;
    private readonly INamedTypeSymbol? _genericValueTaskSymbol;

    internal ProtobufServiceMethodFactory(Compilation compilation)
        : base(compilation.GetTypeByMetadataName("IceRpc.Protobuf.RpcMethods.RpcMethodAttribute"))
    {
        _asyncEnumerableSymbol = compilation.GetTypeByMetadataName("System.Collections.Generic.IAsyncEnumerable`1");
        _genericValueTaskSymbol = compilation.GetTypeByMetadataName("System.Threading.Tasks.ValueTask`1");
    }

    private protected override ServiceMethod? CreateServiceMethod(
        IMethodSymbol methodSymbol,
        AttributeData attribute,
        Action<Diagnostic> reportDiagnostic) =>
        ProtobufServiceMethod.TryCreate(
            methodSymbol,
            attribute,
            _asyncEnumerableSymbol,
            _genericValueTaskSymbol,
            reportDiagnostic);
}

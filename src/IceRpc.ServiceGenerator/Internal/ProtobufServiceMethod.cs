// Copyright (c) ZeroC, Inc.

using Microsoft.CodeAnalysis;
using System.Collections.Immutable;
using System.Diagnostics;
using ZeroC.CodeBuilder;

namespace IceRpc.ServiceGenerator.Internal;

/// <summary>Implements <see cref="ServiceMethod" /> for the Slice IDL.</summary>
internal class ProtobufServiceMethod : ServiceMethod
{
    /// <inheritdoc />
    internal override string OperationName { get; }

    /// <inheritdoc />
    internal override IEnumerable<string> UsingDirectives => _usingDirectives;

    private static readonly string[] _usingDirectives = ["using IceRpc.Protobuf;"];

    // The fully qualified input type name (in C#). For example: "VisitorCenter.GreetRequest".
    private readonly string _inputTypeName;

    // The fully qualified name of the mapped C# Service interface. For example:
    // "VisitorCenter.IGreeterService".
    private readonly string _interfaceName;

    // The kind of the RPC method: "Unary", "ClientStreaming", "ServerStreaming", or "BidiStreaming".
    private readonly string _methodKind;

    // The name of the mapped C# method on the Service interface. For example: "GreetAsync".
    private readonly string _methodName;

    /// <inheritdoc />
    internal override CodeBlock GenerateDispatchCaseBody() =>
        $@"return request.Dispatch{_methodKind}Async(
    global::{_inputTypeName}.Parser,
    (global::{_interfaceName})this,
    static (service, input, features, cancellationToken) => service.{_methodName}(input, features, cancellationToken),
    cancellationToken);";

    internal ProtobufServiceMethod(
        IMethodSymbol method,
        AttributeData attribute,
        INamedTypeSymbol? asyncEnumerableSymbol)
    {
        ImmutableArray<TypedConstant> items = attribute.ConstructorArguments;
        Debug.Assert(
            items.Length == 1,
            "Unexpected number of arguments in attribute constructor.");
        OperationName = (string)items[0].Value!;

        _interfaceName = method.ContainingType.GetFullName();
        _methodName = method.Name;

        ITypeSymbol inputType = method.Parameters[0].Type;
        // An IAsyncEnumerable input parameter denotes a client streaming RPC.
        bool isClientStreaming;
        if (SymbolEqualityComparer.Default.Equals(inputType.OriginalDefinition, asyncEnumerableSymbol))
        {
            isClientStreaming = true;
            var genericType = (INamedTypeSymbol)inputType;
            Debug.Assert(genericType.TypeArguments.Length == 1);
            _inputTypeName = genericType.TypeArguments[0].GetFullName();
        }
        else
        {
            isClientStreaming = false;
            _inputTypeName = inputType.GetFullName();
        }

        // Methods with the ProtobufServiceMethodAttribute always have a generic ValueTask return type.
        // For server-streaming, the return type's generic argument is IAsyncEnumerable.
        Debug.Assert(method.ReturnType is INamedTypeSymbol);
        var genericReturnType = (INamedTypeSymbol)method.ReturnType;
        Debug.Assert(genericReturnType.TypeArguments.Length == 1);
        bool isServerStreaming = SymbolEqualityComparer.Default.Equals(
            genericReturnType.TypeArguments[0].OriginalDefinition,
            asyncEnumerableSymbol);

        _methodKind = (isClientStreaming, isServerStreaming) switch
        {
            (false, false) => "Unary",
            (true, false) => "ClientStreaming",
            (false, true) => "ServerStreaming",
            (true, true) => "BidiStreaming",
        };
    }
}

internal class ProtobufServiceMethodFactory : ServiceMethodFactory
{
    private readonly INamedTypeSymbol? _asyncEnumerableSymbol;

    internal ProtobufServiceMethodFactory(Compilation compilation)
        : base(compilation.GetTypeByMetadataName("IceRpc.Protobuf.ProtobufServiceMethodAttribute")) =>
        _asyncEnumerableSymbol = compilation.GetTypeByMetadataName("System.Collections.Generic.IAsyncEnumerable`1");

    private protected override ServiceMethod CreateServiceMethod(IMethodSymbol methodSymbol, AttributeData attribute) =>
        new ProtobufServiceMethod(methodSymbol, attribute, _asyncEnumerableSymbol);
}

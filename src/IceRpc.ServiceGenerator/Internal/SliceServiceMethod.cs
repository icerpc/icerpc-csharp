// Copyright (c) ZeroC, Inc.

using Microsoft.CodeAnalysis;
using System.Collections.Immutable;
using System.Diagnostics;
using ZeroC.CodeBuilder;

namespace IceRpc.ServiceGenerator.Internal;

/// <summary>Implements <see cref="ServiceMethod" /> for the Slice IDL.</summary>
internal class SliceServiceMethod : ServiceMethod
{
    /// <inheritdoc />
    internal override string OperationName { get; }

    /// <inheritdoc />
    internal override IEnumerable<string> UsingDirectives => _usingDirectives;

    private static readonly string[] _usingDirectives =
    [
        "using IceRpc.Slice.Operations;",
        "using ZeroC.Slice.Codec;",
    ];

    /// <summary>A value indicating whether the return value should be compressed.</summary>
    private readonly bool _compressReturn;

    /// <summary>The name of the C# method minus the Async suffix. For example: "FindObjectById".</summary>
    private readonly string _dispatchMethodName;

    /// <summary>A value indicating whether the non-stream portion of the return value is pre-encoded by the
    /// application.</summary>
    private readonly bool _encodedReturn;

    /// <summary>The name of the C# service interface, including its namespace. For example:
    /// "VisitorCenter.IGreeterService".</summary>
    private readonly string _fullInterfaceName;

    /// <summary>A value indicating whether the operation is idempotent.</summary>
    private readonly bool _idempotent;

    /// <summary>The arity of the operation.</summary>
    private readonly int _parameterCount;

    /// <summary>The capitalized names of the operation parameters. This is empty when <see cref="_parameterCount"/>
    /// is 0 or 1.</summary>
    private readonly string[] _parameterFieldNames = [];

    /// <summary>The number of elements in the return value.</summary>
    private readonly int _returnCount;

    /// <summary>The capitalized names of the operation return value fields. This is empty when
    /// <see cref="_returnCount"/> is 0 or 1.</summary>
    private readonly string[] _returnFieldNames = [];

    /// <summary>A value indicating whether the operation return value has a stream element.</summary>
    private readonly bool _returnStream;

    /// <inheritdoc />
    internal override CodeBlock GenerateDispatchCaseBody()
    {
        var codeBlock = new CodeBlock();
        if (!_idempotent)
        {
            codeBlock.AddBlock(@"if (request.Fields.ContainsKey(IceRpc.RequestFieldKey.Idempotent))
{
    throw new IceRpc.DispatchException(
        IceRpc.StatusCode.InvalidData,
        $""Invocation mode mismatch for operation '{request.Operation}': received idempotent field for an operation not marked as idempotent."");
}");
        }

        if (_compressReturn)
        {
            FunctionCallBuilder withCallBuilder =
                new FunctionCallBuilder("IceRpc.Features.FeatureCollectionExtensions.With")
                .ArgumentsOnNewLine(true)
                .AddArgument("request.Features")
                .AddArgument("IceRpc.Features.CompressFeature.Compress");

            codeBlock.WriteLine($"request.Features = {withCallBuilder.Build()}");
        }

        string thisInterface = $"((global::{_fullInterfaceName})this)";

        string method;
        if (_parameterCount <= 1)
        {
            method = $"{thisInterface}.{_dispatchMethodName}Async";
        }
        else
        {
            var methodCallBuilder = new FunctionCallBuilder($"{thisInterface}.{_dispatchMethodName}Async")
                .UseSemicolon(false);

            methodCallBuilder.AddArguments(_parameterFieldNames.Select(name => $"args.{name}"))
                .AddArgument("features")
                .AddArgument("cancellationToken");

            method = @$"(args, features, cancellationToken) =>
        {methodCallBuilder.Build()}";
        }

        FunctionCallBuilder dispatchCallBuilder = new FunctionCallBuilder("request.DispatchOperationAsync")
            .ArgumentsOnNewLine(true)
            .AddArgument(
                $"decodeArgs: global::{_fullInterfaceName}.Request.Decode{_dispatchMethodName}Async")
            .AddArgument($"method: {method}");

        // We don't use the generated Response.EncodeXxx method when _returnCount is 0. So we could not generate it.

        if (_returnCount == 1)
        {
            if (_returnStream)
            {
                dispatchCallBuilder.AddArgument(
                    @$"encodeReturnValue: static (_, encodeOptions) =>
        global::{_fullInterfaceName}.Response.Encode{_dispatchMethodName}(encodeOptions)");

                dispatchCallBuilder.AddArgument(
                    $"encodeReturnValueStream: global::{_fullInterfaceName}.Response.EncodeStreamOf{_dispatchMethodName}");
            }
            else if (_encodedReturn)
            {
                dispatchCallBuilder.AddArgument("encodeReturnValue: (returnValue, _) => returnValue");
            }
            else
            {
                dispatchCallBuilder.AddArgument(
                    $"encodeReturnValue: global::{_fullInterfaceName}.Response.Encode{_dispatchMethodName}");
            }
        }
        else if (_returnCount > 1)
        {
            // Splatting required.
            var nonStreamReturnNames = new List<string>(_returnFieldNames);
            if (_returnStream)
            {
                nonStreamReturnNames.RemoveAt(_returnFieldNames.Length - 1);
            }

            if (_encodedReturn)
            {
                dispatchCallBuilder.AddArgument(
                    $"encodeReturnValue: (returnValue, _) => returnValue.{nonStreamReturnNames[0]}");
            }
            else
            {
                var encodeBuilder = new FunctionCallBuilder(
                    $"global::{_fullInterfaceName}.Response.Encode{_dispatchMethodName}")
                        .UseSemicolon(false)
                        .AddArguments(nonStreamReturnNames.Select(name => $"returnValue.{name}"))
                        .AddArgument("encodeOptions");

                dispatchCallBuilder.AddArgument(
                    @$"encodeReturnValue: static (returnValue, encodeOptions) =>
        {encodeBuilder.Build()}");
            }

            if (_returnStream)
            {
                string streamFieldName =
                    _returnFieldNames[_returnFieldNames.Length - 1];

                var encodeBuilder = new FunctionCallBuilder(
                    $"global::{_fullInterfaceName}.Response.EncodeStreamOf{_dispatchMethodName}")
                        .UseSemicolon(false)
                        .AddArgument($"returnValue.{streamFieldName}")
                        .AddArgument("encodeOptions");

                dispatchCallBuilder.AddArgument(
                    $"encodeReturnValueStream: static (returnValue, encodeOptions) => {encodeBuilder.Build()}");
            }
        }

        dispatchCallBuilder.AddArgument("cancellationToken: cancellationToken");

        codeBlock.WriteLine($"return {dispatchCallBuilder.Build()}");
        return codeBlock;
    }

    internal SliceServiceMethod(
        IMethodSymbol method,
        AttributeData attribute,
        INamedTypeSymbol? asyncEnumerableSymbol,
        INamedTypeSymbol? pipeReaderSymbol)
    {
        ImmutableArray<TypedConstant> items = attribute.ConstructorArguments;
        Debug.Assert(
            items.Length == 1,
            "Unexpected number of arguments in attribute constructor.");

        INamedTypeSymbol interfaceSymbol = method.ContainingType!;

        OperationName = (string)items[0].Value!;
        _dispatchMethodName = method.Name.Substring(0, method.Name.Length - "Async".Length);
        _fullInterfaceName = interfaceSymbol.GetFullName();

        foreach (KeyValuePair<string, TypedConstant> namedArgument in attribute.NamedArguments)
        {
            switch (namedArgument.Key)
            {
                case "CompressReturn":
                    if (namedArgument.Value.Value is bool compressReturn)
                    {
                        _compressReturn = compressReturn;
                    }
                    break;
                case "EncodedReturn":
                    if (namedArgument.Value.Value is bool encodedReturn)
                    {
                        _encodedReturn = encodedReturn;
                    }
                    break;
                case "Idempotent":
                    if (namedArgument.Value.Value is bool idempotent)
                    {
                        _idempotent = idempotent;
                    }
                    break;
            }
        }

        // Find the nested Request class within the interface
        INamedTypeSymbol? requestClass = interfaceSymbol
            .GetTypeMembers("Request")
            .FirstOrDefault();

        IMethodSymbol? decodeArgsMethod = requestClass?
            .GetMembers()
            .OfType<IMethodSymbol>()
            .FirstOrDefault(m => m.Name == $"Decode{_dispatchMethodName}Async");

        Debug.Assert(
            decodeArgsMethod is not null,
            $"Cannot find decode method for operation {OperationName} in interface {interfaceSymbol.Name}.");

        // Analyze the return type of the decode method (ValueTask or ValueTask<T>)

        if (decodeArgsMethod!.ReturnType is INamedTypeSymbol returnType &&
            returnType.IsGenericType &&
            returnType.TypeArguments.Length == 1)
        {
            // It's ValueTask<T>, check what T is
            ITypeSymbol typeArgument = returnType.TypeArguments[0];

            if (typeArgument.IsTupleType && typeArgument is INamedTypeSymbol tupleType)
            {
                // It's a tuple - get the count and field names
                ImmutableArray<IFieldSymbol> tupleElements = tupleType.TupleElements;
                _parameterCount = tupleElements.Length;
                _parameterFieldNames = tupleElements.Select(e => e.Name).ToArray();
            }
            else
            {
                // It's a simple type (int, string, etc.)
                _parameterCount = 1;
            }
        }
        // else: It's ValueTask (non-generic), _parameterCount stays 0

        if (method.ReturnType is INamedTypeSymbol methodReturnType &&
            methodReturnType.IsGenericType &&
            methodReturnType.TypeArguments.Length == 1)
        {
            ITypeSymbol methodReturnTypeArg = methodReturnType.TypeArguments[0];
            ITypeSymbol lastFieldType;

            if (methodReturnTypeArg.IsTupleType && methodReturnTypeArg is INamedTypeSymbol methodTupleType)
            {
                ImmutableArray<IFieldSymbol> returnElements = methodTupleType.TupleElements;
                _returnCount = returnElements.Length;
                _returnFieldNames = returnElements.Select(e => e.Name).ToArray();

                lastFieldType = returnElements[returnElements.Length - 1].Type;
            }
            else
            {
                // It's a simple return type
                _returnCount = 1;
                lastFieldType = methodReturnTypeArg;
            }

            if (SymbolEqualityComparer.Default.Equals(lastFieldType.OriginalDefinition, asyncEnumerableSymbol))
            {
                _returnStream = true;
            }
            else if (SymbolEqualityComparer.Default.Equals(lastFieldType.OriginalDefinition, pipeReaderSymbol))
            {
                _returnStream = !_encodedReturn || _returnCount > 1;
                // else, the single parameter is the encoded return, and there is no stream
            }
        }
        // else: It's ValueTask (non-generic), _returnCount remains 0 and _returnStream remains false.
    }
}

internal class SliceServiceMethodFactory : ServiceMethodFactory
{
    private readonly INamedTypeSymbol? _asyncEnumerableSymbol;
    private readonly INamedTypeSymbol? _pipeReaderSymbol;

    internal SliceServiceMethodFactory(Compilation compilation)
        : base(compilation.GetTypeByMetadataName("IceRpc.Slice.Operations.SliceOperationAttribute"))
    {
        _asyncEnumerableSymbol = compilation.GetTypeByMetadataName("System.Collections.Generic.IAsyncEnumerable`1");
        _pipeReaderSymbol = compilation.GetTypeByMetadataName("System.IO.Pipelines.PipeReader");
    }

    private protected override ServiceMethod CreateServiceMethod(IMethodSymbol methodSymbol, AttributeData attribute) =>
        new SliceServiceMethod(methodSymbol, attribute, _asyncEnumerableSymbol, _pipeReaderSymbol);
}

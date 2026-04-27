// Copyright (c) ZeroC, Inc.

using Microsoft.CodeAnalysis;
using System.Collections.Immutable;
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
    private readonly string[] _parameterFieldNames;

    /// <summary>The number of elements in the return value.</summary>
    private readonly int _returnCount;

    /// <summary>The capitalized names of the operation return value fields. This is empty when
    /// <see cref="_returnCount"/> is 0 or 1.</summary>
    private readonly string[] _returnFieldNames;

    /// <summary>A value indicating whether the operation return value has a stream element.</summary>
    private readonly bool _returnStream;

    internal static SliceServiceMethod? TryCreate(
        IMethodSymbol method,
        AttributeData attribute,
        INamedTypeSymbol? asyncEnumerableSymbol,
        INamedTypeSymbol? pipeReaderSymbol,
        INamedTypeSymbol? valueTaskSymbol,
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

        if (!method.Name.EndsWith("Async", StringComparison.Ordinal))
        {
            reportDiagnostic(Diagnostic.Create(
                DiagnosticDescriptors.InvalidRpcMethodSignature,
                location,
                method.Name,
                "method name must end with 'Async'"));
            return null;
        }

        INamedTypeSymbol interfaceSymbol = method.ContainingType;
        string dispatchMethodName = method.Name.Substring(0, method.Name.Length - "Async".Length);

        bool compressReturn = false;
        bool encodedReturn = false;
        bool idempotent = false;
        foreach (KeyValuePair<string, TypedConstant> namedArgument in attribute.NamedArguments)
        {
            switch (namedArgument.Key)
            {
                case "CompressReturn":
                    if (namedArgument.Value.Value is bool compressReturnValue)
                    {
                        compressReturn = compressReturnValue;
                    }
                    break;
                case "EncodedReturn":
                    if (namedArgument.Value.Value is bool encodedReturnValue)
                    {
                        encodedReturn = encodedReturnValue;
                    }
                    break;
                case "Idempotent":
                    if (namedArgument.Value.Value is bool idempotentValue)
                    {
                        idempotent = idempotentValue;
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
            .FirstOrDefault(m => m.Name == $"Decode{dispatchMethodName}Async");

        if (decodeArgsMethod is null)
        {
            reportDiagnostic(Diagnostic.Create(
                DiagnosticDescriptors.MissingGeneratedRequestClass,
                location,
                interfaceSymbol.GetFullName(),
                dispatchMethodName,
                operationName));
            return null;
        }

        // The decoder is generated by the Slice IDL generator, so it must return ValueTask or ValueTask<T>; if it
        // doesn't, the IDL-side generator and the service generator are out of sync.
        if (decodeArgsMethod.ReturnType is not INamedTypeSymbol returnType ||
            !returnType.IsValueTask(valueTaskSymbol, genericValueTaskSymbol))
        {
            reportDiagnostic(Diagnostic.Create(
                DiagnosticDescriptors.MissingGeneratedRequestClass,
                location,
                interfaceSymbol.GetFullName(),
                dispatchMethodName,
                operationName));
            return null;
        }

        // Analyze the return type of the decode method (ValueTask or ValueTask<T>)
        int parameterCount = 0;
        string[] parameterFieldNames = [];
        if (returnType.IsGenericType)
        {
            // It's ValueTask<T>, check what T is
            ITypeSymbol typeArgument = returnType.TypeArguments[0];

            if (typeArgument.IsTupleType && typeArgument is INamedTypeSymbol tupleType)
            {
                // It's a tuple - get the count and field names
                ImmutableArray<IFieldSymbol> tupleElements = tupleType.TupleElements;
                parameterCount = tupleElements.Length;
                parameterFieldNames = tupleElements.Select(e => e.Name).ToArray();
            }
            else
            {
                // It's a simple type (int, string, etc.)
                parameterCount = 1;
            }
        }
        // else: It's ValueTask (non-generic), parameterCount stays 0

        // The user method must return ValueTask or ValueTask<T>; the dispatcher uses await to invoke it.
        if (method.ReturnType is not INamedTypeSymbol methodReturnType ||
            !methodReturnType.IsValueTask(valueTaskSymbol, genericValueTaskSymbol))
        {
            reportDiagnostic(Diagnostic.Create(
                DiagnosticDescriptors.InvalidRpcMethodSignature,
                location,
                method.Name,
                "return type must be System.Threading.Tasks.ValueTask or System.Threading.Tasks.ValueTask<T>"));
            return null;
        }

        // Expected user-method shape: <args...> + features + cancellationToken (parameterCount + 2 parameters).
        int expectedParameterCount = parameterCount + 2;
        if (method.Parameters.Length != expectedParameterCount)
        {
            reportDiagnostic(Diagnostic.Create(
                DiagnosticDescriptors.InvalidRpcMethodSignature,
                location,
                method.Name,
                $"expected {expectedParameterCount} parameters (decoded args + features + cancellationToken), found {method.Parameters.Length}"));
            return null;
        }

        int returnCount = 0;
        string[] returnFieldNames = [];
        bool returnStream = false;
        if (methodReturnType.IsGenericType)
        {
            ITypeSymbol methodReturnTypeArg = methodReturnType.TypeArguments[0];
            ITypeSymbol lastFieldType;

            if (methodReturnTypeArg.IsTupleType && methodReturnTypeArg is INamedTypeSymbol methodTupleType)
            {
                ImmutableArray<IFieldSymbol> returnElements = methodTupleType.TupleElements;
                returnCount = returnElements.Length;
                returnFieldNames = returnElements.Select(e => e.Name).ToArray();

                lastFieldType = returnElements[returnElements.Length - 1].Type;
            }
            else
            {
                // It's a simple return type
                returnCount = 1;
                lastFieldType = methodReturnTypeArg;
            }

            if (SymbolEqualityComparer.Default.Equals(lastFieldType.OriginalDefinition, asyncEnumerableSymbol))
            {
                returnStream = true;
            }
            else if (SymbolEqualityComparer.Default.Equals(lastFieldType.OriginalDefinition, pipeReaderSymbol))
            {
                returnStream = !encodedReturn || returnCount > 1;
                // else, the single parameter is the encoded return, and there is no stream
            }
        }
        // else: It's ValueTask (non-generic), returnCount remains 0 and returnStream remains false.

        return new SliceServiceMethod(
            operationName,
            dispatchMethodName,
            interfaceSymbol.GetFullName(),
            compressReturn,
            encodedReturn,
            idempotent,
            parameterCount,
            parameterFieldNames,
            returnCount,
            returnFieldNames,
            returnStream);
    }

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

    private SliceServiceMethod(
        string operationName,
        string dispatchMethodName,
        string fullInterfaceName,
        bool compressReturn,
        bool encodedReturn,
        bool idempotent,
        int parameterCount,
        string[] parameterFieldNames,
        int returnCount,
        string[] returnFieldNames,
        bool returnStream)
    {
        OperationName = operationName;
        _dispatchMethodName = dispatchMethodName;
        _fullInterfaceName = fullInterfaceName;
        _compressReturn = compressReturn;
        _encodedReturn = encodedReturn;
        _idempotent = idempotent;
        _parameterCount = parameterCount;
        _parameterFieldNames = parameterFieldNames;
        _returnCount = returnCount;
        _returnFieldNames = returnFieldNames;
        _returnStream = returnStream;
    }
}

internal class SliceServiceMethodFactory : ServiceMethodFactory
{
    private readonly INamedTypeSymbol? _asyncEnumerableSymbol;
    private readonly INamedTypeSymbol? _pipeReaderSymbol;
    private readonly INamedTypeSymbol? _valueTaskSymbol;
    private readonly INamedTypeSymbol? _genericValueTaskSymbol;

    internal SliceServiceMethodFactory(Compilation compilation)
        : base(compilation.GetTypeByMetadataName("IceRpc.Slice.Operations.SliceOperationAttribute"))
    {
        _asyncEnumerableSymbol = compilation.GetTypeByMetadataName("System.Collections.Generic.IAsyncEnumerable`1");
        _pipeReaderSymbol = compilation.GetTypeByMetadataName("System.IO.Pipelines.PipeReader");
        _valueTaskSymbol = compilation.GetTypeByMetadataName("System.Threading.Tasks.ValueTask");
        _genericValueTaskSymbol = compilation.GetTypeByMetadataName("System.Threading.Tasks.ValueTask`1");
    }

    private protected override ServiceMethod? CreateServiceMethod(
        IMethodSymbol methodSymbol,
        AttributeData attribute,
        Action<Diagnostic> reportDiagnostic) =>
        SliceServiceMethod.TryCreate(
            methodSymbol,
            attribute,
            _asyncEnumerableSymbol,
            _pipeReaderSymbol,
            _valueTaskSymbol,
            _genericValueTaskSymbol,
            reportDiagnostic);
}

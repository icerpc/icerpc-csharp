// Copyright (c) ZeroC, Inc.

using Microsoft.CodeAnalysis;
using System.Collections.Immutable;
using ZeroC.CodeBuilder;

namespace IceRpc.ServiceGenerator.Internal;

/// <summary>Implements <see cref="ServiceMethod" /> for the Ice IDL.</summary>
internal class IceServiceMethod : ServiceMethod
{
    /// <inheritdoc />
    internal override string OperationName { get; }

    /// <inheritdoc />
    internal override IEnumerable<string> UsingDirectives => _usingDirectives;

    private static readonly string[] _usingDirectives =
    [
        "using IceRpc.Ice.Operations;",
        "using ZeroC.Slice.Codec;",
    ];

    /// <summary>The name of the C# method minus the Async suffix. For example: "FindObjectById".</summary>
    private readonly string _dispatchMethodName;

    /// <summary>The exception specification of the operation.</summary>
    private readonly string[] _exceptionSpecification;

    /// <summary>The name of the C# service interface, including its namespace. For example:
    /// "VisitorCenter.IGreeterService".</summary>
    private readonly string _fullInterfaceName;

    /// <summary>A value indicating whether the operation is idempotent.</summary>
    private readonly bool _idempotent;

    /// <summary>A value indicating whether the return value is pre-encoded by the application.</summary>
    private readonly bool _marshaledResult;

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

    internal static IceServiceMethod? TryCreate(
        IMethodSymbol method,
        AttributeData attribute,
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

        string[] exceptionSpecification = [];
        bool idempotent = false;
        foreach (KeyValuePair<string, TypedConstant> namedArgument in attribute.NamedArguments)
        {
            switch (namedArgument.Key)
            {
                case "ExceptionSpecification":
                    if (namedArgument.Value.Values is ImmutableArray<TypedConstant> exceptionTypes)
                    {
                        exceptionSpecification = exceptionTypes
                            .Select(et => et.Value)
                            .OfType<INamedTypeSymbol>()
                            .Select(et => et.GetFullName())
                            .ToArray();
                    }
                    break;
                case "Idempotent":
                    if (namedArgument.Value.Value is bool idempotentValue)
                    {
                        idempotent = idempotentValue;
                    }
                    break;
                // TODO: issue a diagnostic if we encounter an unknown named argument
                // An unknown argument indicates a mismatch between this version of the generator and the version
                // of the IceRpc.Ice assembly that references this generator, which is highly unlikely.
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

        // The decoder is generated by the Slice/Ice IDL generator, so it must return ValueTask or ValueTask<T>; if it
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

        // Analyze the return type of the Decode method (ValueTask or ValueTask<T>) for the parameters.
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
        bool marshaledResult = false;
        if (methodReturnType.IsGenericType)
        {
            ITypeSymbol methodReturnTypeArg = methodReturnType.TypeArguments[0];

            if (methodReturnTypeArg.IsTupleType && methodReturnTypeArg is INamedTypeSymbol methodTupleType)
            {
                ImmutableArray<IFieldSymbol> returnElements = methodTupleType.TupleElements;
                returnCount = returnElements.Length;
                returnFieldNames = returnElements.Select(e => e.Name).ToArray();
            }
            else if (pipeReaderSymbol is not null &&
                SymbolEqualityComparer.Default.Equals(methodReturnTypeArg.OriginalDefinition, pipeReaderSymbol))
            {
                returnCount = 1;
                marshaledResult = true;
            }
            else
            {
                // It's a simple return type
                returnCount = 1;
            }
        }
        // else it's a ValueTask and returnCount stays 0

        return new IceServiceMethod(
            operationName,
            dispatchMethodName,
            interfaceSymbol.GetFullName(),
            idempotent,
            marshaledResult,
            parameterCount,
            parameterFieldNames,
            returnCount,
            returnFieldNames,
            exceptionSpecification);
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
        if (_returnCount > 0)
        {
            if (_marshaledResult)
            {
                dispatchCallBuilder.AddArgument("encodeReturnValue: (returnValue, _) => returnValue");
            }
            else if (_returnCount == 1)
            {
                dispatchCallBuilder.AddArgument(
                    $"encodeReturnValue: global::{_fullInterfaceName}.Response.Encode{_dispatchMethodName}");
            }
            else
            {
                // Splatting required.
                var encodeBuilder = new FunctionCallBuilder(
                    $"global::{_fullInterfaceName}.Response.Encode{_dispatchMethodName}")
                        .UseSemicolon(false)
                        .AddArguments(_returnFieldNames.Select(name => $"returnValue.{name}"))
                        .AddArgument("encodeOptions");

                dispatchCallBuilder.AddArgument(
                    @$"encodeReturnValue: static (returnValue, encodeOptions) =>
        {encodeBuilder.Build()}");
            }
        }

        if (_exceptionSpecification.Length > 0)
        {
            string exceptionList = string.Join(" or ", _exceptionSpecification.Select(ex => $"global::{ex}"));

            dispatchCallBuilder.AddArgument(
                $"inExceptionSpecification: iceException => iceException is {exceptionList}");
        }

        dispatchCallBuilder.AddArgument("cancellationToken: cancellationToken");

        codeBlock.WriteLine($"return {dispatchCallBuilder.Build()}");
        return codeBlock;
    }

    private IceServiceMethod(
        string operationName,
        string dispatchMethodName,
        string fullInterfaceName,
        bool idempotent,
        bool marshaledResult,
        int parameterCount,
        string[] parameterFieldNames,
        int returnCount,
        string[] returnFieldNames,
        string[] exceptionSpecification)
    {
        OperationName = operationName;
        _dispatchMethodName = dispatchMethodName;
        _fullInterfaceName = fullInterfaceName;
        _idempotent = idempotent;
        _marshaledResult = marshaledResult;
        _parameterCount = parameterCount;
        _parameterFieldNames = parameterFieldNames;
        _returnCount = returnCount;
        _returnFieldNames = returnFieldNames;
        _exceptionSpecification = exceptionSpecification;
    }
}

internal class IceServiceMethodFactory : ServiceMethodFactory
{
    private readonly INamedTypeSymbol? _pipeReaderSymbol;
    private readonly INamedTypeSymbol? _valueTaskSymbol;
    private readonly INamedTypeSymbol? _genericValueTaskSymbol;

    internal IceServiceMethodFactory(Compilation compilation)
        : base(compilation.GetTypeByMetadataName("IceRpc.Ice.Operations.IceOperationAttribute"))
    {
        _pipeReaderSymbol = compilation.GetTypeByMetadataName("System.IO.Pipelines.PipeReader");
        _valueTaskSymbol = compilation.GetTypeByMetadataName("System.Threading.Tasks.ValueTask");
        _genericValueTaskSymbol = compilation.GetTypeByMetadataName("System.Threading.Tasks.ValueTask`1");
    }

    private protected override ServiceMethod? CreateServiceMethod(
        IMethodSymbol methodSymbol,
        AttributeData attribute,
        Action<Diagnostic> reportDiagnostic) =>
        IceServiceMethod.TryCreate(
            methodSymbol,
            attribute,
            _pipeReaderSymbol,
            _valueTaskSymbol,
            _genericValueTaskSymbol,
            reportDiagnostic);
}

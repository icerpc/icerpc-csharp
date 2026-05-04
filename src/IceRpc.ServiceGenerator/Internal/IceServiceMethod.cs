// Copyright (c) ZeroC, Inc.

using Microsoft.CodeAnalysis;
using System.Collections.Immutable;
using System.Diagnostics;
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
    private readonly string[] _exceptionSpecification = [];

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
    private readonly string[] _parameterFieldNames = [];

    /// <summary>The number of elements in the return value.</summary>
    private readonly int _returnCount;

    /// <summary>The capitalized names of the operation return value fields. This is empty when
    /// <see cref="_returnCount"/> is 0 or 1.</summary>
    private readonly string[] _returnFieldNames = [];

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

    internal IceServiceMethod(IMethodSymbol method, AttributeData attribute, INamedTypeSymbol? pipeReaderSymbol)
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
                case "ExceptionSpecification":
                    if (namedArgument.Value.Values is ImmutableArray<TypedConstant> exceptionTypes)
                    {
                        _exceptionSpecification = exceptionTypes
                            .Select(et => et.Value)
                            .OfType<INamedTypeSymbol>()
                            .Select(et => et.GetFullName())
                            .ToArray();
                    }
                    break;
                case "Idempotent":
                    if (namedArgument.Value.Value is bool idempotent)
                    {
                        _idempotent = idempotent;
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
            .FirstOrDefault(m => m.Name == $"Decode{_dispatchMethodName}Async");

        Debug.Assert(
            decodeArgsMethod is not null,
            $"Cannot find decode method for operation {OperationName} in interface {interfaceSymbol.Name}.");

        // Analyze the return type of the Decode method (ValueTask or ValueTask<T>) for the parameters.

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

            if (methodReturnTypeArg.IsTupleType && methodReturnTypeArg is INamedTypeSymbol methodTupleType)
            {
                ImmutableArray<IFieldSymbol> returnElements = methodTupleType.TupleElements;
                _returnCount = returnElements.Length;
                _returnFieldNames = returnElements.Select(e => e.Name).ToArray();
            }
            else if (pipeReaderSymbol is not null &&
                SymbolEqualityComparer.Default.Equals(methodReturnTypeArg.OriginalDefinition, pipeReaderSymbol))
            {
                _returnCount = 1;
                _marshaledResult = true;
            }
            else
            {
                // It's a simple return type
                _returnCount = 1;
            }
        }
        // else it's a ValueTask and _returnCount stays 0
    }
}

internal class IceServiceMethodFactory : ServiceMethodFactory
{
    private readonly INamedTypeSymbol? _pipeReaderSymbol;

    internal IceServiceMethodFactory(Compilation compilation)
        : base(compilation.GetTypeByMetadataName("IceRpc.Ice.Operations.IceOperationAttribute")) =>
        _pipeReaderSymbol = compilation.GetTypeByMetadataName("System.IO.Pipelines.PipeReader");

    private protected override ServiceMethod CreateServiceMethod(
        IMethodSymbol methodSymbol,
        AttributeData attribute) =>
        new IceServiceMethod(methodSymbol, attribute, _pipeReaderSymbol);
}

// Copyright (c) ZeroC, Inc.

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Security.Cryptography.X509Certificates;
using ZeroC.CodeBuilder;

namespace IceRpc.ServiceGenerator.Internal;

/// <summary>Implements <see cref="IServiceMethod" /> for the Ice IDL.</summary>
internal class IceServiceMethod : IServiceMethod
{
    /// <inheritdoc />
    public Idl Idl => Idl.Ice;

    /// <inheritdoc />
    public string OperationName { get; }

    private readonly string _dispatchMethodName;
    private readonly bool _encodedReturn;
    private readonly string[] _exceptionSpecification = [];
    private readonly string _fullInterfaceName;
    private readonly bool _idempotent;
    private readonly int _parameterCount;
    private readonly string[] _parameterFieldNames = [];
    private readonly int _returnCount;
    private readonly string[] _returnFieldNames = [];

    /// <inheritdoc />
    public CodeBlock GenerateDispatchCaseBody()
    {
        var codeBlock = new CodeBlock();
        if (!_idempotent)
        {
            codeBlock.WriteLine("request.CheckNonIdempotent();");
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
            if (_encodedReturn)
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

    internal IceServiceMethod(IMethodSymbol method, INamedTypeSymbol interfaceSymbol, AttributeData attribute)
    {
        ImmutableArray<TypedConstant> items = attribute.ConstructorArguments;
        Debug.Assert(
            items.Length == 1,
            "Unexpected number of arguments in attribute constructor.");

        OperationName = (string)items[0].Value!;
        _dispatchMethodName = method.Name.Substring(0, method.Name.Length - "Async".Length);
        _fullInterfaceName = Parser.GetFullName(interfaceSymbol);

        foreach (KeyValuePair<string, TypedConstant> namedArgument in attribute.NamedArguments)
        {
            switch (namedArgument.Key)
            {
                case "EncodedReturn":
                    if (namedArgument.Value.Value is bool encodedReturn)
                    {
                        _encodedReturn = encodedReturn;
                    }
                    break;
                case "ExceptionSpecification":
                    if (namedArgument.Value.Values is ImmutableArray<TypedConstant> exceptionTypes)
                    {
                        _exceptionSpecification = exceptionTypes
                            .Select(et => et.Value)
                            .OfType<INamedTypeSymbol>()
                            .Select(Parser.GetFullName)
                            .ToArray();
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

            if (methodReturnTypeArg.IsTupleType && methodReturnTypeArg is INamedTypeSymbol methodTupleType)
            {
                ImmutableArray<IFieldSymbol> returnElements = methodTupleType.TupleElements;
                _returnCount = returnElements.Length;
                _returnFieldNames = returnElements.Select(e => e.Name).ToArray();
            }
            else
            {
                // It's a simple return type
                _returnCount = 1;
            }
        }
    }
}

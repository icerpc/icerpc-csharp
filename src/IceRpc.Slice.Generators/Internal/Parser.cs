// Copyright (c) ZeroC, Inc.

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Collections.Immutable;
using System.Diagnostics;

namespace IceRpc.Slice.Generators.Internal;

internal sealed class Parser
{
    internal const string ServiceMethodAttribute = "IceRpc.Slice.SliceServiceMethodAttribute";
    internal const string ServiceAttribute = "IceRpc.Slice.SliceServiceAttribute";

    private readonly INamedTypeSymbol? _asyncEnumerableSymbol;
    private readonly CancellationToken _cancellationToken;
    private readonly Compilation _compilation;
    private readonly INamedTypeSymbol? _serviceMethodAttribute;
    private readonly INamedTypeSymbol? _pipeReaderSymbol;
    private readonly Action<Diagnostic> _reportDiagnostic;
    private readonly INamedTypeSymbol? _serviceAttribute;

    internal Parser(
        Compilation compilation,
        Action<Diagnostic> reportDiagnostic,
        CancellationToken cancellationToken)
    {
        _compilation = compilation;
        _reportDiagnostic = reportDiagnostic;
        _cancellationToken = cancellationToken;

        _asyncEnumerableSymbol = _compilation.GetTypeByMetadataName("System.Collections.Generic.IAsyncEnumerable`1");
        _serviceMethodAttribute = _compilation.GetTypeByMetadataName(ServiceMethodAttribute);
        _pipeReaderSymbol = _compilation.GetTypeByMetadataName("System.IO.Pipelines.PipeReader");
        _serviceAttribute = _compilation.GetTypeByMetadataName(ServiceAttribute);
    }

    internal IReadOnlyList<ServiceClass> GetServiceDefinitions(IEnumerable<ClassDeclarationSyntax> classes)
    {
        if (_serviceMethodAttribute is null || _serviceAttribute is null)
        {
            // nothing to do if these types aren't available
            return Array.Empty<ServiceClass>();
        }

        var serviceDefinitions = new List<ServiceClass>();
        // we enumerate by syntax tree, to minimize the need to instantiate semantic models (since they're expensive)
        foreach (IGrouping<SyntaxTree, ClassDeclarationSyntax> group in classes.GroupBy(x => x.SyntaxTree))
        {
            foreach (ClassDeclarationSyntax classDeclaration in group)
            {
                // stop if we're asked to
                _cancellationToken.ThrowIfCancellationRequested();

                SemanticModel semanticModel = _compilation.GetSemanticModel(classDeclaration.SyntaxTree);
                INamedTypeSymbol? classSymbol = semanticModel.GetDeclaredSymbol(classDeclaration, _cancellationToken);
                if (classSymbol is null)
                {
                    continue;
                }

                IReadOnlyList<ServiceMethod> baseServiceMethods = Array.Empty<ServiceMethod>();
                INamedTypeSymbol? baseServiceClass = GetBaseServiceClass(classSymbol);
                if (baseServiceClass is not null)
                {
                    baseServiceMethods = GetServiceMethods(baseServiceClass.AllInterfaces);
                }

                IReadOnlyList<ServiceMethod> serviceMethods = GetServiceMethods(classSymbol.AllInterfaces)
                    .Except(baseServiceMethods)
                    .Distinct()
                    .ToList();

                var operationNames = new HashSet<string>();
                foreach (ServiceMethod method in serviceMethods)
                {
                    if (!operationNames.Add(method.OperationName))
                    {
                        _reportDiagnostic(
                            Diagnostic.Create(
                                DiagnosticDescriptors.DuplicateOperationNames,
                                classDeclaration.GetLocation(),
                                method.OperationName,
                                classDeclaration.Identifier.Text));
                    }
                }

                var serviceClass = new ServiceClass(
                    classSymbol.Name,
                    GetFullName(classSymbol.ContainingNamespace),
                    classDeclaration.Keyword.ValueText,
                    serviceMethods,
                    hasBaseServiceClass: baseServiceClass is not null,
                    isSealed: classSymbol.IsSealed);
                serviceDefinitions.Add(serviceClass);

                static bool IsAllowedKind(SyntaxKind kind) =>
                    kind == SyntaxKind.ClassDeclaration ||
                    kind == SyntaxKind.StructDeclaration ||
                    kind == SyntaxKind.RecordDeclaration;

                SyntaxNode? parentNode = classDeclaration.Parent;
                ContainerDefinition? container = serviceClass;
                if (parentNode is TypeDeclarationSyntax parentType && IsAllowedKind(parentType.Kind()))
                {
                    container.Enclosing = new ContainerDefinition(
                        parentType.Identifier.ToString(),
                        parentType.Keyword.ValueText);
                    container = container.Enclosing;
                    parentNode = parentNode.Parent;
                }
            }
        }
        return serviceDefinitions;
    }

    private AttributeData? GetAttribute(ISymbol symbol, INamedTypeSymbol attributeSymbol)
    {
        ImmutableArray<AttributeData> attributes = symbol.GetAttributes();
        foreach (AttributeData attribute in attributes)
        {
            if (SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, attributeSymbol))
            {
                return attribute;
            }
        }
        return null;
    }

    /// <summary>Returns the nearest base class with the SliceService attribute.</summary>
    /// <param name="classSymbol">The class symbol.</param>
    /// <returns>The nearest base class with the SliceService attribute. null when there is no such base class.</returns>
    private INamedTypeSymbol? GetBaseServiceClass(INamedTypeSymbol classSymbol)
    {
        if (classSymbol.BaseType is INamedTypeSymbol baseType)
        {
            if (GetAttribute(baseType, _serviceAttribute!) is not null)
            {
                return baseType;
            }
            return GetBaseServiceClass(baseType);
        }
        return null;
    }

    private string GetFullName(ISymbol symbol)
    {
        if (symbol is INamespaceSymbol namespaceSymbol && namespaceSymbol.IsGlobalNamespace)
        {
            return "";
        }
        else
        {
            string containingSymbolName = GetFullName(symbol.ContainingSymbol);
            return containingSymbolName.Length == 0 ? symbol.Name : $"{containingSymbolName}.{symbol.Name}";
        }
    }

    private IReadOnlyList<ServiceMethod> GetServiceMethods(ImmutableArray<INamedTypeSymbol> allInterfaces)
    {
        Debug.Assert(_serviceMethodAttribute is not null);
        var allServiceMethods = new List<ServiceMethod>();
        foreach (INamedTypeSymbol interfaceSymbol in allInterfaces)
        {
            allServiceMethods.AddRange(GetServiceMethods(interfaceSymbol));
        }
        return allServiceMethods;
    }

    private IReadOnlyList<ServiceMethod> GetServiceMethods(INamedTypeSymbol interfaceSymbol)
    {
        var serviceMethods = new List<ServiceMethod>();
        foreach (IMethodSymbol method in interfaceSymbol.GetMembers().OfType<IMethodSymbol>())
        {
            if (GetAttribute(method, _serviceMethodAttribute!) is not AttributeData attribute)
            {
                continue;
            }

            foreach (TypedConstant typedConstant in attribute.ConstructorArguments)
            {
                if (typedConstant.Kind == TypedConstantKind.Error)
                {
                    // if a compilation error was found, no need to keep evaluating other args
                    return serviceMethods;
                }
            }

            ImmutableArray<TypedConstant> items = attribute.ConstructorArguments;
            Debug.Assert(
                items.Length == 1,
                "Unexpected number of arguments in attribute constructor.");
            string operationName = (string)items[0].Value!;

            bool compressReturn = false;
            bool encodedReturn = false;
            string[] exceptionSpecification = [];
            bool idempotent = false;

            foreach (KeyValuePair<string, TypedConstant> namedArgument in attribute.NamedArguments)
            {
                switch (namedArgument.Key)
                {
                    case "CompressReturn":
                        if (namedArgument.Value.Value is bool c)
                        {
                            compressReturn = c;
                        }
                        break;
                    case "EncodedReturn":
                        if (namedArgument.Value.Value is bool encodedReturnBool)
                        {
                            encodedReturn = encodedReturnBool;
                        }
                        break;
                    case "ExceptionSpecification":
                        if (namedArgument.Value.Values is ImmutableArray<TypedConstant> exceptionTypes)
                        {
                            exceptionSpecification = exceptionTypes
                                .Select(et => et.Value)
                                .OfType<INamedTypeSymbol>()
                                .Select(GetFullName)
                                .ToArray();
                        }
                        break;
                    case "Idempotent":
                        if (namedArgument.Value.Value is bool b)
                        {
                            idempotent = b;
                        }
                        break;
                }
            }

            string dispatchMethodName = method.Name.Substring(0, method.Name.Length - "Async".Length);

            // Find the nested Request class within the interface
            INamedTypeSymbol? requestClass = interfaceSymbol
                .GetTypeMembers("Request")
                .FirstOrDefault();

            IMethodSymbol? decodeArgsMethod = requestClass?
                .GetMembers()
                .OfType<IMethodSymbol>()
                .FirstOrDefault(m => m.Name == $"Decode{dispatchMethodName}Async");

            Debug.Assert(
                decodeArgsMethod is not null,
                $"Cannot find decode method for operation {operationName} in interface {interfaceSymbol.Name}.");

            // Analyze the return type of the decode method (ValueTask or ValueTask<T>)
            int parameterCount = 0;
            string[] parameterFieldNames = [];

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

            int returnCount = 0;
            string[] returnFieldNames = [];
            bool streamReturn = false;

            if (method.ReturnType is INamedTypeSymbol methodReturnType &&
                methodReturnType.IsGenericType &&
                methodReturnType.TypeArguments.Length == 1)
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

                if (SymbolEqualityComparer.Default.Equals(lastFieldType.OriginalDefinition, _asyncEnumerableSymbol))
                {
                    streamReturn = true;
                }
                else if (SymbolEqualityComparer.Default.Equals(lastFieldType.OriginalDefinition, _pipeReaderSymbol))
                {
                    streamReturn = !encodedReturn || returnCount > 1;
                    // else, the single parameter is the encoded return, and there is no stream
                }
            }
            // else: It's ValueTask (non-generic), returnCount remains 0 and streamReturn remains false.

            serviceMethods.Add(
                new ServiceMethod(
                    dispatchMethodName: dispatchMethodName,
                    operationName: operationName,
                    fullInterfaceName: GetFullName(interfaceSymbol),
                    parameterCount: parameterCount,
                    parameterFieldNames: parameterFieldNames,
                    returnCount: returnCount,
                    returnFieldNames: returnFieldNames,
                    returnStream: streamReturn,
                    compressReturn: compressReturn,
                    encodedReturn: encodedReturn,
                    exceptionSpecification: exceptionSpecification,
                    idempotent: idempotent));
        }
        return serviceMethods;
    }
}

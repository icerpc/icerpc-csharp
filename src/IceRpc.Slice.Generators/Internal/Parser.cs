// Copyright (c) ZeroC, Inc.

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Collections.Immutable;
using System.Diagnostics;

namespace IceRpc.Slice.Generators.Internal;

internal sealed class Parser
{
    internal const string OperationAttribute = "IceRpc.Slice.SliceOperationAttribute";
    internal const string ServiceAttribute = "IceRpc.Slice.SliceServiceAttribute";

    private readonly CancellationToken _cancellationToken;
    private readonly Compilation _compilation;
    private readonly INamedTypeSymbol? _operationAttribute;
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

        _operationAttribute = _compilation.GetTypeByMetadataName(OperationAttribute);
        _serviceAttribute = _compilation.GetTypeByMetadataName(ServiceAttribute);
    }

    internal IReadOnlyList<ServiceClass> GetServiceDefinitions(IEnumerable<ClassDeclarationSyntax> classes)
    {
        if (_operationAttribute is null || _serviceAttribute is null)
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
        Debug.Assert(_operationAttribute is not null);
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
            if (GetAttribute(method, _operationAttribute!) is not AttributeData attribute)
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
            serviceMethods.Add(new ServiceMethod(dispatchMethodName: GetFullName(method), operationName));
        }
        return serviceMethods;
    }
}

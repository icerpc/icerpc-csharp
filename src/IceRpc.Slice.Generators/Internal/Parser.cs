// Copyright (c) ZeroC, Inc.

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Collections.Immutable;
using System.Diagnostics;

namespace IceRpc.Slice.Generators.Internal;

internal sealed class Parser
{
    internal const string IceObjectService = "IceRpc.Slice.Ice.IIceObjectService";
    internal const string OperationAttribute = "IceRpc.Slice.SliceOperationAttribute";
    internal const string ServiceAttribute = "IceRpc.Slice.SliceServiceAttribute";
    internal const string SliceTypeIdAttribute = "ZeroC.Slice.SliceTypeIdAttribute";

    private readonly CancellationToken _cancellationToken;
    private readonly Compilation _compilation;
    private readonly INamedTypeSymbol? _iceObjectService;
    private readonly INamedTypeSymbol? _operationAttribute;
    private readonly Action<Diagnostic> _reportDiagnostic;
    private readonly INamedTypeSymbol? _serviceAttribute;
    private readonly INamedTypeSymbol? _typeIdAttribute;

    internal Parser(
        Compilation compilation,
        Action<Diagnostic> reportDiagnostic,
        CancellationToken cancellationToken)
    {
        _compilation = compilation;
        _reportDiagnostic = reportDiagnostic;
        _cancellationToken = cancellationToken;

        _iceObjectService = _compilation.GetTypeByMetadataName(IceObjectService);
        _operationAttribute = _compilation.GetTypeByMetadataName(OperationAttribute);
        _serviceAttribute = _compilation.GetTypeByMetadataName(ServiceAttribute);
        _typeIdAttribute = _compilation.GetTypeByMetadataName(SliceTypeIdAttribute);
    }

    internal IReadOnlyList<ServiceClass> GetServiceDefinitions(IEnumerable<ClassDeclarationSyntax> classes)
    {
        if (_iceObjectService is null ||
            _operationAttribute is null ||
            _serviceAttribute is null ||
            _typeIdAttribute is null)
        {
            // nothing to do if these types aren't available
            return Array.Empty<ServiceClass>();
        }

        var serviceDefinitions = new List<ServiceClass>();
        // we enumerate by syntax tree, to minimize the need to instantiate semantic models (since they're expensive)
        foreach (IGrouping<SyntaxTree, ClassDeclarationSyntax> group in classes.GroupBy(x => x.SyntaxTree))
        {
            SemanticModel? semanticModel = null;
            foreach (ClassDeclarationSyntax classDeclaration in group)
            {
                // stop if we're asked to
                _cancellationToken.ThrowIfCancellationRequested();

                semanticModel = _compilation.GetSemanticModel(classDeclaration.SyntaxTree);

                INamedTypeSymbol? classSymbol = semanticModel.GetDeclaredSymbol(classDeclaration, _cancellationToken);
                if (classSymbol is null)
                {
                    continue;
                }

                IReadOnlyList<ServiceMethod> baseServiceMethods = Array.Empty<ServiceMethod>();
                bool hasBaseServiceDefinition = false;
                if (classSymbol.BaseType is INamedTypeSymbol baseType)
                {
                    baseServiceMethods = GetServiceMethods(baseType.AllInterfaces);
                    hasBaseServiceDefinition = HasServiceAttribute(baseType);
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
                    hasBaseServiceDefinition,
                    implementIceObject: IsIceObject(classSymbol.AllInterfaces),
                    isSealed: classSymbol.IsSealed,
                    typeIds: GetTypeIds(classSymbol));
                serviceDefinitions.Add(serviceClass);

                static bool IsAllowedKind(SyntaxKind kind) =>
                    kind == SyntaxKind.ClassDeclaration ||
                    kind == SyntaxKind.StructDeclaration ||
                    kind == SyntaxKind.RecordDeclaration;

                SyntaxNode? parentNode = classDeclaration.Parent;
                ContainerDefinition? container = serviceClass;
                if (parentNode is TypeDeclarationSyntax parentType && IsAllowedKind(parentType.Kind()))
                {
                    container.Parent = new ContainerDefinition(
                        parentType.Identifier.ToString(),
                        parentType.Keyword.ValueText);
                    container = container.Parent;
                    parentNode = parentNode.Parent;
                }
            }
        }
        return serviceDefinitions;
    }

    private SortedSet<string> GetTypeIds(INamedTypeSymbol classSymbol)
    {
        var typeIds = new SortedSet<string>();

        if (GetTypeId(_iceObjectService!) is string iceObjectTypeId)
        {
            typeIds.Add(iceObjectTypeId);
        }

        foreach (INamedTypeSymbol interfaceSymbol in classSymbol.AllInterfaces)
        {
            if (GetTypeId(interfaceSymbol!) is string typeId)
            {
                typeIds.Add(typeId);
            }
        }

        return typeIds;

        string? GetTypeId(INamedTypeSymbol symbol)
        {
            if (GetAttribute(symbol, _typeIdAttribute!) is not AttributeData attribute)
            {
                return null;
            }

            foreach (TypedConstant typedConstant in attribute.ConstructorArguments)
            {
                if (typedConstant.Kind == TypedConstantKind.Error)
                {
                    // if a compilation error was found, no need to keep evaluating other args
                    return null;
                }
            }

            ImmutableArray<TypedConstant> items = attribute.ConstructorArguments;
            switch (items.Length)
            {
                case 1:
                    Debug.Assert(items[0].Type?.SpecialType is SpecialType.System_String);
                    return (string?)items[0].Value;
                default:
                    Debug.Assert(false, "Unexpected number of arguments in attribute constructor.");
                    break;
            }

            return null;
        }
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

            string? operationName = null;
            ImmutableArray<TypedConstant> items = attribute.ConstructorArguments;
            switch (items.Length)
            {
                case 1:
                    Debug.Assert(items[0].Type?.SpecialType is SpecialType.System_String);
                    operationName = (string?)items[0].Value;
                    break;
                default:
                    Debug.Assert(false, "Unexpected number of arguments in attribute constructor.");
                    break;
            }

            Debug.Assert(operationName is not null);

            serviceMethods.Add(new ServiceMethod
            {
                OperationName = operationName!,
                DispatchMethodName = GetFullName(method)
            });
        }
        return serviceMethods;
    }

    private bool IsIceObject(ImmutableArray<INamedTypeSymbol> allInterfaces)
    {
        Debug.Assert(_iceObjectService is not null);
        foreach (INamedTypeSymbol interfaceSymbol in allInterfaces)
        {
            if (SymbolEqualityComparer.Default.Equals(interfaceSymbol, _iceObjectService))
            {
                return true;
            }
        }
        return false;
    }

    private bool HasServiceAttribute(INamedTypeSymbol classSymbol)
    {
        foreach (AttributeData attribute in classSymbol.GetAttributes())
        {
            if (SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, _serviceAttribute))
            {
                return true;
            }
        }

        if (classSymbol.BaseType is INamedTypeSymbol baseType)
        {
            return HasServiceAttribute(baseType);
        }

        return false;
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
}

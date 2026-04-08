// Copyright (c) ZeroC, Inc.

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Collections.Immutable;

namespace IceRpc.ServiceGenerator.Internal;

internal sealed class Parser
{
    internal const string ServiceAttribute = "IceRpc.ServiceAttribute";
    private readonly CancellationToken _cancellationToken;
    private readonly Compilation _compilation;
    private readonly Action<Diagnostic> _reportDiagnostic;
    private readonly INamedTypeSymbol? _serviceAttribute;
    private readonly IReadOnlyList<IServiceMethodFactory> _serviceMethodFactoryList;

    internal Parser(
        Compilation compilation,
        Action<Diagnostic> reportDiagnostic,
        CancellationToken cancellationToken)
    {
        _compilation = compilation;
        _reportDiagnostic = reportDiagnostic;
        _cancellationToken = cancellationToken;
        _serviceAttribute = compilation.GetTypeByMetadataName(ServiceAttribute);

        _serviceMethodFactoryList =
        [
            new SliceServiceMethodFactory(compilation),
            new ProtobufServiceMethodFactory(compilation),
            new IceServiceMethodFactory(compilation),
        ];
    }

    internal IReadOnlyList<ServiceClass> GetServiceDefinitions(IEnumerable<ClassDeclarationSyntax> classes)
    {
        if (_serviceAttribute is null)
        {
            // nothing to do
            return [];
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

                IReadOnlyList<ServiceMethod> baseServiceMethods = [];
                INamedTypeSymbol? baseServiceClass = GetBaseServiceClass(classSymbol);
                if (baseServiceClass is not null)
                {
                    baseServiceMethods = GetServiceMethods(baseServiceClass.AllInterfaces);
                }

                IEnumerable<ServiceMethod> serviceMethods =
                    GetServiceMethods(classSymbol.AllInterfaces).Except(baseServiceMethods);

                // We check for duplicates only once per class.
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

                // Suppress duplicates, if any.
                serviceMethods = serviceMethods.Distinct();

                string containingNamespace = classSymbol.ContainingNamespace.GetFullName();
                var serviceClass = new ServiceClass(
                    classSymbol.Name,
                    containingNamespace.Length > 0 ? containingNamespace : null,
                    classDeclaration.Keyword.ValueText,
                    serviceMethods.ToList(),
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

    /// <summary>Returns the nearest base class with the Service attribute.</summary>
    /// <param name="classSymbol">The class symbol.</param>
    /// <returns>The nearest base class with the Service attribute. null when there is no such base class.</returns>
    private INamedTypeSymbol? GetBaseServiceClass(INamedTypeSymbol classSymbol)
    {
        if (classSymbol.BaseType is INamedTypeSymbol baseType)
        {
            if (baseType.FindAttribute(_serviceAttribute!) is not null)
            {
                return baseType;
            }
            return GetBaseServiceClass(baseType);
        }
        return null;
    }

    private IReadOnlyList<ServiceMethod> GetServiceMethods(ImmutableArray<INamedTypeSymbol> allInterfaces)
    {
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
            foreach (IServiceMethodFactory factory in _serviceMethodFactoryList)
            {
                // When a factory succeeds, we don't try the following factories. It is an error for a method to
                // have several operation attributes, but we don't enforce it.
                if (factory.TryCreate(method, out ServiceMethod? serviceMethod))
                {
                    serviceMethods.Add(serviceMethod!);
                    break;
                }
            }
        }
        return serviceMethods;
    }
}

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

    internal IReadOnlyList<ServiceClass> GetServiceDefinitions(IEnumerable<TypeDeclarationSyntax> typeDeclarations)
    {
        if (_serviceAttribute is null)
        {
            // nothing to do
            return [];
        }

        var serviceDefinitions = new List<ServiceClass>();
        // we enumerate by syntax tree, to minimize the need to instantiate semantic models (since they're expensive)
        foreach (IGrouping<SyntaxTree, TypeDeclarationSyntax> group in typeDeclarations.GroupBy(x => x.SyntaxTree))
        {
            foreach (TypeDeclarationSyntax typeDeclaration in group)
            {
                // stop if we're asked to
                _cancellationToken.ThrowIfCancellationRequested();

                SemanticModel semanticModel = _compilation.GetSemanticModel(typeDeclaration.SyntaxTree);
                INamedTypeSymbol? classSymbol = semanticModel.GetDeclaredSymbol(typeDeclaration, _cancellationToken);
                if (classSymbol is null)
                {
                    continue;
                }

                ImmutableArray<INamedTypeSymbol> baseInterfaces = [];
                IReadOnlyList<ServiceMethod> baseServiceMethods = [];

                INamedTypeSymbol? baseServiceClass = GetBaseServiceClass(classSymbol);

                if (baseServiceClass is not null)
                {
                    baseInterfaces = baseServiceClass.AllInterfaces;
                    baseServiceMethods = GetServiceMethods(baseInterfaces);
                }

                // Partition the interfaces into those inherited from the base service class and those new in this
                // class. Methods from inherited interfaces are already dispatched by the base; methods from new
                // interfaces are what this class adds.
                var newInterfaces = classSymbol.AllInterfaces
                    // The explicit type argument is required because SymbolEqualityComparer.Default is typed as
                    // IEqualityComparer<ISymbol>, which would otherwise infer Except's element type as ISymbol.
                    .Except<INamedTypeSymbol>(baseInterfaces, SymbolEqualityComparer.Default)
                    .ToImmutableArray();

                IEnumerable<ServiceMethod> serviceMethods = GetServiceMethods(newInterfaces);

                // We check for duplicates only once per class. Seed the set with the base service operation names so
                // we also report collisions between an operation added by this class and a base operation.
                var operationNames = new HashSet<string>(baseServiceMethods.Select(m => m.OperationName));
                foreach (ServiceMethod method in serviceMethods)
                {
                    if (!operationNames.Add(method.OperationName))
                    {
                        // The diagnostic report is not fatal: we keep going.
                        _reportDiagnostic(
                            Diagnostic.Create(
                                DiagnosticDescriptors.DuplicateOperationNames,
                                typeDeclaration.GetLocation(),
                                method.OperationName,
                                typeDeclaration.Identifier.Text));
                    }
                }

                // Suppress duplicates, if any. Any such duplicate was reported above as an error.
                serviceMethods = serviceMethods.Distinct();

                string containingNamespace = classSymbol.ContainingNamespace.GetFullName();
                // Capture the type-parameter list (e.g. "<T1, T2>") in the name so the emitted partial declaration
                // matches the user's generic type. WithoutTrivia gives a clean form with no source whitespace.
                // Constraint clauses (where T : ...) are not captured: C# only requires them on one partial
                // declaration, so the user's own declaration carries them.
                string typeParameterList =
                    typeDeclaration.TypeParameterList?.WithoutTrivia().ToString() ?? string.Empty;
                // Use Identifier.Text (not classSymbol.Name) so verbatim identifiers like @class keep their @.
                var serviceClass = new ServiceClass(
                    typeDeclaration.Identifier.Text + typeParameterList,
                    containingNamespace.Length > 0 ? containingNamespace : null,
                    GetKeyword(typeDeclaration),
                    typeDeclaration.TypeParameterList?.Parameters.Count ?? 0,
                    serviceMethods.ToList(),
                    hasBaseServiceClass: baseServiceClass is not null,
                    isSealed: classSymbol.IsSealed);
                serviceDefinitions.Add(serviceClass);

                // Walk the chain of enclosing type declarations (class, struct, record, record struct, interface) so
                // the emitted partial declaration is nested inside the same chain as the user's source.
                SyntaxNode? parentNode = typeDeclaration.Parent;
                ContainerDefinition container = serviceClass;
                while (parentNode is TypeDeclarationSyntax parentType)
                {
                    string parentTypeParameterList =
                        parentType.TypeParameterList?.WithoutTrivia().ToString() ?? string.Empty;
                    container.Enclosing = new ContainerDefinition(
                        parentType.Identifier.Text + parentTypeParameterList,
                        GetKeyword(parentType),
                        parentType.TypeParameterList?.Parameters.Count ?? 0);
                    container = container.Enclosing;
                    parentNode = parentNode.Parent;
                }

                // A record struct is a RecordDeclarationSyntax with ClassOrStructKeyword == "struct"; its Keyword
                // alone ("record") is not enough to disambiguate from a record class. Other type declarations are
                // fully described by their Keyword.
                static string GetKeyword(TypeDeclarationSyntax decl) =>
                    decl is RecordDeclarationSyntax record && record.ClassOrStructKeyword.ValueText == "struct" ?
                        "record struct" :
                        decl.Keyword.ValueText;
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

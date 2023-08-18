// Copyright (c) ZeroC, Inc.

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace IceRpc.Slice.Generators;

/// <summary>Provides a generator to implement IceRpc.IDispatcher for classes annotated with
/// IceRpc.Slice.SliceServiceAttribute.</summary>
[Generator]
public class ServiceGenerator : IIncrementalGenerator
{
    /// <inheritdoc/>
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        IncrementalValuesProvider<ClassDeclarationSyntax> classDeclarations =
            context.SyntaxProvider
                .ForAttributeWithMetadataName(
                    "IceRpc.Slice.SliceServiceAttribute",
                    (node, _) => node is ClassDeclarationSyntax,
                    (context, _) => (ClassDeclarationSyntax)context.TargetNode);

        IncrementalValueProvider<(Compilation Compilation, ImmutableArray<ClassDeclarationSyntax> ClassDeclarations)> compilationAndClasses =
            context.CompilationProvider.Combine(classDeclarations.Collect());

        context.RegisterSourceOutput(
            compilationAndClasses,
            static (context, source) => Execute(context, source.Compilation, source.ClassDeclarations));
    }

    private static void Execute(
        SourceProductionContext context,
        Compilation compilation,
        ImmutableArray<ClassDeclarationSyntax> classes)
    {
        if (classes.IsDefaultOrEmpty)
        {
            return;
        }

        IEnumerable<ClassDeclarationSyntax> distinctClasses = classes.Distinct();

        var parser = new Parser(compilation, context.ReportDiagnostic, context.CancellationToken);
        IReadOnlyList<ServiceDefinition> serviceClasses = parser.GetServiceDefinitions(distinctClasses);

        if (serviceClasses.Count > 0)
        {
            var emitter = new Emitter();
            string result = emitter.Emit(serviceClasses, context.CancellationToken);
            context.AddSource("Service.g.cs", SourceText.From(result, Encoding.UTF8));
        }
    }

    /// <summary>Represents a C# definition that is the parent of a service definition.</summary>
    internal class ContainerDefinition
    {
        /// <summary>Gets the keyowrd definition (struct, class, or record).</summary>
        internal string Keyword { get; }

        /// <summary>Gets the definition name.</summary>
        internal string Name { get; }

        /// <summary>Gets or sets the parent definition in which this definition is nested, nulls if this definition is
        /// not nested into another definition.</summary>
        internal ContainerDefinition? Parent { get; set; }

        internal ContainerDefinition(string name, string keyword)
        {
            Name = name;
            Keyword = keyword;
        }
    }

    /// <summary>Represents a C# definition that has the IceRpc.Slice.SliceServiceAttribute and for wich this generator
    /// implements the <c>IceRpc.IDispatcher</c> interface.</summary>
    internal class ServiceDefinition : ContainerDefinition
    {
        /// <summary>Gets a value indicating whether the service has a base service definition.</summary>
        internal bool HasBaseServiceDefinition { get; }

        /// <summary>Gets the C# namespace containing this definition.</summary>
        internal string ContainingNamespace { get; }

        /// <summary>Gets a value indicating whether the service is a sealed type.</summary>
        internal bool IsSealed { get; }

        /// <summary>Gets the service type IDs.</summary>
        internal SortedSet<string> TypeIds { get; }

        /// <summary>Gets the service methods implemented by the service.</summary>
        /// <remarks>It doesn't include the service methods implemented by the base service defintion if any.</remarks>
        internal IReadOnlyList<ServiceMethod> ServiceMethods { get; }

        internal ServiceDefinition(
            string name,
            string containingNamespace,
            string keyword,
            IReadOnlyList<ServiceMethod> serviceMethods,
            bool hasBaseServiceDefinition,
            bool isSealed,
            SortedSet<string> typeIds)
            : base(name, keyword)
        {
            ContainingNamespace = containingNamespace;
            ServiceMethods = serviceMethods;
            HasBaseServiceDefinition = hasBaseServiceDefinition;
            IsSealed = isSealed;
            TypeIds = typeIds;
        }
    }

    /// <summary>Represents an RPC operation annotated with the IceRpc.Slice.SliceOperationAttribute attribute.
    /// </summary>
    internal record struct ServiceMethod
    {
        // The fully qualified name of the generated dispatch helper method, for example:
        // "IceRpc.Slice.Ice.ILocatorService.SliceDFindObjectByIdAsync"
        internal string DispatchMethodName { get; set; }

        // The name of the service operation as defined in Slice interface, for example:
        // "findObjectById"
        internal string OperationName { get; set; }
    }

    internal sealed class Parser
    {
        internal const string ServiceAttribute = "IceRpc.Slice.SliceServiceAttribute";
        internal const string OperationAttribute = "IceRpc.Slice.SliceOperationAttribute";
        internal const string IceObjectService = "IceRpc.Slice.Ice.IIceObjectService";
        internal const string SliceTypeIdAttribute = "ZeroC.Slice.SliceTypeIdAttribute";

        private readonly CancellationToken _cancellationToken;
        private readonly Compilation _compilation;
        private readonly Action<Diagnostic> _reportDiagnostic;

        private readonly INamedTypeSymbol? _serviceAttribute;
        private readonly INamedTypeSymbol? _operationAttribute;
        private readonly INamedTypeSymbol? _iceObjectService;
        private readonly INamedTypeSymbol? _sliceTypeIdAttribute;

        internal Parser(
            Compilation compilation,
            Action<Diagnostic> reportDiagnostic,
            CancellationToken cancellationToken)
        {
            _compilation = compilation;
            _reportDiagnostic = reportDiagnostic;
            _cancellationToken = cancellationToken;

            _serviceAttribute = _compilation.GetTypeByMetadataName(ServiceAttribute);
            _operationAttribute = _compilation.GetTypeByMetadataName(OperationAttribute);
            _iceObjectService = _compilation.GetTypeByMetadataName(IceObjectService);
            _sliceTypeIdAttribute = _compilation.GetTypeByMetadataName(SliceTypeIdAttribute);
        }

        internal IReadOnlyList<ServiceDefinition> GetServiceDefinitions(IEnumerable<ClassDeclarationSyntax> classes)
        {
            if (_serviceAttribute is null ||
                _operationAttribute is null ||
                _iceObjectService is null ||
                _sliceTypeIdAttribute is null)
            {
                // nothing to do if these types aren't available
                return Array.Empty<ServiceDefinition>();
            }

            var serviceDefinitions = new List<ServiceDefinition>();
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
                        .Union(GetServiceMethods(_iceObjectService))
                        .Distinct()
                        .ToList();

                    var operationNames = new HashSet<string>();
                    foreach (ServiceMethod method in serviceMethods)
                    {
                        if (!operationNames.Add(method.OperationName))
                        {
                            _reportDiagnostic(
                                Diagnostic.Create(
                                    DiagnosticDescriptors.ShouldntReuseOperationNames,
                                    classDeclaration.GetLocation(),
                                    method.OperationName,
                                    classDeclaration.Identifier.Text));
                        }
                    }

                    var serviceClass = new ServiceDefinition(
                        classSymbol.Name,
                        GetFullName(classSymbol.ContainingNamespace),
                        classDeclaration.Keyword.ValueText,
                        serviceMethods,
                        hasBaseServiceDefinition,
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
                if (GetAttribute(symbol, _sliceTypeIdAttribute!) is not AttributeData attribute)
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

    internal class Emitter
    {
        private static readonly string _newLine = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "\r\n" : "\n";

        internal string Emit(IReadOnlyList<ServiceDefinition> serviceClasses, CancellationToken cancellationToken)
        {
            string generated = "";
            foreach (ServiceDefinition serviceClass in serviceClasses)
            {
                // stop if we're asked to.
                cancellationToken.ThrowIfCancellationRequested();

                string dispatchModifier =
                    serviceClass.HasBaseServiceDefinition ? "public override" :
                    serviceClass.IsSealed ? "public" : "public virtual";

                string dispatcherBlock;
                if (serviceClass.ServiceMethods.Count > 0)
                {
                    string dispatchBlocks = "";
                    foreach (ServiceMethod serviceMethod in serviceClass.ServiceMethods)
                    {
                        dispatchBlocks += @$"

case ""{serviceMethod.OperationName}"":
    return {serviceMethod.DispatchMethodName}(this, request, cancellationToken);";
                    }

                    if (serviceClass.HasBaseServiceDefinition)
                    {
                        dispatchBlocks += @$"

default:
    return base.DispatchAsync(request, cancellationToken);";
                    }
                    else
                    {
                        dispatchBlocks += @$"

default:
    return new(new IceRpc.OutgoingResponse(request, IceRpc.StatusCode.NotImplemented));";
                    }
                    dispatchBlocks = dispatchBlocks.Trim();

                    dispatcherBlock = @$"
switch (request.Operation)
{{
    {dispatchBlocks.WithIndent("    ")}
}}".Trim();
                }
                else
                {
                    dispatcherBlock = serviceClass.HasBaseServiceDefinition ?
                        "base.DispatchAsync(request, cancellationToken);" :
                        "new(new IceRpc.OutgoingResponse(request, IceRpc.StatusCode.NotImplemented));";
                }

                string typeIds = "";
                foreach (string typeId in serviceClass.TypeIds)
                {
                    typeIds += @$"""{typeId}"",{_newLine}";
                }
                typeIds = typeIds.TrimEnd(',', '\n', '\r', ' ');

                string dispatcherClass = $@"
partial {serviceClass.Keyword} {serviceClass.Name} : IceRpc.IDispatcher, IceRpc.Slice.Ice.IIceObjectService
{{
    private static readonly global::System.Collections.Generic.IReadOnlySet<string> _typeIds =
        new global::System.Collections.Generic.SortedSet<string>()
        {{
            {typeIds.WithIndent("            ")}
        }};

    {dispatchModifier} global::System.Threading.Tasks.ValueTask<IceRpc.OutgoingResponse> DispatchAsync(
        IceRpc.IncomingRequest request,
        global::System.Threading.CancellationToken cancellationToken)
    {{
        {dispatcherBlock.WithIndent("        ")}
    }}

    /// <inheritdoc/>
    {dispatchModifier} global::System.Threading.Tasks.ValueTask<global::System.Collections.Generic.IEnumerable<string>> IceIdsAsync(
        IceRpc.Features.IFeatureCollection features,
        global::System.Threading.CancellationToken cancellationToken) =>
        new(_typeIds);

    /// <inheritdoc/>
    {dispatchModifier} global::System.Threading.Tasks.ValueTask<bool> IceIsAAsync(
        string id,
        IceRpc.Features.IFeatureCollection features,
        global::System.Threading.CancellationToken cancellationToken) =>
        new(_typeIds.Contains(id));

    /// <inheritdoc/>
    {dispatchModifier} global::System.Threading.Tasks.ValueTask IcePingAsync(
        IceRpc.Features.IFeatureCollection features,
        global::System.Threading.CancellationToken cancellationToken) => default;
}}";

                string container = dispatcherClass;
                ContainerDefinition? containerDefinition = serviceClass;
                while (containerDefinition.Parent is ContainerDefinition parent)
                {
                    container = WriteContainer($"partial {parent.Keyword} {parent.Name}", container);
                    containerDefinition = parent;
                }

                generated += WriteContainer($"namespace {serviceClass.ContainingNamespace}", container);
                generated += $"{_newLine}{_newLine}";
            }
            return generated.Trim();
        }

        private static string WriteContainer(string header, string body)
        {
            return $@"
{header}
{{
    {body.WithIndent("    ")}
}}".Trim();
        }
    }
}

internal static class StringExtensions
{
    internal static string WithIndent(this string contents, string indent) =>
        string.Join("\n", contents.Split('\n').Select(value => $"{indent}{value}")).Trim();
}

internal static class DiagnosticDescriptors
{
    internal static DiagnosticDescriptor ShouldntReuseOperationNames { get; } = new DiagnosticDescriptor(
        id: "SLICE1001",
        title: "Multiple RPC operatrions cannot use the same name within a service class",
        messageFormat: "Multiple RPC operations are using name {0} in class {1}",
        category: "SliceServiceGenerator",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);
}

// Copyright (c) ZeroC, Inc.

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using System.Collections.Immutable;
using System.Diagnostics;
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
        /// <summary>Gets whether or not the service has a base service definition.</summary>
        internal bool HasBaseServiceDefinition { get; }

        /// <summary>Gets the C# namespace containing this definition.</summary>
        internal string ContainingNamespace { get; }

        /// <summary>Gets whether or not the service is a sealed type.</summary>
        internal bool IsSealed { get; }

        /// <summary>The service methods implemented by the service. It doesn't include the service methods implemented
        /// by the base service defintion if any.</summary>
        internal IReadOnlyList<ServiceMethod> ServiceMethods { get; }

        internal ServiceDefinition(
            string name,
            string containingNamespace,
            string keyword,
            IReadOnlyList<ServiceMethod> serviceMethods,
            bool hasBaseServiceDefinition,
            bool isSealed)
            : base(name, keyword)
        {
            ContainingNamespace = containingNamespace;
            ServiceMethods = serviceMethods;
            HasBaseServiceDefinition = hasBaseServiceDefinition;
            IsSealed = isSealed;
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

        private readonly CancellationToken _cancellationToken;
        private readonly Compilation _compilation;
        private readonly Action<Diagnostic> _reportDiagnostic;

        private readonly INamedTypeSymbol? _serviceAttribute;
        private readonly INamedTypeSymbol? _operationAttribute;

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
        }

        internal IReadOnlyList<ServiceDefinition> GetServiceDefinitions(IEnumerable<ClassDeclarationSyntax> classes)
        {
            if (_operationAttribute is null)
            {
                // nothing to do if this type isn't available
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
                        baseServiceMethods = GetServiceMethods(baseType);
                        if (baseServiceMethods.Count > 0 || HasServiceAttribute(baseType))
                        {
                            hasBaseServiceDefinition = true;
                        }
                    }

                    IReadOnlyList<ServiceMethod> serviceMethods = GetServiceMethods(classSymbol);
                    var operationNames = new HashSet<string>();
                    foreach(ServiceMethod method in serviceMethods)
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
                    serviceMethods = serviceMethods.Except(baseServiceMethods).ToArray();

                    var serviceClass = new ServiceDefinition(
                        classSymbol.Name,
                        GetFullName(classSymbol.ContainingNamespace),
                        classDeclaration.Keyword.ValueText,
                        serviceMethods,
                        hasBaseServiceDefinition,
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

        public IReadOnlyList<ServiceMethod> GetServiceMethods(INamedTypeSymbol classSymbol)
        {
            Debug.Assert(_operationAttribute is not null);
            var allServiceMethods = new List<ServiceMethod>();
            foreach (INamedTypeSymbol interfaceSymbol in classSymbol.AllInterfaces)
            {
                var serviceMethods = new List<ServiceMethod>();
                foreach (IMethodSymbol method in interfaceSymbol.GetMembers().OfType<IMethodSymbol>())
                {
                    string? operationName = null;
                    ImmutableArray<AttributeData> attributes = method.GetAttributes();
                    if (attributes.IsEmpty)
                    {
                        continue;
                    }

                    foreach (AttributeData attribute in attributes)
                    {
                        if (!SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, _operationAttribute))
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

                            ImmutableArray<TypedConstant> items = attribute.ConstructorArguments;
                            switch (items.Length)
                            {
                                case 1:
                                    Debug.Assert(items[0].Type?.SpecialType is SpecialType.System_String);
                                    operationName = (string?)GetItem(items[0]);
                                    break;
                                default:
                                    Debug.Assert(false, "Unexpected number of arguments in attribute constructor.");
                                    break;
                            }
                        }
                    }

                    Debug.Assert(operationName is not null);

                    serviceMethods.Add(new ServiceMethod
                    {
                        OperationName = operationName!,
                        DispatchMethodName = GetFullName(method)
                    });
                }
                allServiceMethods.AddRange(serviceMethods);
            }
            return allServiceMethods;
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

        private static object? GetItem(TypedConstant arg) => arg.Kind == TypedConstantKind.Array ? arg.Values : arg.Value;
    }

    internal class Emitter
    {
        internal string Emit(IReadOnlyList<ServiceDefinition> serviceClasses, CancellationToken cancellationToken)
        {
            string generated = "";
            foreach (ServiceDefinition serviceClass in serviceClasses)
            {
                // stop if we're asked to.
                cancellationToken.ThrowIfCancellationRequested();

                string dispatcherClass;
                string dispatchModifier =
                    serviceClass.HasBaseServiceDefinition ? "public override" :
                    serviceClass.IsSealed ? "public" : "public virtual";

                if (serviceClass.ServiceMethods.Count > 0)
                {
                    string dispatchBlocks = "";
                    foreach (ServiceMethod serviceMethod in serviceClass.ServiceMethods)
                    {
                        dispatchBlocks += @$"

case ""{serviceMethod.OperationName}"":
    return await {serviceMethod.DispatchMethodName}(this, request, cancellationToken).ConfigureAwait(false);";
                    }

                    if (serviceClass.HasBaseServiceDefinition)
                    {
                        dispatchBlocks += @$"

default:
    return await base.DispatchAsync(request, cancellationToken).ConfigureAwait(false);";
                    }
                    else
                    {
                        dispatchBlocks += @$"

default:
    return new IceRpc.OutgoingResponse(request, IceRpc.StatusCode.NotImplemented);";
                    }
                    dispatchBlocks = dispatchBlocks.Trim();

                    dispatcherClass = $@"
partial {serviceClass.Keyword} {serviceClass.Name} : IceRpc.IDispatcher
{{
    {dispatchModifier} async System.Threading.Tasks.ValueTask<IceRpc.OutgoingResponse> DispatchAsync(IceRpc.IncomingRequest request, System.Threading.CancellationToken cancellationToken)
    {{
        try
        {{
            switch(request.Operation)
            {{
                {dispatchBlocks.WithIndent("                ")}
            }}
        }}
        catch (IceRpc.Slice.DispatchException exception)
        {{
            if (exception.ConvertToInternalError)
            {{
                return new IceRpc.OutgoingResponse(request, IceRpc.StatusCode.InternalError, message: null, exception);
            }}
            return new IceRpc.OutgoingResponse(request, exception.StatusCode);
        }}
    }}
}}";
                }
                else
                {
                    string dispatcherBlock = serviceClass.HasBaseServiceDefinition ?
                        "base.DispatchAsync(request, cancellationToken);" :
                        "new(new IceRpc.OutgoingResponse(request, IceRpc.StatusCode.NotImplemented));";
                    dispatcherClass = $@"
partial {serviceClass.Keyword} {serviceClass.Name} : IceRpc.IDispatcher
{{
    {dispatchModifier} System.Threading.Tasks.ValueTask<IceRpc.OutgoingResponse> DispatchAsync(IceRpc.IncomingRequest request, System.Threading.CancellationToken cancellationToken)
    {{
        try
        {{
            return {dispatcherBlock};
        }}
        catch (IceRpc.Slice.DispatchException exception)
        {{
            if (exception.ConvertToInternalError)
            {{
                return new(new IceRpc.OutgoingResponse(request, IceRpc.StatusCode.InternalError, message: null, exception));
            }}
            return new(new IceRpc.OutgoingResponse(request, exception.StatusCode));
        }}
    }}
}}";
                }
                string container = dispatcherClass;
                ContainerDefinition? containerDefinition = serviceClass;
                while (containerDefinition.Parent is ContainerDefinition parent)
                {
                    container = WriteContainer($"partial {parent.Keyword} {parent.Name}", container);
                    containerDefinition = parent;
                }

                generated += WriteContainer($"namespace {serviceClass.ContainingNamespace}", container);
                generated += "\n";
            }
            return generated;
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
        title: new LocalizableResourceString(
            nameof(Messages.ShouldntReuseOperationNamesTitle),
            Messages.ResourceManager,
            typeof(Messages)),
        messageFormat: new LocalizableResourceString(
            nameof(Messages.ShouldntReuseOperationNamesMessage),
            Messages.ResourceManager,
            typeof(Messages)),
        category: "SliceServiceGenerator",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);
}

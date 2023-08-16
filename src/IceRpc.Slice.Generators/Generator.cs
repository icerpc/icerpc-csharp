// Copyright (c) ZeroC, Inc.

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Text;

namespace IceRpc.Slice.Generators;

/// <summary>Provides a generator to implement IDispatch for classes annotated with IceRpc.Slice.ServiceAttribute.
/// </summary>
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

        var parser = new Parser(compilation, context.CancellationToken);
        IReadOnlyList<ServiceClass> serviceClasses = parser.GetServiceClasses(distinctClasses);

        if (serviceClasses.Count > 0)
        {
            var emitter = new Emitter();
            string result = emitter.Emit(serviceClasses, context.CancellationToken);
            context.AddSource("Service.g.cs", SourceText.From(result, Encoding.UTF8));
        }
    }

    /// <summary>Represents a class that has the IceRpc.Slice.ServiceAttribute and for wich we want to implement
    /// the IDispatcher interface.</summary>
    internal class ServiceClass
    {
        // The base service class or null if the base class is not a service.
        internal ServiceClass? BaseServiceClass { get; set; }

        internal string Keyword { get; }

        internal string Name { get; }

        internal string Namespace { get; }

        // The service interfaces implemented by the service class. It doesn't include the service interfaces
        // implemented by base service class if any.
        internal IReadOnlyList<ServiceMethod> ServiceMethods { get; }

        internal ServiceClass(string name, string containingNamespace, string keyword, IReadOnlyList<ServiceMethod> serviceMethods)
        {
            Name = name;
            Keyword = keyword;
            Namespace = containingNamespace;
            ServiceMethods = serviceMethods;
        }
    }

    /// <summary>Represents an RPC operation in a C# interface generated from a Slice interface.</summary>
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
        internal const string OperationAttribute = "IceRpc.Slice.SliceOperationAttribute";

        private readonly CancellationToken _cancellationToken;
        private readonly Compilation _compilation;

        private readonly INamedTypeSymbol? _operationAttribute;

        public Parser(Compilation compilation, CancellationToken cancellationToken)
        {
            _compilation = compilation;
            _cancellationToken = cancellationToken;

            _operationAttribute = _compilation.GetTypeByMetadataName(OperationAttribute);
        }

        /// <summary>Gets the set of logging classes containing methods to output.</summary>
        public IReadOnlyList<ServiceClass> GetServiceClasses(IEnumerable<ClassDeclarationSyntax> classes)
        {
            if (_operationAttribute is null)
            {
                // nothing to do if this type isn't available
                return Array.Empty<ServiceClass>();
            }

            var serviceClasses = new List<ServiceClass>();
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

                    IReadOnlyList<ServiceMethod> serviceMethods = GetServiceMethods(classSymbol);
                    if (serviceMethods.Count > 0)
                    {
                        serviceClasses.Add(
                            new ServiceClass(
                                classSymbol.Name,
                                GetFullName(classSymbol.ContainingNamespace),
                                classDeclaration.Keyword.ValueText,
                                serviceMethods));
                    }
                }
            }
            return serviceClasses;
        }

        public IReadOnlyList<ServiceMethod> GetServiceMethods(INamedTypeSymbol classSymbol)
        {
            Debug.Assert(_operationAttribute is not null);
            var allServiceMethods = new List<ServiceMethod>();
            // TODO exclude intefaces from parent class annotated with [Service]
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
        internal string Emit(IReadOnlyList<ServiceClass> serviceClasses, CancellationToken cancellationToken)
        {
            string generated = "";
            foreach (ServiceClass serviceClass in serviceClasses) 
            {
                // stop if we're asked to.
                cancellationToken.ThrowIfCancellationRequested();

                var dispatchBlocks = new List<string>();
                foreach (ServiceMethod serviceMethod in serviceClass.ServiceMethods)
                {
                    dispatchBlocks.Add(@$"
case ""{serviceMethod.OperationName}"":
   return await {serviceMethod.DispatchMethodName}(this, request, cancellationToken).ConfigureAwait(false);".Trim());
                }

                generated += $@"
namespace {serviceClass.Namespace}
{{
    partial {serviceClass.Keyword} {serviceClass.Name} : IceRpc.IDispatcher
    {{
        public async ValueTask<IceRpc.OutgoingResponse> DispatchAsync(IceRpc.IncomingRequest request, CancellationToken cancellationToken)
        {{
            try
            {{
                switch(request.Operation)
                {{
                    {dispatchBlocks.WithIndent("                ")}
                    default:
                        return new IceRpc.OutgoingResponse(request, IceRpc.StatusCode.NotImplemented);
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
    }}
}}";
            }
            return generated;
        }
    }
}

internal static class StringListExtensions
{
    internal static string WithIndent(this IList<string> contents, string indent) =>
        string.Join(
            "\n\n", 
            contents
                .SelectMany(value => value.Split('\n'))
                .Select(value => $"{indent}{value}")).Trim();
}

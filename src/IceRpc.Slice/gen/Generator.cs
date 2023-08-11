// Copyright (c) ZeroC, Inc.

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using System.Collections.Immutable;
using System.Text;
using System.Xml.Linq;

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
                    "IceRpc.Slice.ServiceAttribute",
                    (node, _) => node is ClassDeclarationSyntax,
                    (context, _) => (ClassDeclarationSyntax)context.TargetNode);

        context.RegisterSourceOutput(
            classDeclarations.Collect(),
            static (context, classDeclarations) => Execute(context, classDeclarations));

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
        IReadOnlyList<ServiceClass> serviceClasses = parser.GetServiceClasses(distinctClasses);

        if (distinctClasses.Any())
        {
            string result = "";
            foreach (var classDeclaration in distinctClasses)
            {
                classDeclaration.WithAttributeLists
            }
            context.AddSource("Service.g.cs", SourceText.From(result, Encoding.UTF8));
        }
    }

    /// <summary>Represents a class that has the IceRpc.Slice.ServiceAttribute and for wich we want to implement
    /// the IDispatcher interface.</summary>
    internal class ServiceClass
    {
        // The base service class or null if the base class is not a service.
        internal ServiceClass? BaseServiceClasses = { get; set; }

        // The service interfaces implemented by the service class. It doesn't include the service interfaces
        // implemented by base service class if any.
        private readonly List<ServiceInteface> _serviceIntefaces = new();
    }

    /// <summary>Represents a C# interface generated from a Slice interface.</summary>
    internal class ServiceInteface
    {
        internal string Name { get; set; }

        // The list of methods defined for the interface.
        private readonly List<ServiceMethod> _methods = new();
    }

    /// <summary>Represents an RPC operation in a C# interface generated from a Slice interface.</summary>
    internal class ServiceMethod 
    {
        // The fully qualified name of the generated dispatch helper method, for example:
        // "IceRpc.Slice.Ice.ILocatorService.SliceDFindObjectByIdAsync"
        internal string DispatchMethodName { get; set; }

        // The name of the service operation as defined in Slice interface, for example:
        // "findObjectById"
        internal string OperationName { get; set; }

        internal ServiceMethod(string operationName, string dispatchMethodName)
        {
            OperationName = operationName;
            DispatchMethodName = dispatchMethodName;
        }
    }

    internal sealed class Parser
    {
        internal const string ServiceAttribute = "IceRpc.Slice.ServiceAttribute";
        internal const string OperationAttribute = "IceRpc.Slice.OperationAttribute";

        private readonly CancellationToken _cancellationToken;
        private readonly Compilation _compilation;
        private readonly Action<Diagnostic> _reportDiagnostic;

        public Parser(Compilation compilation, Action<Diagnostic> reportDiagnostic, CancellationToken cancellationToken)
        {
            _compilation = compilation;
            _cancellationToken = cancellationToken;
            _reportDiagnostic = reportDiagnostic;
        }

        /// <summary>
        /// Gets the set of logging classes containing methods to output.
        /// </summary>
        public IReadOnlyList<ServiceClass> GetServiceClasses(IEnumerable<ClassDeclarationSyntax> classes)
        {
            INamedTypeSymbol? serviceAttribute = _compilation.GetTypeByMetadataName(ServiceAttribute);
            if (serviceAttribute == null)
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
                }
            }

            return serviceClasses;
        }
    }
}

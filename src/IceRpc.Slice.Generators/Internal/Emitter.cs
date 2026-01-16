// Copyright (c) ZeroC, Inc.

using ZeroC.CodeBuilder;

namespace IceRpc.Slice.Generators.Internal;

internal class Emitter
{
    internal string Emit(ServiceClass serviceClass, CancellationToken cancellationToken)
    {
        // Stop if we're asked to.
        cancellationToken.ThrowIfCancellationRequested();

        CodeBlock codeBlock = Preamble();

        if (serviceClass.ContainingNamespace is not null)
        {
            codeBlock.AddBlock($"namespace {serviceClass.ContainingNamespace};");
        }

        // We need to implement IDispatcher all the time, even when there is a base class that itself implements
        // IDispatcher.
        CodeBlock container = new ContainerBuilder($"partial {serviceClass.Keyword}", serviceClass.Name)
                .AddBase("IceRpc.IDispatcher")
                .AddComment(
                    "summary",
                    "Implements <see cref=\"IceRpc.IDispatcher\" /> for the Slice interface(s) implemented by this class.")
                .AddBlock(GenerateDispatch(serviceClass))
                .Build();

        ContainerDefinition? containerDefinition = serviceClass;
        while (containerDefinition.Enclosing is ContainerDefinition enclosing)
        {
            container = new ContainerBuilder($"partial {enclosing.Keyword}", enclosing.Name)
                .AddBlock(container)
                .Build();

            containerDefinition = enclosing;
        }

        codeBlock.AddBlock(container);
        return codeBlock.ToString().ReplaceLineEndings();
    }

    private static CodeBlock GenerateDispatch(ServiceClass serviceClass)
    {
        FunctionBuilder dispatchFunctionBuilder =
            new FunctionBuilder(
                access: serviceClass.HasBaseServiceClass ?
                    "public override" :
                    serviceClass.IsSealed ? "public" : "public virtual",
                "global::System.Threading.Tasks.ValueTask<IceRpc.OutgoingResponse>",
                "DispatchAsync",
                FunctionType.BlockBody)
            .AddComment(
                "summary",
                @$"Dispatches an incoming request to a method of {serviceClass.Name} based on the operation name
carried by the request.")
            .AddParameter(
                "IceRpc.IncomingRequest",
                "request",
                docComment: "The incoming request.")
            .AddParameter(
                "global::System.Threading.CancellationToken",
                "cancellationToken",
                docComment: "A cancellation token that receives the cancellation requests.")
            .AddComment("returns", "The outgoing response.")
            .AddComment(
                "exception",
                "cref",
                "IceRpc.DispatchException",
                @"Thrown if the operation name carried by the request does not correspond to any method implemented by this class.
The exception status code is <see cref=""IceRpc.StatusCode.NotImplemented"" /> in this case.");

        dispatchFunctionBuilder.SetBody(GenerateDispatchBody(serviceClass));
        return dispatchFunctionBuilder.Build();
    }

    private static CodeBlock GenerateDispatchBody(ServiceClass serviceClass)
    {
        if (serviceClass.ServiceMethods.Count > 0)
        {
            var cases = new CodeBlock();
            foreach (ServiceMethod serviceMethod in serviceClass.ServiceMethods)
            {
                // The Indent is intentional: we want to indent the code in the case body.
                cases.AddBlock(GenerateDispatchCase(serviceMethod).Indent());
            }
            cases = cases.Indent(); // This indents all the case statements in the switch.

            var fallback = new CodeBlock();
            if (serviceClass.HasBaseServiceClass)
            {
                fallback.WriteLine(@"default:
    return base.DispatchAsync(request, cancellationToken);");
            }
            else
            {
                fallback.WriteLine(@"default:
    return new(new IceRpc.OutgoingResponse(request, IceRpc.StatusCode.NotImplemented));");
            }
            fallback = fallback.Indent();

            return @$"switch (request.Operation)
{{
    {cases}

    {fallback}
}}";
        }
        else if (serviceClass.HasBaseServiceClass)
        {
            return "return base.DispatchAsync(request, cancellationToken);";
        }
        else
        {
            return "return new(new IceRpc.OutgoingResponse(request, IceRpc.StatusCode.NotImplemented));";
        }
    }

    private static CodeBlock GenerateDispatchCase(ServiceMethod serviceMethod)
    {
        var codeBlock = new CodeBlock();
        codeBlock.WriteLine(@$"case ""{serviceMethod.OperationName}"":
{GenerateDispatchCaseBody(serviceMethod)}");

        return codeBlock;
    }

    private static CodeBlock GenerateDispatchCaseBody(ServiceMethod serviceMethod)
    {
        var codeBlock = new CodeBlock();
        if (!serviceMethod.Idempotent)
        {
            codeBlock.WriteLine("request.CheckNonIdempotent();");
        }
        if (serviceMethod.CompressReturn)
        {
            FunctionCallBuilder withCallBuilder =
                new FunctionCallBuilder("IceRpc.Features.FeatureCollectionExtensions.With")
                .ArgumentsOnNewline(true)
                .AddArgument("request.Features")
                .AddArgument("IceRpc.Features.CompressFeature.Compress");

            codeBlock.WriteLine($"request.Features = {withCallBuilder.Build()}");
        }

        string thisInterface = $"((global::{serviceMethod.FullInterfaceName})this)";

        string method;
        if (serviceMethod.ParameterCount <= 1)
        {
            method = $"{thisInterface}.{serviceMethod.DispatchMethodName}Async";
        }
        else
        {
            var methodCallBuilder = new FunctionCallBuilder($"{thisInterface}.{serviceMethod.DispatchMethodName}Async")
                .UseSemicolon(false);

            methodCallBuilder.AddArguments(serviceMethod.ParameterFieldNames.Select(name => $"args.{name}"))
                .AddArgument("features")
                .AddArgument("cancellationToken");

            method = @$"(args, features, cancellationToken) =>
        {methodCallBuilder.Build()}";
        }

        FunctionCallBuilder dispatchCallBuilder = new FunctionCallBuilder("request.DispatchOperationAsync")
            .ArgumentsOnNewline(true)
            .AddArgument(
                $"decodeArgs: global::{serviceMethod.FullInterfaceName}.Request.Decode{serviceMethod.DispatchMethodName}Async")
            .AddArgument($"method: {method}");

        // We don't use the generated Response.EncodeXxx method when ReturnCount is 0. So we could not generate it.
        if (serviceMethod.ReturnCount > 0)
        {
            if (serviceMethod.ReturnCount == 1)
            {
                if (serviceMethod.ReturnStream)
                {
                    dispatchCallBuilder.AddArgument(
                        @$"encodeReturnValue: (_, encodeOptions) =>
        global::{serviceMethod.FullInterfaceName}.Response.Encode{serviceMethod.DispatchMethodName}(encodeOptions)");

                    dispatchCallBuilder.AddArgument(
                        $"encodeReturnValueStream: global::{serviceMethod.FullInterfaceName}.Response.EncodeStreamOf{serviceMethod.DispatchMethodName}");
                }
                else if (serviceMethod.EncodedReturn)
                {
                    dispatchCallBuilder.AddArgument("encodeReturnValue: (returnValue, _) => returnValue");
                }
                else
                {
                    dispatchCallBuilder.AddArgument(
                        $"encodeReturnValue: global::{serviceMethod.FullInterfaceName}.Response.Encode{serviceMethod.DispatchMethodName}");
                }
            }
            else
            {
                // Splatting required.
                var nonStreamReturnNames = new List<string>(serviceMethod.ReturnFieldNames);
                if (serviceMethod.ReturnStream)
                {
                    nonStreamReturnNames.RemoveAt(serviceMethod.ReturnFieldNames.Length - 1);
                }

                if (serviceMethod.EncodedReturn)
                {
                    dispatchCallBuilder.AddArgument(
                        $"encodeReturnValue: (returnValue, _) => returnValue.{nonStreamReturnNames[0]}");
                }
                else
                {
                    var encodeBuilder = new FunctionCallBuilder(
                        $"global::{serviceMethod.FullInterfaceName}.Response.Encode{serviceMethod.DispatchMethodName}")
                            .UseSemicolon(false)
                            .AddArguments(nonStreamReturnNames.Select(name => $"returnValue.{name}"))
                            .AddArgument("encodeOptions");

                    dispatchCallBuilder.AddArgument(
                        @$"encodeReturnValue: (returnValue, encodeOptions) =>
        {encodeBuilder.Build()}");
                }

                if (serviceMethod.ReturnStream)
                {
                    string streamFieldName =
                        serviceMethod.ReturnFieldNames[serviceMethod.ReturnFieldNames.Length - 1];

                    var encodeBuilder = new FunctionCallBuilder(
                        $"global::{serviceMethod.FullInterfaceName}.Response.EncodeStreamOf{serviceMethod.DispatchMethodName}")
                            .UseSemicolon(false)
                            .AddArgument($"returnValue.{streamFieldName}")
                            .AddArgument("encodeOptions");

                    dispatchCallBuilder.AddArgument(
                        $"encodeReturnValueStream: (returnValue, encodeOptions) => {encodeBuilder.Build()}");
                }
            }
        }

        if (serviceMethod.ExceptionSpecification.Length > 0)
        {
            string exceptionList =
                string.Join(" or ", serviceMethod.ExceptionSpecification.Select(ex => $"global::{ex}"));

            dispatchCallBuilder.AddArgument(
                $"inExceptionSpecification: sliceException => sliceException is {exceptionList}");
        }

        dispatchCallBuilder.AddArgument("cancellationToken: cancellationToken");

        codeBlock.WriteLine($"return {dispatchCallBuilder.Build()}");
        return codeBlock;
    }

    private static CodeBlock Preamble() => @$"
// <auto-generated/>
// IceRpc.Slice.Generators version: {typeof(ServiceGenerator).Assembly.GetName().Version!.ToString(3)}

#nullable enable

#pragma warning disable CS0612 // Type or member is obsolete
#pragma warning disable CS0618 // Type or member is obsolete
#pragma warning disable CS0619 // Type or member is obsolete

using IceRpc.Slice;
using ZeroC.Slice;

";
}

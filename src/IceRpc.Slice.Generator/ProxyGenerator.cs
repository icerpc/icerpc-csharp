// Copyright (c) ZeroC, Inc.

using System.Collections.Immutable;
using ZeroC.CodeBuilder;
using ZeroC.Slice.Generator;
using ZeroC.Slice.Symbols;

namespace IceRpc.Slice.Generator;

/// <summary>Generates C# client-side code from a Slice interface: client interface, proxy struct, and
/// encoder/decoder extensions.</summary>
internal static class ProxyGenerator
{
    /// <summary>Generates all proxy-side code blocks for the given interface.</summary>
    internal static CodeBlock Generate(Interface interfaceDef)
    {
        string name = interfaceDef.Name;
        string scopedId = interfaceDef.ScopedIdentifier;
        string accessModifier = interfaceDef.AccessModifier;
        string currentNamespace = interfaceDef.Namespace;
        string defaultServicePath = $"/{scopedId.Replace("::", ".", StringComparison.Ordinal)}";

        return CodeBlock.FromBlocks(
        [
            GenerateClientInterface(interfaceDef, name, scopedId, accessModifier, currentNamespace),
            GenerateProxyStruct(interfaceDef, name, scopedId, accessModifier, currentNamespace, defaultServicePath),
            GenerateProxyEncoderExtensions(name, accessModifier),
            GenerateProxyDecoderExtensions(name, accessModifier),
        ]);
    }

    private static CodeBlock GenerateClientInterface(
        Interface interfaceDef,
        string name,
        string scopedId,
        string accessModifier,
        string currentNamespace)
    {
        ContainerBuilder builder = new ContainerBuilder($"{accessModifier} partial interface", $"I{name}")
            .AddDocCommentSummary(interfaceDef.Comment, currentNamespace)
            .AddComment(
                "remarks",
                $"""
                The Slice compiler generated this client-side interface from Slice interface <c>{scopedId}</c>.
                It's implemented by <see cref="{name}Proxy" />.
                """)
            .AddDocCommentSeeAlso(interfaceDef.Comment, currentNamespace)
            .AddDeprecatedAttribute(interfaceDef.Attributes);

        // Inherit from base client interfaces
        foreach (Interface baseInterface in interfaceDef.Bases)
        {
            string baseName = currentNamespace == baseInterface.Namespace
                ? $"I{baseInterface.Name}"
                : $"global::{baseInterface.Namespace}.I{baseInterface.Name}";
            builder.AddBase(baseName);
        }

        foreach (Operation op in interfaceDef.Operations)
        {
            builder.AddBlock(BuildClientOperationDeclaration(op, currentNamespace));
        }

        return builder.Build();
    }

    private static CodeBlock BuildClientOperationDeclaration(Operation op, string currentNamespace)
    {
        string opName = op.Name;
        string returnType = op.GetClientReturnType(currentNamespace);
        ImmutableList<Field> nonStreamedParams = op.NonStreamedParameters;

        var builder = new FunctionBuilder("", returnType, $"{opName}Async", FunctionType.Declaration)
            .AddDocCommentSummary(op.Comment, currentNamespace)
            .AddDeprecatedAttribute(op.Attributes);

        foreach (Field param in nonStreamedParams)
        {
            builder.AddParameter(
                param.DataType.OutgoingParameterTypeString(param.DataTypeIsOptional, currentNamespace),
                param.ParameterName,
                docComment: DocCommentFormatter.FormatOverview(param.Comment, currentNamespace));
        }

        // Streamed parameter (if any) goes after non-streamed params
        if (op.StreamedParameter is Field streamParam)
        {
            builder.AddParameter(
                OperationExtensions.GetStreamTypeString(streamParam, currentNamespace),
                streamParam.ParameterName,
                docComment: DocCommentFormatter.FormatOverview(streamParam.Comment, currentNamespace));
        }

        string featuresParam = op.FeaturesParamName;
        builder.AddParameter("IceRpc.Features.IFeatureCollection?", featuresParam, "null", "The invocation features.");
        builder.AddParameter(
            "global::System.Threading.CancellationToken",
            "cancellationToken",
            "default",
            "A cancellation token that receives the cancellation requests.");

        if (op.NonStreamedReturns.Count == 0 && !op.HasStreamedReturn)
        {
            builder.AddComment("returns", "A task that completes when the response is received.");
        }

        builder.AddDocCommentSeeAlso(op.Comment, currentNamespace);
        return builder.Build();
    }

    private static CodeBlock GenerateProxyStruct(
        Interface interfaceDef,
        string name,
        string scopedId,
        string accessModifier,
        string currentNamespace,
        string defaultServicePath)
    {
        string proxyName = $"{name}Proxy";

        ContainerBuilder builder = new ContainerBuilder($"{accessModifier} readonly partial record struct", proxyName)
            .AddComment(
                "summary",
                $"""
                Implements <see cref="I{name}" /> by making invocations on a remote IceRPC service.
                This remote service must implement Slice interface {scopedId}.
                """)
            .AddComment(
                "remarks",
                $"The Slice compiler generated this record struct from the Slice interface <c>{scopedId}</c>.")
            .AddDeprecatedAttribute(interfaceDef.Attributes)
            .AddBase($"I{name}")
            .AddBase("ISliceProxy")
            .AddBlock(BuildProxyRequestClass(interfaceDef, scopedId, currentNamespace))
            .AddBlock(BuildProxyResponseClass(interfaceDef, scopedId, currentNamespace))
            .AddBlock(BuildProxyProperties(scopedId, defaultServicePath));

        // Implicit conversion operators for base interfaces
        foreach (Interface baseInterface in interfaceDef.AllBases)
        {
            string baseProxyName = currentNamespace == baseInterface.Namespace
                ? $"{baseInterface.Name}Proxy"
                : $"global::{baseInterface.Namespace}.{baseInterface.Name}Proxy";
            builder.AddBlock(
                new FunctionBuilder(
                    "public static implicit",
                    "",
                    $"operator {baseProxyName}",
                    FunctionType.ExpressionBody)
                    .AddComment(
                        "summary",
                        $"""Provides an implicit conversion to <see cref="{baseProxyName}" />.""")
                    .AddParameter(proxyName, "proxy")
                    .SetBody(
                        "new() { EncodeOptions = proxy.EncodeOptions, Invoker = proxy.Invoker, ServiceAddress = proxy.ServiceAddress }")
                    .Build());
        }

        builder.AddBlock(BuildProxyConstructors(proxyName));

        // Generate inherited operations by delegating to the base proxy
        foreach (Interface baseInterface in interfaceDef.AllBases)
        {
            foreach (Operation op in baseInterface.Operations)
            {
                builder.AddBlock(BuildProxyBaseOperationDelegation(op, baseInterface, currentNamespace));
            }
        }

        // Generate own operations
        foreach (Operation op in interfaceDef.Operations)
        {
            builder.AddBlock(BuildProxyOperationCore(op, currentNamespace));
        }

        return builder.Build();
    }

    private static CodeBlock BuildProxyRequestClass(Interface interfaceDef, string scopedId, string currentNamespace)
    {
        ContainerBuilder request = new ContainerBuilder("public static class", "Request")
            .AddComment("summary", "Provides static methods that encode operation arguments into request payloads.")
            .AddComment(
                "remarks",
                $"The Slice compiler generated this static class from the Slice interface <c>{scopedId}</c>.");

        foreach (Operation op in interfaceDef.Operations)
        {

            ImmutableList<Field> nonStreamedParams = op.NonStreamedParameters;
            string opName = op.Name;

            CodeBlock? encodeBody = nonStreamedParams.GenerateEncodeBody(currentNamespace);

            if (encodeBody is null)
            {
                // Stream operations with no non-streamed params use CreateEmptySliceStructPayload
                // (encodes an empty Slice2 struct). Truly void operations use EmptyPipeReader.Instance.
                string emptyPayload = op.HasStreamedParameter ?
                    "System.IO.Pipelines.PipeReader.CreateEmptySliceStructPayload()" :
                    "IceRpc.EmptyPipeReader.Instance";

                request.AddBlock(
                    new FunctionBuilder(
                        "public static",
                        "global::System.IO.Pipelines.PipeReader",
                        $"Encode{opName}",
                        FunctionType.ExpressionBody)
                        .AddComment(
                            "summary",
                            $"Encodes the arguments of operation <c>{op.Identifier}</c> into a request payload.")
                        .AddParameter("SliceEncodeOptions?", "encodeOptions", "null", "The Slice encode options.")
                        .AddComment("returns", "The Slice-encoded payload.")
                        .SetBody(emptyPayload)
                        .Build());
            }
            else
            {
                FunctionBuilder fn = new FunctionBuilder(
                    "public static",
                    "global::System.IO.Pipelines.PipeReader",
                    $"Encode{opName}",
                    FunctionType.BlockBody)
                    .AddComment(
                        "summary",
                        $"Encodes the argument{(nonStreamedParams.Count > 1 ? "s" : "")} of operation <c>{op.Identifier}</c> into a request payload.");

                foreach (Field param in nonStreamedParams)
                {
                    fn.AddParameter(
                        param.DataType.OutgoingParameterTypeString(param.DataTypeIsOptional, currentNamespace),
                        param.ParameterName);
                }

                request.AddBlock(
                    fn.AddParameter("SliceEncodeOptions?", "encodeOptions", "null", "The Slice encode options.")
                        .AddComment("returns", "The Slice-encoded payload.")
                        .SetBody(InterfaceGenerator.BuildPipeEncodeBody(encodeBody))
                        .Build());
            }

            // Add EncodeStreamOf{Op} method for streamed parameters
            if (op.StreamedParameter is Field streamParam)
            {
                request.AddBlock(op.BuildEncodeStreamMethod(streamParam, currentNamespace));
            }
        }

        return request.Build();
    }

    private static CodeBlock BuildProxyResponseClass(Interface interfaceDef, string scopedId, string currentNamespace)
    {
        ContainerBuilder response = new ContainerBuilder("public static class", "Response")
            .AddComment(
                "summary",
                $"Provides a <see cref=\"ResponseDecodeFunc{{T}}\" /> for each operation defined in Slice interface {scopedId}.")
            .AddComment(
                "remarks",
                $"The Slice compiler generated this static class from the Slice interface <c>{scopedId}</c>.");

        foreach (Operation op in interfaceDef.Operations)
        {
            string opName = op.Name;
            ImmutableList<Field> nonStreamedReturns = op.NonStreamedReturns;
            Field? streamReturn = op.StreamedReturn;
            string returnType = op.GetProxyResponseReturnType(currentNamespace);

            if (streamReturn is null)
            {
                // Non-streaming: expression body
                FunctionBuilder decodeBuilder = new FunctionBuilder(
                    "public static",
                    returnType,
                    $"Decode{opName}Async",
                    FunctionType.ExpressionBody)
                    .AddComment("summary", $"Decodes an incoming response for operation <c>{op.Identifier}</c>.")
                    .AddParameter("IceRpc.IncomingResponse", "response")
                    .AddParameter("IceRpc.OutgoingRequest", "request")
                    .AddParameter("ISliceProxy", "sender")
                    .AddParameter("global::System.Threading.CancellationToken", "cancellationToken");

                if (nonStreamedReturns.Count == 0)
                {
                    decodeBuilder.SetBody(
                        """
                        response.DecodeVoidReturnValueAsync(
                            request,
                            cancellationToken)
                        """);
                }
                else
                {
                    string decodeLambda = nonStreamedReturns.GenerateDecodeLambda(currentNamespace);
                    decodeBuilder.SetBody(
                        $$"""
                        response.DecodeReturnValueAsync(
                            request,
                            sender,
                            {{decodeLambda}},
                            cancellationToken)
                        """);
                }

                response.AddBlock(decodeBuilder.Build());
            }
            else
            {
                // Streaming: async block body
                FunctionBuilder decodeBuilder = new FunctionBuilder(
                    "public static async",
                    returnType,
                    $"Decode{opName}Async",
                    FunctionType.BlockBody)
                        .AddComment("summary", $"Decodes an incoming response for operation <c>{op.Identifier}</c>.")
                        .AddParameter("IceRpc.IncomingResponse", "response")
                        .AddParameter("IceRpc.OutgoingRequest", "request")
                        .AddParameter("ISliceProxy", "sender")
                        .AddParameter("global::System.Threading.CancellationToken", "cancellationToken");

                var body = new CodeBlock();

                // Decode non-streamed returns first
                if (nonStreamedReturns.Count == 0)
                {
                    body.WriteLine(
                        """
                        await response.DecodeVoidReturnValueAsync(
                            request,
                            cancellationToken).ConfigureAwait(false);
                        """);
                }
                else
                {
                    string decodeLambda = nonStreamedReturns.GenerateDecodeLambda(currentNamespace);
                    string varName = nonStreamedReturns.Count == 1
                        ? $"sliceP_{nonStreamedReturns[0].ParameterName}"
                        : $"({string.Join(", ", nonStreamedReturns.Select(r => $"sliceP_{r.ParameterName}"))})";
                    body.WriteLine($$"""
                        var {{varName}} = await response.DecodeReturnValueAsync(
                            request,
                            sender,
                            {{decodeLambda}},
                            cancellationToken).ConfigureAwait(false);
                        """);
                }

                // Decode stream return
                body.WriteLine("");
                if (streamReturn.IsByteStream)
                {
                    body.WriteLine("var sliceP_returnValue = IceRpc.IncomingFrameExtensions.DetachPayload(response);");
                }
                else
                {
                    body.WriteLine("var payloadContinuation = IceRpc.IncomingFrameExtensions.DetachPayload(response);");
                    string streamElemType = streamReturn.DataType.FieldTypeString(
                        streamReturn.DataTypeIsOptional,
                        currentNamespace);
                    string decodeLambda = streamReturn.DataTypeIsOptional
                        ? OperationExtensions.GetStreamOfOptionalDecodeLambda(streamReturn, currentNamespace)
                        : streamReturn.DataType.Type.GetDecodeLambda(isOptional: false, currentNamespace);

                    if (streamReturn.DataType.FixedSize is int fixedSize && !streamReturn.DataTypeIsOptional)
                    {
                        body.WriteLine($$"""
                            var sliceP_returnValue = payloadContinuation.ToAsyncEnumerable<{{streamElemType}}>(
                                {{decodeLambda}},
                                {{fixedSize}});
                            """);
                    }
                    else
                    {
                        body.WriteLine($$"""
                            var sliceP_returnValue = payloadContinuation.ToAsyncEnumerable<{{streamElemType}}>(
                                {{decodeLambda}},
                                sender,
                                sliceFeature: request.Features.Get<IceRpc.Features.ISliceFeature>());
                            """);
                    }
                }

                // Build return statement
                if (nonStreamedReturns.Count == 0)
                {
                    body.WriteLine("return sliceP_returnValue;");
                }
                else
                {
                    var returnParts = nonStreamedReturns.Select(r => $"sliceP_{r.ParameterName}").ToList();
                    returnParts.Add("sliceP_returnValue");
                    body.WriteLine($"return ({string.Join(", ", returnParts)});");
                }

                decodeBuilder.SetBody(body);
                response.AddBlock(decodeBuilder.Build());
            }
        }

        return response.Build();
    }

    private static CodeBlock BuildProxyOperationCore(Operation op, string currentNamespace)
    {
        string opName = op.Name;
        string returnType = op.GetClientReturnType(currentNamespace);
        ImmutableList<Field> nonStreamedParams = op.NonStreamedParameters;
        Field? streamParam = op.StreamedParameter;

        bool compressArgs = op.Attributes.FindAttribute("compress") is ZeroC.Slice.Symbols.Attribute compressAttr &&
            compressAttr.Args.Any(a => a == "Args");

        FunctionBuilder builder = new FunctionBuilder(
            "public",
            returnType,
            $"{opName}Async",
            compressArgs ? FunctionType.BlockBody : FunctionType.ExpressionBody)
            .SetInheritDoc(true);

        foreach (Field param in nonStreamedParams)
        {
            builder.AddParameter(
                param.DataType.OutgoingParameterTypeString(param.DataTypeIsOptional, currentNamespace),
                param.ParameterName);
        }

        if (streamParam is not null)
        {
            builder.AddParameter(
                OperationExtensions.GetStreamTypeString(streamParam, currentNamespace),
                streamParam.ParameterName);
        }

        string featuresParam = op.FeaturesParamName;
        builder.AddParameter("IceRpc.Features.IFeatureCollection?", featuresParam, "null");
        builder.AddParameter("global::System.Threading.CancellationToken", "cancellationToken", "default");

        string encodeArgs = nonStreamedParams.Count > 0
            ? string.Join(", ", nonStreamedParams.Select(p => p.ParameterName)) + ", encodeOptions: EncodeOptions"
            : "encodeOptions: EncodeOptions";

        // payloadContinuation: either null or the stream encoder
        string payloadContinuation = streamParam is not null
            ? $"Request.EncodeStreamOf{opName}({streamParam.ParameterName}, encodeOptions: EncodeOptions)"
            : "null";

        FunctionCallBuilder bodyBuilder = new FunctionCallBuilder("this.InvokeOperationAsync")
            .ArgumentsOnNewLine(true)
            .UseSemicolon(false)
            .AddArgument($"\"{op.Identifier}\"")
            .AddArgument($"payload: Request.Encode{opName}({encodeArgs})")
            .AddArgument($"payloadContinuation: {payloadContinuation}")
            .AddArgument($"Response.Decode{opName}Async")
            .AddArgument(featuresParam)
            .AddArgumentIf(op.IsIdempotent, "idempotent: true")
            .AddArgumentIf(op.Attributes.HasAttribute("oneway"), "oneway: true")
            .AddArgument("cancellationToken: cancellationToken");

        if (compressArgs)
        {
            var body = new CodeBlock();
            body.WriteLine($$"""
                if ({{featuresParam}}?.Get<IceRpc.Features.ICompressFeature>() is null)
                {
                    {{featuresParam}} ??= new IceRpc.Features.FeatureCollection();
                    {{featuresParam}} = IceRpc.Features.FeatureCollectionExtensions.With(
                        {{featuresParam}},
                        IceRpc.Features.CompressFeature.Compress);
                }
                return {{bodyBuilder.Build()}};
                """);
            builder.SetBody(body);
        }
        else
        {
            builder.SetBody(bodyBuilder.Build());
        }
        return builder.Build();
    }

    /// <summary>Generates a proxy method that delegates to the base proxy's implementation.</summary>
    private static CodeBlock BuildProxyBaseOperationDelegation(
        Operation op,
        Interface baseInterface,
        string currentNamespace)
    {
        string opName = op.Name;
        string returnType = op.GetClientReturnType(currentNamespace);
        ImmutableList<Field> nonStreamedParams = op.NonStreamedParameters;

        string baseProxyName = currentNamespace == baseInterface.Namespace
            ? $"{baseInterface.Name}Proxy"
            : $"global::{baseInterface.Namespace}.{baseInterface.Name}Proxy";

        var paramNames = nonStreamedParams.Select(p => p.ParameterName).ToList();
        if (op.StreamedParameter is Field streamParam)
        {
            paramNames.Add(streamParam.ParameterName);
        }
        string featuresParam = op.FeaturesParamName;
        paramNames.Add(featuresParam);
        paramNames.Add("cancellationToken");

        FunctionBuilder builder = new FunctionBuilder(
            "public", returnType, $"{opName}Async", FunctionType.ExpressionBody)
            .SetInheritDoc(true);

        foreach (Field param in nonStreamedParams)
        {
            builder.AddParameter(
                param.DataType.OutgoingParameterTypeString(param.DataTypeIsOptional, currentNamespace),
                param.ParameterName);
        }

        if (op.StreamedParameter is Field sp)
        {
            builder.AddParameter(OperationExtensions.GetStreamTypeString(sp, currentNamespace), sp.ParameterName);
        }

        builder.AddParameter("IceRpc.Features.IFeatureCollection?", featuresParam, "null");
        builder.AddParameter("global::System.Threading.CancellationToken", "cancellationToken", "default");

        builder.SetBody($"(({baseProxyName})this).{opName}Async({string.Join(", ", paramNames)})");

        return builder.Build();
    }

    private static CodeBlock GenerateProxyEncoderExtensions(string name, string accessModifier)
    {
        string proxyName = $"{name}Proxy";

        return new ContainerBuilder($"{accessModifier} static class", $"{proxyName}SliceEncoderExtensions")
            .AddComment(
                "summary",
                $"""Provides an extension method for <see cref="SliceEncoder" /> to encode a <see cref="{proxyName}" />.""")
            .AddBlock(
                new FunctionBuilder(
                    $"{accessModifier} static",
                    "void", $"Encode{proxyName}",
                    FunctionType.ExpressionBody)
                        .AddComment(
                            "summary",
                            $"""Encodes a <see cref="{proxyName}" /> as an <see cref="IceRpc.ServiceAddress" />.""")
                        .AddParameter("this ref SliceEncoder", "encoder", docComment: "The Slice encoder.")
                        .AddParameter(proxyName, "proxy", docComment: "The proxy to encode as a service address.")
                        .SetBody("encoder.EncodeServiceAddress(proxy.ServiceAddress)")
                        .Build())
            .Build();
    }

    private static CodeBlock GenerateProxyDecoderExtensions(string name, string accessModifier)
    {
        string proxyName = $"{name}Proxy";

        return new ContainerBuilder($"{accessModifier} static class", $"{proxyName}SliceDecoderExtensions")
            .AddComment(
                "summary",
                $"""Provides an extension method for <see cref="SliceDecoder" /> to decode a <see cref="{proxyName}" />.""")
            .AddBlock(
                new FunctionBuilder(
                    $"{accessModifier} static", 
                    proxyName, $"Decode{proxyName}", 
                    FunctionType.ExpressionBody)
                        .AddComment(
                            "summary",
                            $"""Decodes an <see cref="IceRpc.ServiceAddress" /> into a <see cref="{proxyName}" />.""")
                        .AddParameter("this ref SliceDecoder", "decoder", docComment: "The Slice decoder.")
                        .AddComment("returns", "The proxy created from the decoded service address.")
                        .SetBody($"decoder.DecodeProxy<{proxyName}>()")
                        .Build())
            .Build();
    }

    private static CodeBlock BuildProxyProperties(string scopedId, string defaultServicePath) =>
        $$"""
        /// <summary>Represents the default path for IceRPC services that implement Slice interface
        /// <c>{{scopedId}}</c>.</summary>
        public const string DefaultServicePath = "{{defaultServicePath}}";

        /// <inheritdoc/>
        public SliceEncodeOptions? EncodeOptions { get; init; }

        /// <inheritdoc/>
        public required IceRpc.IInvoker Invoker { get; init; }

        /// <inheritdoc/>
        public IceRpc.ServiceAddress ServiceAddress { get; init; } = _defaultServiceAddress;

        private static IceRpc.ServiceAddress _defaultServiceAddress =
            new(IceRpc.Protocol.IceRpc) { Path = DefaultServicePath };
        """;

    private static CodeBlock BuildProxyConstructors(string proxyName)
    {
        CodeBlock fromPath = new FunctionBuilder("public static", proxyName, "FromPath", FunctionType.ExpressionBody)
            .AddComment("summary", "Creates a relative proxy from a path.")
            .AddParameter("string", "path", docComment: "The path.")
            .AddComment("returns", "The new relative proxy.")
            .SetBody("new(IceRpc.InvalidInvoker.Instance, new IceRpc.ServiceAddress { Path = path })")
            .Build();

        CodeBlock mainCtor = new FunctionBuilder("public", "", proxyName, FunctionType.BlockBody)
            .AddComment("summary", "Constructs a proxy from an invoker, a service address and encode options.")
            .AddParameter("IceRpc.IInvoker", "invoker", docComment: "The invocation pipeline of the proxy.")
            .AddParameter(
                "IceRpc.ServiceAddress?",
                "serviceAddress",
                "null",
                """
                The service address. <see langword="null" /> is equivalent to an icerpc service address
                with path <see cref="DefaultServicePath" />.
                """)
            .AddParameter(
                "SliceEncodeOptions?",
                "encodeOptions",
                "null",
                "The encode options, used to customize the encoding of request payloads.")
            .AddSetsRequiredMembersAttribute()
            .SetBody(new CodeBlock("""
                Invoker = invoker;
                ServiceAddress = serviceAddress ?? _defaultServiceAddress;
                EncodeOptions = encodeOptions;
                """))
            .Build();

        CodeBlock uriCtor = new FunctionBuilder("public", "", proxyName, FunctionType.BlockBody)
            .AddComment("summary", "Constructs a proxy from an invoker, a service address URI and encode options.")
            .AddParameter("IceRpc.IInvoker", "invoker", docComment: "The invocation pipeline of the proxy.")
            .AddParameter("System.Uri", "serviceAddressUri", docComment: "A URI that represents a service address.")
            .AddParameter(
                "SliceEncodeOptions?",
                "encodeOptions",
                "null",
                "The encode options, used to customize the encoding of request payloads.")
            .AddSetsRequiredMembersAttribute()
            .AddThisParameters(["invoker", "new IceRpc.ServiceAddress(serviceAddressUri)", "encodeOptions"])
            .Build();

        CodeBlock defaultCtor = new FunctionBuilder("public", "", proxyName, FunctionType.BlockBody)
            .AddComment(
                "summary",
                @"Constructs a proxy with an icerpc service address with path <see cref=""DefaultServicePath"" />.")
            .Build();

        return CodeBlock.FromBlocks([fromPath, mainCtor, uriCtor, defaultCtor]);
    }
}

// Copyright (c) ZeroC, Inc.

using ZeroC.CodeBuilder;

namespace IceRpc.ServiceGenerator.Internal;

/// <summary>Represents an abstract method in a generated XxxService interface marked with an IDL-specific attribute,
/// such as <c>IceRpc.Slice.SliceOperationAttribute</c> for Slice.</summary>
internal class ServiceMethod : IServiceMethod
{
    /// <inheritdoc />
    public Idl Idl { get; }

    /// <inheritdoc />
    public string OperationName { get; }

    public IEnumerable<string> UsingDirectives => _usingDirectives;

    private static readonly string[] _usingDirectives =
    [
        "using IceRpc.Slice;",
        "using ZeroC.Slice.Codec;",
    ];

    /// <summary>A value indicating whether the return value should be compressed.</summary>
    private readonly bool _compressReturn;

    /// <summary>The name of the C# method minus the Async suffix. For example: "FindObjectById".</summary>
    private readonly string _dispatchMethodName;

    /// <summary>A value indicating whether the non-stream portion of the return value is pre-encoded by the
    /// application.</summary>
    private readonly bool _encodedReturn;

    /// <summary>The exception specification of the operation.</summary>
    private readonly string[] _exceptionSpecification;

    /// <summary>The name of the C# service interface, including its namespace. For example:
    /// "VisitorCenter.IGreeterService".</summary>
    private readonly string _fullInterfaceName;

    /// <summary>A value indicating whether the operation is idempotent.</summary>
    private readonly bool _idempotent;

    /// <summary>The arity of the operation.</summary>
    private readonly int _parameterCount;

    /// <summary>The capitalized names of the operation parameters. This is empty when <see cref="_parameterCount"/>
    /// is 0 or 1.</summary>
    private readonly string[] _parameterFieldNames;

    /// <summary>The number of elements in the return value.</summary>
    private readonly int _returnCount;

    /// <summary>The capitalized names of the operation return value fields. This is empty when
    /// <see cref="_returnCount"/> is 0 or 1.</summary>
    private readonly string[] _returnFieldNames;

    /// <summary>A value indicating whether the operation return value has a stream element.</summary>
    private readonly bool _returnStream;

    /// <inheritdoc />
    public CodeBlock GenerateDispatchCaseBody()
    {
        var codeBlock = new CodeBlock();
        if (!_idempotent)
        {
            codeBlock.WriteLine("request.CheckNonIdempotent();");
        }
        if (_compressReturn)
        {
            FunctionCallBuilder withCallBuilder =
                new FunctionCallBuilder("IceRpc.Features.FeatureCollectionExtensions.With")
                .ArgumentsOnNewLine(true)
                .AddArgument("request.Features")
                .AddArgument("IceRpc.Features.CompressFeature.Compress");

            codeBlock.WriteLine($"request.Features = {withCallBuilder.Build()}");
        }

        string thisInterface = $"((global::{_fullInterfaceName})this)";

        string method;
        if (_parameterCount <= 1)
        {
            method = $"{thisInterface}.{_dispatchMethodName}Async";
        }
        else
        {
            var methodCallBuilder = new FunctionCallBuilder($"{thisInterface}.{_dispatchMethodName}Async")
                .UseSemicolon(false);

            methodCallBuilder.AddArguments(_parameterFieldNames.Select(name => $"args.{name}"))
                .AddArgument("features")
                .AddArgument("cancellationToken");

            method = @$"(args, features, cancellationToken) =>
        {methodCallBuilder.Build()}";
        }

        FunctionCallBuilder dispatchCallBuilder = new FunctionCallBuilder("request.DispatchOperationAsync")
            .ArgumentsOnNewLine(true)
            .AddArgument(
                $"decodeArgs: global::{_fullInterfaceName}.Request.Decode{_dispatchMethodName}Async")
            .AddArgument($"method: {method}");

        // We don't use the generated Response.EncodeXxx method when _returnCount is 0. So we could not generate it.
        if (_returnCount > 0)
        {
            if (_returnCount == 1)
            {
                if (_returnStream)
                {
                    dispatchCallBuilder.AddArgument(
                        @$"encodeReturnValue: static (_, encodeOptions) =>
        global::{_fullInterfaceName}.Response.Encode{_dispatchMethodName}(encodeOptions)");

                    dispatchCallBuilder.AddArgument(
                        $"encodeReturnValueStream: global::{_fullInterfaceName}.Response.EncodeStreamOf{_dispatchMethodName}");
                }
                else if (_encodedReturn)
                {
                    dispatchCallBuilder.AddArgument("encodeReturnValue: (returnValue, _) => returnValue");
                }
                else
                {
                    dispatchCallBuilder.AddArgument(
                        $"encodeReturnValue: global::{_fullInterfaceName}.Response.Encode{_dispatchMethodName}");
                }
            }
            else
            {
                // Splatting required.
                var nonStreamReturnNames = new List<string>(_returnFieldNames);
                if (_returnStream)
                {
                    nonStreamReturnNames.RemoveAt(_returnFieldNames.Length - 1);
                }

                if (_encodedReturn)
                {
                    dispatchCallBuilder.AddArgument(
                        $"encodeReturnValue: (returnValue, _) => returnValue.{nonStreamReturnNames[0]}");
                }
                else
                {
                    var encodeBuilder = new FunctionCallBuilder(
                        $"global::{_fullInterfaceName}.Response.Encode{_dispatchMethodName}")
                            .UseSemicolon(false)
                            .AddArguments(nonStreamReturnNames.Select(name => $"returnValue.{name}"))
                            .AddArgument("encodeOptions");

                    dispatchCallBuilder.AddArgument(
                        @$"encodeReturnValue: static (returnValue, encodeOptions) =>
        {encodeBuilder.Build()}");
                }

                if (_returnStream)
                {
                    string streamFieldName =
                        _returnFieldNames[_returnFieldNames.Length - 1];

                    var encodeBuilder = new FunctionCallBuilder(
                        $"global::{_fullInterfaceName}.Response.EncodeStreamOf{_dispatchMethodName}")
                            .UseSemicolon(false)
                            .AddArgument($"returnValue.{streamFieldName}")
                            .AddArgument("encodeOptions");

                    dispatchCallBuilder.AddArgument(
                        $"encodeReturnValueStream: static (returnValue, encodeOptions) => {encodeBuilder.Build()}");
                }
            }
        }

        if (_exceptionSpecification.Length > 0)
        {
            string exceptionList =
                string.Join(" or ", _exceptionSpecification.Select(ex => $"global::{ex}"));

            dispatchCallBuilder.AddArgument(
                $"inExceptionSpecification: sliceException => sliceException is {exceptionList}");
        }

        dispatchCallBuilder.AddArgument("cancellationToken: cancellationToken");

        codeBlock.WriteLine($"return {dispatchCallBuilder.Build()}");
        return codeBlock;
    }

    /// <summary>Initializes a new instance of the <see cref="ServiceMethod"/> class.</summary>
    /// <param name="idl">The IDL used to define the corresponding operation.</param>
    /// <param name="operationName">The name of the operation defined in the Slice interface, for example:
    /// "findObjectById".</param>
    /// <param name="dispatchMethodName">The name of the C# method minus the Async suffix. For example:
    /// "FindObjectById".</param>
    /// <param name="fullInterfaceName">The name of the C# service interface, including its namespace. For example:
    /// "VisitorCenter.IGreeterService".</param>
    /// <param name="parameterCount">The arity of the operation.</param>
    /// <param name="parameterFieldNames">The capitalized names of the operation parameters. This is empty when
    /// <paramref name="parameterCount"/> is 0 or 1.</param>
    /// <param name="returnCount">The number of elements in the return value.</param>
    /// <param name="returnFieldNames">The capitalized names of the operation return value fields. This is empty when
    /// <paramref name="returnCount"/> is 0 or 1.</param>
    /// <param name="returnStream">A value indicating whether the operation return value has a stream
    /// element.</param>
    /// <param name="compressReturn">A value indicating whether the return value should be compressed.</param>
    /// <param name="encodedReturn">A value indicating whether the non-stream portion of the return value is
    /// pre-encoded by the application.</param>
    /// <param name="exceptionSpecification">The exception specification of the operation.</param>
    /// <param name="idempotent">A value indicating whether the operation is idempotent.</param>
    internal ServiceMethod(
        Idl idl,
        string operationName,
        string dispatchMethodName,
        string fullInterfaceName,
        int parameterCount,
        string[] parameterFieldNames,
        int returnCount,
        string[] returnFieldNames,
        bool returnStream,
        bool compressReturn,
        bool encodedReturn,
        string[] exceptionSpecification,
        bool idempotent)
    {
        Idl = idl;
        OperationName = operationName;
        _compressReturn = compressReturn;
        _dispatchMethodName = dispatchMethodName;
        _encodedReturn = encodedReturn;
        _exceptionSpecification = exceptionSpecification;
        _fullInterfaceName = fullInterfaceName;
        _idempotent = idempotent;
        _parameterCount = parameterCount;
        _parameterFieldNames = parameterFieldNames;
        _returnCount = returnCount;
        _returnFieldNames = returnFieldNames;
        _returnStream = returnStream;
    }
}

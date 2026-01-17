// Copyright (c) ZeroC, Inc.

namespace IceRpc.Slice.Generators.Internal;

/// <summary>Represents an RPC operation annotated with the <c>IceRpc.Slice.SliceOperationAttribute</c> attribute.
/// </summary>
internal readonly record struct ServiceMethod
{
    /// <summary>Gets the name of the C# method minus the Async suffix. For example: "FindObjectById".</summary>
    internal string DispatchMethodName { get; }

    /// <summary>Gets the name of the service operation as defined in Slice interface, for example: "findObjectById".
    /// </summary>
    internal string OperationName { get; }

    internal string FullInterfaceName { get; }

    /// <summary>Gets the arity of the operation.</summary>
    internal int ParameterCount { get; }

    /// <summary>Gets the capitalized names of the operation parameters.</summary>
    /// <remarks>This field is empty when <see cref="ParameterCount"/> is 0 or 1.</remarks>
    internal string[] ParameterFieldNames { get; }

    /// <summary>Gets the number of elements in the return value.</summary>
    internal int ReturnCount { get; }

    /// <summary>Gets the capitalized names of the operation return value fields.</summary>
    /// <remarks>This field is empty when <see cref="ReturnCount"/> is 0 or 1.</remarks>
    internal string[] ReturnFieldNames { get; }

    /// <summary>Gets a value indicating whether the operation return value has a stream element.</summary>
    internal bool ReturnStream { get; }

    internal bool CompressReturn { get; }

    internal bool EncodedReturn { get; }

    internal string[] ExceptionSpecification { get; }

    internal bool Idempotent { get; }

    internal ServiceMethod(
        string dispatchMethodName,
        string operationName,
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
        DispatchMethodName = dispatchMethodName;
        OperationName = operationName;
        FullInterfaceName = fullInterfaceName;
        ParameterCount = parameterCount;
        ParameterFieldNames = parameterFieldNames;
        ReturnCount = returnCount;
        ReturnFieldNames = returnFieldNames;
        ReturnStream = returnStream;
        CompressReturn = compressReturn;
        EncodedReturn = encodedReturn;
        ExceptionSpecification = exceptionSpecification;
        Idempotent = idempotent;
    }
}

// Copyright (c) ZeroC, Inc.

namespace IceRpc.Slice.Generators.Internal;

/// <summary>Represents an RPC operation annotated with the <c>IceRpc.Slice.SliceDOperationAttribute</c> attribute.
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
    internal int ParameterCount { get; init; }

    /// <summary>Gets the capitalized names of the operation parameters.</summary>
    /// <remarks>This field is empty when <see cref="ParameterCount"/> is 0 or 1.</remarks>
    internal string[] ParameterFieldNames { get; init; } = [];

    /// <summary>Gets the number of elements in the return value.</summary>
    internal int ReturnCount { get; init; }

    /// <summary>Gets the capitalized names of the operation return value fields.</summary>
    /// <remarks>This field is empty when <see cref="ReturnCount"/> is 0 or 1.</remarks>
    internal string[] ReturnFieldNames { get; init; } = [];

    /// <summary>Gets a value indicating whether the operation return value has a stream element.</summary>
    internal bool StreamReturn { get; init; }

    internal bool CompressReturn { get; init; }

    internal bool EncodedReturn { get; init; }

    internal string[] ExceptionSpecification { get; init; } = [];

    internal bool Idempotent { get; init; }

    internal ServiceMethod(string dispatchMethodName, string operationName, string fullInterfaceName)
    {
        DispatchMethodName = dispatchMethodName;
        OperationName = operationName;
        FullInterfaceName = fullInterfaceName;
    }
}

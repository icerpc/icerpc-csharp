// Copyright (c) ZeroC, Inc.

namespace IceRpc.Slice.Generators.Internal;

/// <summary>Represents an RPC operation annotated with the <c>IceRpc.Slice.SliceDOperationAttribute</c> attribute.
/// </summary>
internal readonly record struct ServiceMethod
{
    // The name of the C# method, for example: "FindObjectByIdAsync"
    internal string DispatchMethodName { get; }

    // The name of the service operation as defined in Slice interface, for example: "findObjectById"
    internal string OperationName { get; }

    internal string FullInterfaceName { get; }

    internal bool CompressReturn { get; }

    internal bool EncodedReturn { get; }

    internal string[] ExceptionSpecification { get; }

    internal bool Idempotent { get; }

    internal ServiceMethod(
        string dispatchMethodName,
        string operationName,
        string fullInterfaceName,
        bool compressReturnValue,
        bool encodedReturn,
        string[] exceptionSpecification,
        bool idempotent)
    {
        DispatchMethodName = dispatchMethodName;
        OperationName = operationName;
        FullInterfaceName = fullInterfaceName;
        CompressReturn = compressReturnValue;
        EncodedReturn = encodedReturn;
        ExceptionSpecification = exceptionSpecification;
        Idempotent = idempotent;
    }
}

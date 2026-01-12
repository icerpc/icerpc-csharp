// Copyright (c) ZeroC, Inc.

namespace IceRpc.Slice.Generators.Internal;

/// <summary>Represents an RPC operation annotated with the <c>IceRpc.Slice.SliceDOperationAttribute</c> attribute.
/// </summary>
internal readonly record struct ServiceMethod
{
    // The fully qualified name of the generated dispatch helper method, for example:
    // "IceRpc.Slice.Ice.ILocatorService.SliceDFindObjectByIdAsync"
    internal string DispatchMethodName { get; }

    // The name of the service operation as defined in Slice interface, for example:
    // "findObjectById"
    internal string OperationName { get; }

    internal bool CompressReturnValue { get; }

    internal bool EncodedReturn { get; }

    internal string[] ExceptionSpecification { get; }

    internal bool Idempotent { get; }

    internal ServiceMethod(
        string dispatchMethodName,
        string operationName,
        bool compressReturnValue,
        bool encodedReturn,
        string[] exceptionSpecification,
        bool idempotent)
    {
        DispatchMethodName = dispatchMethodName;
        OperationName = operationName;
        CompressReturnValue = compressReturnValue;
        EncodedReturn = encodedReturn;
        ExceptionSpecification = exceptionSpecification;
        Idempotent = idempotent;
    }
}

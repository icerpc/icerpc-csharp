// Copyright (c) ZeroC, Inc.

namespace IceRpc.Slice.Generators.Internal;

/// <summary>Represents an RPC operation annotated with the <c>IceRpc.Slice.SliceOperationAttribute</c> attribute.
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

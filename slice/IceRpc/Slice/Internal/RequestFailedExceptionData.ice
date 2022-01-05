// Copyright (c) ZeroC, Inc. All rights reserved.

[cs:internal]
module IceRpc::Slice::Internal
{
    /// The data carried by a RequestFailedException (ObjectNotExistException, FacetNotExistException or
    /// OperationNotExistException) when encoded with the 1.1 encoding.
    [cs:readonly]
    struct RequestFailedExceptionData
    {
        identity: Identity,
        facet: Facet,
        operation: string,
    }
}

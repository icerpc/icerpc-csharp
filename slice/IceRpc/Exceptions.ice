//
// Copyright (c) ZeroC, Inc. All rights reserved.
//

#pragma once

[[suppress-warning(reserved-identifier)]]

module IceRpc
{
    /// Represents the origin of a remote exception. With the Ice 2.0 encoding, all remote exceptions have an implicit
    /// origin data member set during marshaling. With the Ice 1.1 encoding, this origin data member is set for
    /// {@link ServiceNotFoundException} and {@link OperationNotFoundException}, and is otherwise set to an unknown
    /// (all empty) value.
    [cs:readonly] struct RemoteExceptionOrigin
    {
        /// The path of the target service.
        string path;

        /// The operation name.
        string operation;
    }

    /// The server could not find this service.
    exception ServiceNotFoundException
    {
    }

    /// The server found a service but this service does not implement the requested operation. This exception is
    /// typically thrown when a client with newer Slice definitions calls a server using older Slice definitions.
    exception OperationNotFoundException
    {
    }

    /// An unhandled exception is thrown when an operation implementation throws an exception not derived from
    /// RemoteException or when it throws a RemoteException with its convertToUnhandled flag set to true.
    /// With ice1, an UnhandledException is transmitted as an "UnknownLocalException" with just a string (the message)
    /// as its payload. When receiving any Unknown exception over ice1, the mapped exception is UnhandledException.
    exception UnhandledException
    {
    }

    /// A dispatch exception is thrown when an implementation error occured. This can occur for example if the
    /// response can't be sent because it's larger than the peer's incoming frame maximum size.
    exception DispatchException
    {
    }
}

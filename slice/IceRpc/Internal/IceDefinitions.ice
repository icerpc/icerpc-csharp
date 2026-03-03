// Copyright (c) ZeroC, Inc.

module IceRpc::Internal
{
    // These definitions help with the encoding of ice frames.

    /// Each ice frame has a type identified by this enumeration.
    enum IceFrameType
    {
        Request = 0,
        RequestBatch = 1,
        Reply = 2,
        ValidateConnection = 3,
        CloseConnection = 4,
    }

    /// The ice frame prologue.
    struct IcePrologue
    {
        byte magic1;
        byte magic2;
        byte magic3;
        byte magic4;
        byte protocolMajor;
        byte protocolMinor;
        byte encodingMajor;
        byte encodingMinor;
        IceFrameType frameType;
        byte compressionStatus;
        int frameSize;
    }

    /// Specifies the retry behavior of an invocation in case of a (potentially) recoverable error. OperationMode is
    /// sent with each ice request to allow the server to verify the assumptions made by the caller.
    enum OperationMode
    {
        /// An ordinary operation. The client runtime guarantees at-most-once semantics for such an operation.
        Normal,

        /// Equivalent to {@link #Idempotent}, but deprecated.
        ["deprecated:Use Idempotent instead."]
        Nonmutating,

        /// An idempotent operation. The client runtime does not guarantee at-most-once semantics for such an
        /// operation.
        Idempotent,
    }

    /// The payload of most request and response frames starts with an encapsulation header that specifies the size of
    /// the encapsulation and its encoding.
    struct EncapsulationHeader
    {
        int encapsulationSize;
        byte payloadEncodingMajor;
        byte payloadEncodingMinor;
    }

    /// The identity of a service.
    /// @remarks We named this internal struct IceIdentity to avoid confusion with Ice::Identity, the public version
    /// of this struct.
    struct IceIdentity
    {
        /// The name of the identity.
        string name;

        /// The category of the identity.
        string category;
    }

    /// The facet of a service. A sequence with 0 elements corresponds to the default, empty facet; a sequence with a
    /// single element corresponds to a non-empty facet; and a sequence with more than 1 element is invalid.
    sequence<string> Facet;

    /// Each ice request frame has:
    /// - a frame prologue, with the frame type and the overall frame size
    /// - a request ID
    /// - a request header (below)
    /// - a request payload, with encapsulationSize - 6 bytes
    struct IceRequestHeader
    {
        IceIdentity identity;
        Facet facet;
        string operation;
        OperationMode operationMode;
        // Manually encoded/decoded
        // Context context;
        // EncapsulationHeader encapsulationHeader;
    }

    /// The reply status of an ice response frame.
    /// Each ice response frame has:
    /// - a frame prologue, with the frame type and the overall frame size
    /// - a request ID
    /// - a reply status
    /// - when reply status is OK or UserException, an encapsulation header followed by a response payload, with
    /// encapsulationSize - 6 bytes
    enum ReplyStatus
    {
        /// A successful reply message.
        Ok = 0,

        /// A user exception reply message.
        UserException = 1,

        /// The target object does not exist.
        ObjectNotExistException = 2,

        /// The target object does not support the facet (treated like ObjectNotExistException by IceRPC).
        FacetNotExistException = 3,

        /// The target object does not support the operation.
        OperationNotExistException = 4,

        /// The dispatch failed with an Ice local exception (not applicable with IceRPC).
        UnknownLocalException = 5,

        /// The dispatch failed with a Slice user exception that does not conform to the exception specification of the
        /// operation (not applicable with IceRPC).
        UnknownUserException = 6,

        /// The dispatch failed with some other exception.
        UnknownException = 7,

        /// The dispatch failed because the request payload could not be decoded. This is typically due to a mismatch in the
        /// Slice definitions used by the client and the server.
        InvalidData = 8,

        /// The caller is not authorized to access the requested resource.
        Unauthorized = 9,
    }

    /// The data carried by a RequestFailedException (ObjectNotExistException, FacetNotExistException or
    /// OperationNotExistException).
    struct RequestFailedExceptionData
    {
        IceIdentity identity;
        Facet facet;
        string operation;
    }
}

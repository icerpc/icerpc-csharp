// Copyright (c) ZeroC, Inc. All rights reserved.

[cs:internal]
module IceRpc::Internal
{
    // These definitions help with the encoding of ice1 frames.

    /// Each ice1 frame has a type identified by this enumeration.
    enum Ice1FrameType : byte
    {
        Request = 0,
        RequestBatch = 1,
        Reply = 2,
        ValidateConnection = 3,
        CloseConnection = 4
    }

    /// Determines the retry behavior an invocation in case of a (potentially) recoverable error. OperationMode is
    /// sent with each ice1 request to allow the server to verify the assumptions made by the caller.
    enum OperationMode : byte
    {
        /// Ordinary operations have <code>Normal</code> mode. These operations can modify object state; invoking such
        /// an operation twice in a row may have different semantics than invoking it once. The Ice run time guarantees
        /// that it will not violate at-most-once semantics for <code>Normal</code> operations.
        Normal,

        /// <p class="Deprecated"><code>Nonmutating</code> is deprecated; use <code>Idempotent</code> instead.
        Nonmutating, // TODO: deprecated metadata for enumerator

        /// Operations that use the Slice <code>idempotent</code> keyword can modify object state, but invoking an
        /// operation twice in a row must result in the same object state as invoking it once. For example,
        /// <code>x = 1</code> is an idempotent statement, whereas <code>x += 1</code> is not. For idempotent
        /// operations, the Ice run-time does not guarantee at-most-once semantics.
        \Idempotent
    }

    /// The payload of most request and response frames starts with an encapsulation header that specifies the size of
    /// the encapsulation and its encoding.
    [cs:readonly]
    struct EncapsulationHeader
    {
        int encapsulationSize;
        byte payloadEncodingMajor;
        byte payloadEncodingMinor;
    }

    /// Each ice1 request frame has:
    /// - a frame prologue, with the frame type and the overall frame size
    /// - a request header (below)
    /// - a request payload, with encapsulationSize - 6 bytes
    [cs:readonly]
    struct Ice1RequestHeader
    {
        Slice::Internal::Identity identity;
        Slice::Internal::Facet facet;
        string operation;
        OperationMode operationMode;
        Context context;
        EncapsulationHeader encapsulationHeader;
    }

    /// The reply status of an ice1 response frame.
    /// Each ice1 response frame has:
    /// - a frame prologue, with the frame type and the overall frame size
    /// - a reply status
    /// - when reply status is OK or UserException, an encapsulation header followed by a response payload, with
    /// encapsulationSize - 6 bytes
    enum ReplyStatus : byte
    {
        /// A successful reply message.
        OK = 0,

        /// A user exception reply message.
        UserException = 1,

        /// The target object does not exist.
        ObjectNotExistException = 2,

        /// The target object does not support the facet.
        FacetNotExistException = 3,

        /// The target object does not support the operation.
        OperationNotExistException = 4,

        /// The reply message carries an unknown Ice local exception.
        UnknownLocalException = 5,

        /// The reply message carries an unknown Ice user exception.
        UnknownUserException = 6,

        /// The reply message carries an unknown exception.
        UnknownException = 7
    }
}

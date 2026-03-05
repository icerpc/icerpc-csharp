// Copyright (c) ZeroC, Inc.

#include <Ice/OperationMode.ice>

module IceRpc::Internal
{
    // These definitions help with the encoding of ice frames.

    /// Each ice frame has a type identified by this enumeration.
    ["cs:internal"]
    enum IceFrameType
    {
        Request = 0,
        RequestBatch = 1,
        Reply = 2,
        ValidateConnection = 3,
        CloseConnection = 4,
    }

    /// The ice frame prologue.
    ["cs:internal"]
    ["cs:readonly"]
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

    /// The payload of most request and response frames starts with an encapsulation header that specifies the size of
    /// the encapsulation and its encoding.
    ["cs:internal"]
    ["cs:readonly"]
    struct EncapsulationHeader
    {
        int encapsulationSize;
        byte payloadEncodingMajor;
        byte payloadEncodingMinor;
    }

    /// The identity of a service.
    /// @remarks We named this internal struct IceIdentity to avoid confusion with Ice::Identity, the public version
    /// of this struct.
    ["cs:internal"]
    ["cs:readonly"]
    struct IceIdentity
    {
        /// The name of the identity.
        string name;

        /// The category of the identity.
        string category;
    }

    /// The facet of a service. A sequence with 0 elements corresponds to the default, empty facet; a sequence with a
    /// single element corresponds to a non-empty facet; and a sequence with more than 1 element is invalid.
    ["cs:internal"]
    sequence<string> Facet;

    /// Each ice request frame has:
    /// - a frame prologue, with the frame type and the overall frame size
    /// - a request ID
    /// - a request header (below)
    /// - a request payload, with encapsulationSize - 6 bytes
    ["cs:internal"]
    ["cs:readonly"]
    struct IceRequestHeader
    {
        IceIdentity identity;
        Facet facet;
        string operation;
        Ice::OperationMode operationMode;
        // Manually encoded/decoded
        // Context context;
        // EncapsulationHeader encapsulationHeader;
    }

    /// The data carried by a RequestFailedException (ObjectNotExistException, FacetNotExistException or
    /// OperationNotExistException).
    ["cs:internal"]
    ["cs:readonly"]
    struct RequestFailedExceptionData
    {
        IceIdentity identity;
        Facet facet;
        string operation;
    }
}

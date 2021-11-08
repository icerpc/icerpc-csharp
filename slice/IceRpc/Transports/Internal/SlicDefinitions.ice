// Copyright (c) ZeroC, Inc. All rights reserved.

[[suppress-warning(reserved-identifier)]]

// These definitions help with the encoding of Slic frames.
// TODO: use generated internal types once supported
module IceRpc::Transports::Internal
{
    /// The Slic frame types
    enum FrameType : byte
    {
        /// The initialization frame is sent by the client-side Slic connection on connection
        /// establishement.
        Initialize = 1,
        /// The initialize acknowledgment is sent by the server-side Slic connection after receiving
        /// an initialize frame.
        InitializeAck,
        /// The Slic versions supported by the server if the version from the Initialize frame is
        /// not supported.
        Version,
        /// The stream frame is sent send data over a Slic stream.
        Stream,
        /// The last stream frame is the last frame used to send data over a Slic stream.
        StreamLast,
        /// The stream reset frame terminates the stream.
        StreamReset,
        /// The stream consumed frame is sent to notifiy the peer that data has been consumed. The
        /// peer can send additional data after receiving this frame.
        StreamConsumed,
        /// The stream stop sending frame is sent to notify the peer it should stop sending data.
        StreamStopSending
    }

    /// The keys for supported Slic connection parameters exchanged with the Initialize and
    /// InitializeAck frames.
    unchecked enum ParameterKey : int
    {
        /// The maximum number of bidirectional streams. The peer shouldn't open more streams
        /// than the maximum defined by this parameter.
        MaxBidirectionalStreams = 0,
        /// The maximum number of unidirectional streams. The peer shouldn't open more streams
        /// than the maximum defined by this parameter.
        MaxUnidirectionalStreams = 1,
        /// The idle timeout. If the connection is inactive for longer than the idle timeout
        /// it will be closed.
        IdleTimeout = 2,
        /// The maximum Slic packet size.
        PacketMaxSize = 3,
        /// The maximum size of the buffer used for streaming data. This is used when flow
        /// control is enabled to limit the amount of data that can be received. The sender
        /// shouldn't send more data than this maximum size. It needs to wait to receive
        /// a stream consumed frame to send additional data.
        StreamBufferMaxSize = 4,
    }

    typealias ParameterFields = dictionary<varint, sequence<byte>>;

    /// The Slic initialize frame body.
    [cs:readonly]
    struct InitializeBody
    {
        /// The application protocol name.
        string applicationProtocolName;

        /// The parameters.
        ParameterFields parameters;
    }

    /// The Slic initialize acknowledgment frame body.
    [cs:readonly]
    struct InitializeAckBody
    {
        /// The parameters.
        ParameterFields parameters;
    }

    /// The body of a Slic version frame. This frame is sent in response to an initialize frame if the Slic version
    /// from the initialize frame is not supported by the receiver. Upon receiving the Version frame the receiver
    /// should send back a new Initialize frame with a version matching one of the versions provided by the Version
    /// frame body.
    [cs:readonly]
    struct VersionBody
    {
        /// The supported Slic versions.
        sequence<varuint> versions;
    }

    /// The body of the Stream reset frame. This frame is sent to notify the peer that sender is no longer
    /// interested in the stream. The error code is application protocol specific.
    [cs:readonly]
    struct StreamResetBody
    {
        /// The application protocol error code indicating the reason of the reset.
        varulong applicationProtocolErrorCode;
    }

    /// The body of the Stream consumed frame. This frame is sent to notify the peer that the receiver
    /// consumed some data from the stream.
    [cs:readonly]
    struct StreamConsumedBody
    {
        /// The size of the consumed data.
        varulong size;
    }

    /// The body of the Stream stop sending frame. This frame is sent to notify the peer that the receiver
    /// is no longer interested in receiving data.
    [cs:readonly]
    struct StreamStopSendingBody
    {
        /// The application protocol error code indicating the reason why the peer no longer needs to receive data.
        varulong applicationProtocolErrorCode;
    }
}

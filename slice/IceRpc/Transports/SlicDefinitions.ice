// Copyright (c) ZeroC, Inc. All rights reserved.

#pragma once

[[suppress-warning(reserved-identifier)]]

#include <IceRpc/BuiltinSequences.ice>

// These definitions help with the encoding of Slic frames.
module IceRpc::Transports::Slic
{
    /// The Slic frame types
    enum FrameType : byte
    {
        Initialize = 1,
        InitializeAck,
        Version,
        Stream,
        StreamLast,
        StreamReset,
        StreamConsumed,
        StreamStopSending
    }

    /// The keys for supported Slic connection parameters.
    unchecked enum ParameterKey : int
    {
        MaxBidirectionalStreams = 0,
        MaxUnidirectionalStreams = 1,
        IdleTimeout = 2,
        PacketMaxSize = 3,
        StreamBufferMaxSize = 4,
    }

    dictionary<varint, ByteSeq> ParameterFields;

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

    sequence<varuint> VersionSeq;

    /// The body of a Slic version frame. This frame is sent in response to an initialize frame if the Slic version
    /// from the initialize frame is not supported by the receiver. Upon receiving the Version frame the receiver
    /// should send back a new Initialize frame with a version matching one of the versions provided by the Version
    /// frame body.
    [cs:readonly]
    struct VersionBody
    {
        /// The supported Slic versions.
        VersionSeq versions;
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

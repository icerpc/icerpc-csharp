// Copyright (c) ZeroC, Inc. All rights reserved.

#pragma once

[[suppress-warning(reserved-identifier)]]

// These definitions help with the encoding of Slic frames.
module IceRpc::Slic
{
    /// The keys for supported Slic connection parameters.
    unchecked enum ParameterKey : int
    {
        MaxBidirectionalStreams = 0,
        MaxUnidirectionalStreams = 1,
        IdleTimeout = 2,
        PacketMaxSize = 3,
        StreamBufferMaxSize = 4,
    }

    /// The header of the Slic initialize frame body. This header is followed by connection parameters encoded as
    /// Fields.
    [cs:readonly]
    struct InitializeHeaderBody
    {
        /// The application protocol name.
        string applicationProtocolName;
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

    /// The body of the close frame. This frame is sent to notify the peer that the sender will no longer be
    /// sending any data. The error code is application protocol specific.
    [cs:readonly]
    struct CloseBody
    {
        /// The application protocol error code indicating the reason of the reset.
        varulong applicationProtocolErrorCode;
    }
}

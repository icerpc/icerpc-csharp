// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc;

/// <summary>This enumeration contains event ID constants used for protocol logging.</summary>
// TODO: split into IceEventId and IceRpcEventId
// The protocol-neutral event IDs are in ConnectionEventId.
public enum ProtocolEventId
{
    /// <summary>Received an ice close connection frame.</summary>
    ReceivedIceCloseConnectionFrame = Internal.BaseEventId.Protocol,

    /// <summary>Received an ice request batch frame.</summary>
    ReceivedIceRequestBatchFrame,

    /// <summary>Received an ice validate connection frame.</summary>
    ReceivedIceValidateConnectionFrame,

    /// <summary>Received an icerpc go away frame.</summary>
    ReceivedGoAwayFrame,

    /// <summary>An ice validate connection frame was sent.</summary>
    SentIceValidateConnectionFrame,

    /// <summary>An ice close connection frame was sent.</summary>
    SentIceCloseConnectionFrame,

    /// <summary>An icerpc go away frame was sent.</summary>
    SentGoAwayFrame,
}

// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc
{
    /// <summary>This class contains event ID constants used for protocol logging.</summary>
    // TODO: split into Ice1EventIds and Ice2EventIds
    // The protocol-neutral event IDs are in ConnectionEventIds.
    public enum ProtocolEventIds
    {
        /// <summary>A datagram connection received a close connection frame.</summary>
        DatagramConnectionReceiveCloseConnectionFrame = Internal.BaseEventIds.Protocol,
        /// <summary>A datagram message that exceeded the <see
        /// cref="Configure.ConnectionOptions.IncomingFrameMaxSize"/> was received.</summary>
        DatagramSizeExceededIncomingFrameMaxSize,
        /// <summary>A datagram message that exceeded the maximum datagram size was received.</summary>
        DatagramMaximumSizeExceeded,
        /// <summary>Received an ice close connection frame.</summary>
        ReceivedIce1CloseConnectionFrame,
        /// <summary>Received an ice request batch frame.</summary>
        ReceivedIce1RequestBatchFrame,
        /// <summary>Received an ice validate connection frame.</summary>
        ReceivedIce1ValidateConnectionFrame,
        /// <summary>Received an icerpc go away frame.</summary>
        ReceivedGoAwayFrame,
        /// <summary>Received an icerpc initialize frame.</summary>
        ReceivedInitializeFrame,

        /// <summary>Received an invalid datagram message (ice).</summary>
        ReceivedInvalidDatagram,

        /// <summary>An ice validate connection frame was sent.</summary>
        SentIce1ValidateConnectionFrame,
        /// <summary>An ice close connection frame was sent.</summary>
        SentIce1CloseConnectionFrame,
        /// <summary>An icerpc go away frame was sent.</summary>
        SentGoAwayFrame,
        /// <summary>An icerpc initialize frame was sent.</summary>
        SentInitializeFrame,
    }
}

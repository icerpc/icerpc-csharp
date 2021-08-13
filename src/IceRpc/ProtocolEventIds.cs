// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc
{
    /// <summary>This class contains event ID constants used for protocol logging.</summary>
    public enum ProtocolEventIds
    {
        /// <summary>A datagram connection received a close connection frame.</summary>
        DatagramConnectionReceiveCloseConnectionFrame = Internal.BaseEventIds.Protocol,
        /// <summary>A datagram message that exceeded the <see cref="ConnectionOptions.IncomingFrameMaxSize"/> was
        /// received.</summary>
        DatagramSizeExceededIncomingFrameMaxSize,
        /// <summary>A datagram message that exceeded the maximum datagram size was received.</summary>
        DatagramMaximumSizeExceeded,
        /// <summary>Received an ice1 close connection frame.</summary>
        ReceivedIce1CloseConnectionFrame,
        /// <summary>Received an ice1 request batch frame.</summary>
        ReceivedIce1RequestBatchFrame,
        /// <summary>Received an ice1 validate connection frame.</summary>
        ReceivedIce1ValidateConnectionFrame,
        /// <summary>Received an ice2 go away frame.</summary>
        ReceivedGoAwayFrame,
        /// <summary>Received an ice2 go away canceled frame.</summary>
        ReceivedGoAwayCanceledFrame,
        /// <summary>Received an ice2 initialize frame.</summary>
        ReceivedInitializeFrame,
        /// <summary>Received a request frame.</summary>
        ReceivedRequestFrame,
        /// <summary>Received a response frame.</summary>
        ReceivedResponseFrame,
        /// <summary>An ice1 validate connection frame was sent.</summary>
        SentIce1ValidateConnectionFrame,
        /// <summary>An ice1 close connection frame was sent.</summary>
        SentIce1CloseConnectionFrame,
        /// <summary>An ice2 go away frame was sent.</summary>
        SentGoAwayFrame,
        /// <summary>An ice2 go away canceled frame was sent.</summary>
        SentGoAwayCanceledFrame,
        /// <summary>An ice2 initialize frame was sent.</summary>
        SentInitializeFrame,
        /// <summary>A request frame was sent.</summary>
        SentRequestFrame,
        /// <summary>A response frame was sent.</summary>
        SentResponseFrame
    }
}

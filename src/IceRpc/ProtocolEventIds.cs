// Copyright (c) ZeroC, Inc. All rights reserved.

using Microsoft.Extensions.Logging;

namespace IceRpc
{
    /// <summary>This class contains event ID constants used for protocol logging.</summary>
    public static class ProtocolEventIds
    {
        /// <summary>A datagram connection received a close connection frame.</summary>
        public static readonly EventId DatagramConnectionReceiveCloseConnectionFrame =
            GetEventId(ProtocolEvent.DatagramConnectionReceiveCloseConnectionFrame);
        
        /// <summary>A datagram message that exceeded the <see cref="ConnectionOptions.IncomingFrameMaxSize"/> was
        /// received.</summary>
        public static readonly EventId DatagramSizeExceededIncomingFrameMaxSize =
            GetEventId(ProtocolEvent.DatagramSizeExceededIncomingFrameMaxSize);

        /// <summary>A datagram message that exceeded the maximum datagram size was received.</summary>
        public static readonly EventId DatagramMaximumSizeExceeded =
            GetEventId(ProtocolEvent.DatagramMaximumSizeExceeded);

        /// <summary>Received an ice1 close connection frame.</summary>
        public static readonly EventId ReceivedIce1CloseConnectionFrame =
            GetEventId(ProtocolEvent.ReceivedIce1CloseConnectionFrame);

        /// <summary>Received an ice1 request batch frame.</summary>
        public static readonly EventId ReceivedIce1RequestBatchFrame =
            GetEventId(ProtocolEvent.ReceivedIce1RequestBatchFrame);

        /// <summary>Received an ice1 validate connection frame.</summary>
        public static readonly EventId ReceivedIce1ValidateConnectionFrame =
            GetEventId(ProtocolEvent.ReceivedIce1ValidateConnectionFrame);

        /// <summary>Received an ice2 go away frame.</summary>
        public static readonly EventId ReceivedGoAwayFrame = GetEventId(ProtocolEvent.ReceivedGoAwayFrame);

        /// <summary>Received an ice2 initialize frame.</summary>
        public static readonly EventId ReceivedInitializeFrame =
            GetEventId(ProtocolEvent.ReceivedInitializeFrame);

        /// <summary>Received a request frame.</summary>
        public static readonly EventId ReceivedRequestFrame = GetEventId(ProtocolEvent.ReceivedRequestFrame);

        /// <summary>Received a response frame.</summary>
        public static readonly EventId ReceivedResponseFrame = GetEventId(ProtocolEvent.ReceivedResponseFrame);

        /// <summary>A request failed with an exception.</summary>
        public static readonly EventId RequestException = GetEventId(ProtocolEvent.RequestException);

        /// <summary>A request will be retry because of a retryable exception.</summary>
        public static readonly EventId RetryRequestRetryableException =
            GetEventId(ProtocolEvent.RetryRequestRetryableException);

        /// <summary>A connection establishment will be retry because of a retryable exception.</summary>
        public static readonly EventId RetryRequestConnectionException =
            GetEventId(ProtocolEvent.RetryRequestConnectionException);

        /// <summary>An ice1 validate connection frame was sent.</summary>
        public static readonly EventId SentIce1ValidateConnectionFrame =
            GetEventId(ProtocolEvent.SentIce1ValidateConnectionFrame);

        /// <summary>An ice1 close connection frame was sent.</summary>
        public static readonly EventId SentIce1CloseConnectionFrame =
            GetEventId(ProtocolEvent.SentIce1CloseConnectionFrame);

        /// <summary>An ice2 go away frame was sent.</summary>
        public static readonly EventId SentGoAwayFrame = GetEventId(ProtocolEvent.SentGoAwayFrame);

        /// <summary>An ice2 initialize frame was sent.</summary>
        public static readonly EventId SentInitializeFrame = GetEventId(ProtocolEvent.SentInitializeFrame);

        /// <summary>A request frame was sent.</summary>
        public static readonly EventId SentRequestFrame = GetEventId(ProtocolEvent.SentRequestFrame);

        /// <summary>A response frame was sent.</summary>
        public static readonly EventId SentResponseFrame = GetEventId(ProtocolEvent.SentResponseFrame);

        private const int BaseEventId = Internal.LoggerExtensions.ProtocolBaseEventId;

        private enum ProtocolEvent
        {
            DatagramConnectionReceiveCloseConnectionFrame = BaseEventId,
            DatagramSizeExceededIncomingFrameMaxSize,
            DatagramMaximumSizeExceeded,
            ReceivedIce1CloseConnectionFrame,
            ReceivedIce1RequestBatchFrame,
            ReceivedIce1ValidateConnectionFrame,
            ReceivedGoAwayFrame,
            ReceivedInitializeFrame,
            ReceivedRequestFrame,
            ReceivedResponseFrame,
            RequestException,
            RetryRequestRetryableException,
            RetryRequestConnectionException,
            SentIce1ValidateConnectionFrame,
            SentIce1CloseConnectionFrame,
            SentGoAwayFrame,
            SentInitializeFrame,
            SentRequestFrame,
            SentResponseFrame
        }

        private static EventId GetEventId(ProtocolEvent e) => new((int)e, e.ToString());
    }
}

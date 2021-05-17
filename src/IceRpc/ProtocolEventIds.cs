// Copyright (c) ZeroC, Inc. All rights reserved.

using Microsoft.Extensions.Logging;

namespace IceRpc
{
    /// <summary>This class contains event ID constants used for protocol logging.</summary>
    public static class ProtocolEventIds
    {
        public static readonly EventId DatagramConnectionReceiveCloseConnectionFrame =
            GetEventId(ProtocolEvent.DatagramConnectionReceiveCloseConnectionFrame);
        public static readonly EventId DatagramSizeExceededIncomingFrameMaxSize =
            GetEventId(ProtocolEvent.DatagramSizeExceededIncomingFrameMaxSize);
        public static readonly EventId DatagramMaximumSizeExceeded =
            GetEventId(ProtocolEvent.DatagramMaximumSizeExceeded);

        public static readonly EventId ReceivedIce1CloseConnectionFrame =
            GetEventId(ProtocolEvent.ReceivedIce1CloseConnectionFrame);
        public static readonly EventId ReceivedIce1RequestBatchFrame =
            GetEventId(ProtocolEvent.ReceivedIce1RequestBatchFrame);
        public static readonly EventId ReceivedIce1ValidateConnectionFrame =
            GetEventId(ProtocolEvent.ReceivedIce1ValidateConnectionFrame);

        public static readonly EventId ReceivedGoAwayFrame = GetEventId(ProtocolEvent.ReceivedGoAwayFrame);
        public static readonly EventId ReceivedInitializeFrame =
            GetEventId(ProtocolEvent.ReceivedInitializeFrame);
        public static readonly EventId ReceivedRequestFrame = GetEventId(ProtocolEvent.ReceivedRequestFrame);
        public static readonly EventId ReceivedResponseFrame = GetEventId(ProtocolEvent.ReceivedResponseFrame);
        public static readonly EventId RequestException = GetEventId(ProtocolEvent.RequestException);
        public static readonly EventId RetryRequestRetryableException =
            GetEventId(ProtocolEvent.RetryRequestRetryableException);
        public static readonly EventId RetryRequestConnectionException =
            GetEventId(ProtocolEvent.RetryRequestConnectionException);

        public static readonly EventId SentIce1ValidateConnectionFrame =
            GetEventId(ProtocolEvent.SentIce1ValidateConnectionFrame);
        public static readonly EventId SentIce1CloseConnectionFrame =
            GetEventId(ProtocolEvent.SentIce1CloseConnectionFrame);
        public static readonly EventId SentGoAwayFrame = GetEventId(ProtocolEvent.SentGoAwayFrame);
        public static readonly EventId SentInitializeFrame = GetEventId(ProtocolEvent.SentInitializeFrame);
        public static readonly EventId SentRequestFrame = GetEventId(ProtocolEvent.SentRequestFrame);
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

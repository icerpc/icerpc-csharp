// Copyright (c) ZeroC, Inc. All rights reserved.

using Microsoft.Extensions.Logging;

namespace IceRpc
{
    /// <summary>This class contains event ID constants used for protocol logging.</summary>
    public static class ProtocolEventIds
    {
        public static readonly EventId DatagramConnectionReceiveCloseConnectionFrame =
            new(BaseEventId + 0, nameof(DatagramConnectionReceiveCloseConnectionFrame));
        public static readonly EventId DatagramSizeExceededIncomingFrameMaxSize =
            new(BaseEventId + 1, nameof(DatagramSizeExceededIncomingFrameMaxSize));
        public static readonly EventId DatagramMaximumSizeExceeded =
            new(BaseEventId + 2, nameof(DatagramMaximumSizeExceeded));

        public static readonly EventId ReceivedIce1CloseConnectionFrame =
            new(BaseEventId + 3, nameof(ReceivedIce1CloseConnectionFrame));
        public static readonly EventId ReceivedIce1RequestBatchFrame =
            new(BaseEventId + 4, nameof(ReceivedIce1RequestBatchFrame));
        public static readonly EventId ReceivedIce1ValidateConnectionFrame =
            new(BaseEventId + 5, nameof(ReceivedIce1ValidateConnectionFrame));

        public static readonly EventId ReceivedGoAwayFrame = new(BaseEventId + 6, nameof(ReceivedGoAwayFrame));
        public static readonly EventId ReceivedInitializeFrame =
            new(BaseEventId + 7, nameof(ReceivedInitializeFrame));
        public static readonly EventId ReceivedRequestFrame = new(BaseEventId + 8, nameof(ReceivedRequestFrame));
        public static readonly EventId ReceivedResponseFrame = new(BaseEventId + 9, nameof(ReceivedResponseFrame));
        public static readonly EventId RequestException = new(BaseEventId + 10, nameof(RequestException));
        public static readonly EventId RetryRequestRetryableException =
            new(BaseEventId + 11, nameof(RetryRequestRetryableException));
        public static readonly EventId RetryRequestConnectionException =
            new(BaseEventId + 12, nameof(RetryRequestConnectionException));

        public static readonly EventId SentIce1ValidateConnectionFrame =
            new(BaseEventId + 13, nameof(SentIce1ValidateConnectionFrame));
        public static readonly EventId SentIce1CloseConnectionFrame =
            new(BaseEventId + 14, nameof(SentIce1CloseConnectionFrame));
        public static readonly EventId SentGoAwayFrame = new(BaseEventId + 15, nameof(SentGoAwayFrame));
        public static readonly EventId SentInitializeFrame = new(BaseEventId + 16, nameof(SentInitializeFrame));
        public static readonly EventId SentRequestFrame = new(BaseEventId + 17, nameof(SentRequestFrame));
        public static readonly EventId SentResponseFrame = new(BaseEventId + 18, nameof(SentResponseFrame));

        private const int BaseEventId = Internal.LoggerExtensions.ProtocolBaseEventId;
    }
}

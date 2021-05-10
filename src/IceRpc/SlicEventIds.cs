// Copyright (c) ZeroC, Inc. All rights reserved.

using Microsoft.Extensions.Logging;

namespace IceRpc
{
    /// <summary>This class contains ILogger extensions methods for logging Slic transport messages.</summary>
    public static class SlicEventIds
    {
        public static readonly EventId ReceivedFrame = new(BaseEventId + 0, nameof(ReceivedFrame));
        public static readonly EventId ReceivedInitializeFrame = new(BaseEventId + 1, nameof(ReceivedInitializeFrame));
        public static readonly EventId ReceivedInitializeAckFrame =
            new(BaseEventId + 2, nameof(ReceivedInitializeAckFrame));
        public static readonly EventId ReceivedResetFrame = new(BaseEventId + 3, nameof(ReceivedResetFrame));
        public static readonly EventId ReceivedVersionFrame = new(BaseEventId + 4, nameof(ReceivedVersionFrame));
        public static readonly EventId SentFrame = new(BaseEventId + 5, nameof(SentFrame));
        public static readonly EventId SentInitializeFrame = new(BaseEventId + 6, nameof(SentInitializeFrame));
        public static readonly EventId SentInitializeAckFrame = new(BaseEventId + 7, nameof(SentInitializeAckFrame));
        public static readonly EventId SentResetFrame = new(BaseEventId + 8, nameof(SentResetFrame));
        public static readonly EventId SentVersionFrame = new(BaseEventId + 9, nameof(SentVersionFrame));

        private const int BaseEventId = Internal.LoggerExtensions.SlicBaseEventId;
    }
}

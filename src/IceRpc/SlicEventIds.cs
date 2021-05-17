// Copyright (c) ZeroC, Inc. All rights reserved.

using Microsoft.Extensions.Logging;

namespace IceRpc
{
    /// <summary>This class contains event ID constants used for Slic transport logging.</summary>
    public static class SlicEventIds
    {
        public static readonly EventId ReceivedFrame = GetEventId(SlicEvent.ReceivedFrame);
        public static readonly EventId ReceivedInitializeFrame = GetEventId(SlicEvent.ReceivedInitializeFrame);
        public static readonly EventId ReceivedInitializeAckFrame = GetEventId(SlicEvent.ReceivedInitializeAckFrame);
        public static readonly EventId ReceivedResetFrame = GetEventId(SlicEvent.ReceivedResetFrame);
        public static readonly EventId ReceivedVersionFrame = GetEventId(SlicEvent.ReceivedVersionFrame);
        public static readonly EventId SentFrame = GetEventId(SlicEvent.SentFrame);
        public static readonly EventId SentInitializeFrame = GetEventId(SlicEvent.SentInitializeFrame);
        public static readonly EventId SentInitializeAckFrame = GetEventId(SlicEvent.SentInitializeAckFrame);
        public static readonly EventId SentResetFrame = GetEventId(SlicEvent.SentResetFrame);
        public static readonly EventId SentVersionFrame = GetEventId(SlicEvent.SentVersionFrame);

        private const int BaseEventId = Internal.LoggerExtensions.SlicBaseEventId;

        private enum SlicEvent
        {
            ReceivedFrame = BaseEventId,
            ReceivedInitializeFrame,
            ReceivedInitializeAckFrame,
            ReceivedResetFrame,
            ReceivedVersionFrame,
            SentFrame,
            SentInitializeFrame,
            SentInitializeAckFrame,
            SentResetFrame,
            SentVersionFrame
        }

        private static EventId GetEventId(SlicEvent e) => new((int)e, e.ToString());
    }
}

// Copyright (c) ZeroC, Inc. All rights reserved.

using Microsoft.Extensions.Logging;

namespace IceRpc.Transports
{
    /// <summary>This class contains event ID constants used for Slic transport logging.</summary>
    public static class SlicEventIds
    {
        /// <summary>Slic frame received.</summary>
        public static readonly EventId ReceivedFrame = GetEventId(SlicEvent.ReceivedFrame);

        /// <summary>Slic initialize frame received.</summary>
        public static readonly EventId ReceivedInitializeFrame = GetEventId(SlicEvent.ReceivedInitializeFrame);

        /// <summary>Slice initialize ack frame received.</summary>
        public static readonly EventId ReceivedInitializeAckFrame = GetEventId(SlicEvent.ReceivedInitializeAckFrame);

        /// <summary>Slic reset frame received.</summary>
        public static readonly EventId ReceivedResetFrame = GetEventId(SlicEvent.ReceivedResetFrame);

        /// <summary>Slic version frame received.</summary>
        public static readonly EventId ReceivedVersionFrame = GetEventId(SlicEvent.ReceivedVersionFrame);

        /// <summary>Slice frame sent.</summary>
        public static readonly EventId SentFrame = GetEventId(SlicEvent.SentFrame);

        /// <summary>Slic initialize frame sent.</summary>
        public static readonly EventId SentInitializeFrame = GetEventId(SlicEvent.SentInitializeFrame);

        /// <summary>Slic initialize ack frame sent.</summary>
        public static readonly EventId SentInitializeAckFrame = GetEventId(SlicEvent.SentInitializeAckFrame);

        /// <summary>Slic reset frame sent.</summary>
        public static readonly EventId SentResetFrame = GetEventId(SlicEvent.SentResetFrame);

        /// <summary>Slic version frame sent.</summary>
        public static readonly EventId SentVersionFrame = GetEventId(SlicEvent.SentVersionFrame);

        private const int BaseEventId = IceRpc.Internal.LoggerExtensions.SlicBaseEventId;

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

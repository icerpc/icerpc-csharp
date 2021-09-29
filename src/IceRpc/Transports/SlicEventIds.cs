// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc.Transports
{
    /// <summary>This enum contains event ID constants used for Slic transport logging.</summary>
    public enum SlicEventIds
    {
        /// <summary>Received Slic stream consumed frame.</summary>
        ReceivedConsumedFrame,
        /// <summary>Receiving Slic initialize frame.</summary>
        ReceivedInitializeFrame,
        /// <summary>Receiving Slic initialize ack frame.</summary>
        ReceivedInitializeAckFrame,
        /// <summary>Received Slic stream reset frame.</summary>
        ReceivedResetFrame = IceRpc.Internal.BaseEventIds.Slic,
        /// <summary>Received Slic stream stop sending frame.</summary>
        ReceivedStopSendingFrame,
        /// <summary>Received Slic Initialize frame with unsupported version.</summary>
        ReceivedUnsupportedInitializeFrame,
        /// <summary>Receiving Slic version frame.</summary>
        ReceivedVersionFrame,
        /// <summary>Receiving Slic stream data frame.</summary>
        ReceivingStreamFrame,
        /// <summary>Sending Slic frame.</summary>
        SendingStreamFrame,
        /// <summary>Sending Slic frame failed.</summary>
        SendFailure,
        /// <summary>Sent Slic stream consumed frame.</summary>
        SentConsumedFrame,
        /// <summary>Sent Slic initialize frame.</summary>
        SentInitializeFrame,
        /// <summary>Sent Slic initialize ack frame.</summary>
        SentInitializeAckFrame,
        /// <summary>Sent Slic reset frame.</summary>
        SentResetFrame,
        /// <summary>Sent Slic stop sending frame.</summary>
        SentStopSendingFrame,
        /// <summary>Sent Slic version frame.</summary>
        SentVersionFrame
    }
}

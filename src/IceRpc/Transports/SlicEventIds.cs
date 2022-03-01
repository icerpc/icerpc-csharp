// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc.Transports
{
    /// <summary>This enum contains event ID constants used for Slic transport logging.</summary>
    public enum SlicEventIds
    {
        /// <summary>Receiving Slic initialize frame.</summary>
        ReceivedInitializeFrame,
        /// <summary>Receiving Slic initialize ack frame.</summary>
        ReceivedInitializeAckFrame,
        /// <summary>Receiving Slic stream data frame.</summary>
        ReceivingStreamFrame,
        /// <summary>Received Slic stream consume frame.</summary>
        ReceivedStreamConsumedFrame,
        /// <summary>Received Slic stream reset frame.</summary>
        ReceivedStreamResetFrame = IceRpc.Internal.BaseEventIds.Slic,
        /// <summary>Received Slic stream stop sending frame.</summary>
        ReceivedStreamStopSendingFrame,
        /// <summary>Received Slic unidirectional stream released frame.</summary>
        ReceivedUnidirectionalStreamReleased,
        /// <summary>Received Slic Initialize frame with unsupported version.</summary>
        ReceivedUnsupportedInitializeFrame,
        /// <summary>Receiving Slic version frame.</summary>
        ReceivedVersionFrame,
        /// <summary>Sending Slic frame failed.</summary>
        SendFailure,
        /// <summary>Sent Slic initialize frame.</summary>
        SentInitializeFrame,
        /// <summary>Sent Slic initialize ack frame.</summary>
        SentInitializeAckFrame,
        /// <summary>Sent Slic stream consumed frame.</summary>
        SentStreamConsumedFrame,
        /// <summary>Sent Slic reset frame.</summary>
        SentStreamResetFrame,
        /// <summary>Sent Slic stream frame.</summary>
        SentStreamFrame,
        /// <summary>Sent Slic stream stop sending frame.</summary>
        SentStreamStopSendingFrame,
        /// <summary>Sent Slic unidirectional stream released frame.</summary>
        SentUnidirectionalStreamReleased,
        /// <summary>Sent Slic version frame.</summary>
        SentVersionFrame
    }
}

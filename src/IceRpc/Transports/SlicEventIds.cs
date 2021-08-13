// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc.Transports
{
    /// <summary>This enum contains event ID constants used for Slic transport logging.</summary>
    public enum SlicEventIds
    {
        /// <summary>Received Slic reset frame.</summary>
        ReceivedResetFrame = IceRpc.Internal.BaseEventIds.Slic,
        /// <summary>Received Slic stop sending frame.</summary>
        ReceivedStopSendingFrame,
        /// <summary>Received Slic Initialize frame with unsupported version.</summary>
        ReceivedUnsupportedInitializeFrame,
        /// <summary>Receiving Slic frame.</summary>
        ReceivingFrame,
        /// <summary>Receiving Slic initialize frame.</summary>
        ReceivingInitializeFrame,
        /// <summary>Receiving Slic initialize ack frame.</summary>
        ReceivingInitializeAckFrame,
        /// <summary>Receiving Slic version frame.</summary>
        ReceivingVersionFrame,
        /// <summary>Sending Slic frame.</summary>
        SendingFrame,
        /// <summary>Sending Slic initialize frame.</summary>
        SendingInitializeFrame,
        /// <summary>Sending Slic initialize ack frame.</summary>
        SendingInitializeAckFrame,
        /// <summary>Sending Slic reset frame.</summary>
        SendingResetFrame,
        /// <summary>Sending Slic stop sending frame.</summary>
        SendingStopSendingFrame,
        /// <summary>Sending Slic version frame.</summary>
        SendingVersionFrame
    }
}

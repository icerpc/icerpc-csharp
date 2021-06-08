// Copyright (c) ZeroC, Inc. All rights reserved.

using Microsoft.Extensions.Logging;

namespace IceRpc
{
    /// <summary>This enum contains event ID constants used for Slic transport logging.</summary>
    public enum SlicEvent
    {
        /// <summary>Slic frame received.</summary>
        ReceivedFrame = Internal.LoggerExtensions.SlicBaseEventId,
        /// <summary>Slic initialize frame received.</summary>
        ReceivedInitializeFrame,
        /// <summary>Slic initialize ack frame received.</summary>
        ReceivedInitializeAckFrame,
        /// <summary>Slic reset frame received.</summary>
        ReceivedResetFrame,
        /// <summary>Received Slic Initialize frame with unsupported version.</summary>
        ReceivedUnsupportedInitializeFrame,
        /// <summary>Slic version frame received.</summary>
        ReceivedVersionFrame,
        /// <summary>Slice frame sent.</summary>
        SentFrame,
        /// <summary>Slic initialize frame sent.</summary>
        SentInitializeFrame,
        /// <summary>Slic initialize ack frame sent.</summary>
        SentInitializeAckFrame,
        /// <summary>Slic reset frame sent.</summary>
        SentResetFrame,
        /// <summary>Slic version frame sent.</summary>
        SentVersionFrame
    }
}

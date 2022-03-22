// Copyright (c) ZeroC, Inc. All rights reserved.

using System.IO.Pipelines;

namespace IceRpc.Transports
{
    /// <summary>A multiplexed stream enables byte data exchange over a multiplexed transport.</summary>
    public interface IMultiplexedStream : IDuplexPipe
    {
        /// <summary>The stream ID.</summary>
        /// <exception cref="InvalidOperationException">Raised if the stream is not started. Local streams are not
        /// started until data is written. A remote stream is always started.</exception>
        long Id { get; }

        /// <summary>Returns <c>true</c> if the stream is a bidirectional stream, <c>false</c> otherwise.</summary>
        bool IsBidirectional { get; }

        /// <summary>Returns <c>true</c> if the local stream is started, <c>false</c> otherwise.</summary>
        bool IsStarted { get; }

        /// <summary>Sets the action which is called when the stream is reset.</summary>
        Action? ShutdownAction { get; set; }

        /// <summary>Sets the action which is called when writes complete.</summary>
        Action? WriteCompletionAction { get; set; }
    }
}

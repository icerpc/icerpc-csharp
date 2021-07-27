// Copyright (c) ZeroC, Inc. All rights reserved.

using System;

namespace IceRpc.Transports
{
    /// <summary>This exception reports that the provided transport is unknown or unregistered.</summary>
    public class UnknownTransportException : NotSupportedException
    {
        /// <summary>Constructs a new instance of the <see cref="UnknownTransportException"/> class.</summary>
        /// <param name="transportName">The name of the transport.</param>
        public UnknownTransportException(string transportName)
            : base($"unknown transport '{transportName}'")
        {
        }
    }
}

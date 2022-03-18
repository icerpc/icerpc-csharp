// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using System.IO.Pipelines;

namespace IceRpc
{
    /// <summary>Base class for outgoing frames.</summary>
    public abstract class OutgoingFrame
    {
        /// <summary>Gets or sets the payload sink of this frame.</summary>
        public abstract PipeWriter PayloadSink { get; set; }

        /// <summary>Gets or sets the payload source of this frame. The payload source is sent together with the frame
        /// header and the sending operation awaits until the payload source is fully sent.</summary>
        /// <value>The payload source of this frame. The default is an empty pipe reader.</value>
        public PipeReader PayloadSource { get; set; } = EmptyPipeReader.Instance;

        /// <summary>Gets or sets the payload source stream of this frame. The payload source stream (if specified) is
        /// sent after the payload source. It's sent in the background: the sending operation does not await it.
        /// </summary>
        public PipeReader? PayloadSourceStream { get; set; }

        /// <summary>Returns the Ice protocol of this frame.</summary>
        public Protocol Protocol { get; }

        /// <summary>Constructs an outgoing frame.</summary>
        /// <param name="protocol">The protocol used to send the frame.</param>
        protected OutgoingFrame(Protocol protocol)
        {
            if (!protocol.IsSupported)
            {
                if (protocol == Protocol.Relative)
                {
                    throw new NotSupportedException($"cannot create an outgoing frame for a relative proxy");
                }
                else
                {
                    throw new NotSupportedException($"cannot create an outgoing frame for protocol '{protocol}'");
                }
            }

            Protocol = protocol;
        }
    }
}

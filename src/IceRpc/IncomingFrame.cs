// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using System.Buffers;
using System.IO.Pipelines;

namespace IceRpc
{
    /// <summary>Base class for incoming frames.</summary>
    public abstract class IncomingFrame
    {
        /// <summary>The connection that received this frame.</summary>
        public Connection Connection { get; }

        /// <summary>Gets or sets the payload of this frame.</summary>
        /// <value>The payload of this frame. The default value is an empty <see cref="PipeReader"/>.</value>
        public PipeReader Payload { get; set; } = EmptyPipeReader.Instance;

        /// <summary>Returns the protocol of this frame.</summary>
        public Protocol Protocol => Connection.Endpoint.Protocol;

        /// <summary>Releases the memory used by the fields.</summary>
        public abstract void CompleteFields();

        /// <summary>Constructs an incoming frame.</summary>
        /// <param name="connection">The <see cref="Connection"/> that received the frame.</param>
        protected IncomingFrame(Connection connection) => Connection = connection;
    }

    /// <summary>Base class for incoming frames with fields.</summary>
    /// <paramtype name="T">The type of the keys in the Fields dictionary.</paramtype>
    public abstract class IncomingFrame<T> : IncomingFrame where T : struct
    {
        /// <summary>Gets the fields of this incoming frame.</summary>
        public IDictionary<T, ReadOnlySequence<byte>> Fields => _fields ??
            throw new InvalidOperationException($"cannot access fields after calling {nameof(CompleteFields)}");

        private IDictionary<T, ReadOnlySequence<byte>>? _fields;
        private readonly PipeReader? _fieldsPipeReader;

        /// <inheritdoc/>
        public override void CompleteFields()
        {
            if (_fields != null)
            {
                _fields = null;
                _fieldsPipeReader?.Complete();
            }
        }

        /// <summary>Constructs an incoming frame with fields.</summary>
        /// <param name="connection">The <see cref="Connection"/> that received the frame.</param>
        /// <param name="fields">The fields of this frame.</param>
        /// <param name="fieldsPipeReader">The pipe reader that holds the memory of the fields. Use <c>null</c> when the
        /// fields memory is not held by a pipe reader.</param>
        protected IncomingFrame(
            Connection connection,
            IDictionary<T, ReadOnlySequence<byte>> fields,
            PipeReader? fieldsPipeReader = null)
            : base(connection)
        {
            _fields = fields;
            _fieldsPipeReader = fieldsPipeReader;
        }
    }
}

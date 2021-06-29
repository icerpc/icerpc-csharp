// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;

namespace IceRpc
{
    /// <summary>Base class for outgoing frames.</summary>
    public abstract class OutgoingFrame
    {
        /// <summary>Returns a dictionary used to set the fields of this frame. The full fields are a combination of
        /// these fields plus the <see cref="FieldsDefaults"/>.</summary>
        /// <remarks>The actions set in this dictionary are executed when the frame is sent.</remarks>
        public Dictionary<int, Action<BufferWriter>> Fields
        {
            get
            {
                if (_fields == null)
                {
                    if (Protocol == Protocol.Ice1)
                    {
                        throw new NotSupportedException("ice1 does not support header fields");
                    }

                    _fields = new Dictionary<int, Action<BufferWriter>>();
                }
                return _fields;
            }
        }

        /// <summary>Returns the defaults fields set during construction of this frame. The fields are used only when
        /// there is no corresponding entry in <see cref="Fields"/>.</summary>
        public IReadOnlyDictionary<int, ReadOnlyMemory<byte>> FieldsDefaults { get; private protected init; } =
              ImmutableDictionary<int, ReadOnlyMemory<byte>>.Empty;

        /// <summary>The features of this frame.</summary>
        public FeatureCollection Features { get; set; } = FeatureCollection.Empty;

        /// <summary>Gets or sets the payload of this frame.</summary>
        public ReadOnlyMemory<ReadOnlyMemory<byte>> Payload
        {
            get => _payload;
            set
            {
                _payload = value;
                _payloadSize = -1;
            }
        }

        /// <summary>Returns the encoding of the payload of this frame.</summary>
        /// <remarks>The header of the frame is always encoded using the frame protocol's encoding.</remarks>
        public abstract Encoding PayloadEncoding { get; private protected set; }

        /// <summary>Returns the number of bytes in the payload.</summary>
        public int PayloadSize
        {
            get
            {
                if (_payloadSize == -1)
                {
                    _payloadSize = Payload.GetByteCount();
                }
                return _payloadSize;
            }
        }

        /// <summary>Returns the Ice protocol of this frame.</summary>
        public Protocol Protocol { get; }

        /// <summary>The stream encoder if the request or response has a stream param. The encoder is called
        /// after the request or response frame is sent over the stream.</summary>
        internal RpcStreamWriter? StreamWriter { get; set; }

        private Dictionary<int, Action<BufferWriter>>? _fields;

        private ReadOnlyMemory<ReadOnlyMemory<byte>> _payload = ReadOnlyMemory<ReadOnlyMemory<byte>>.Empty;
        private int _payloadSize = -1;

        /// <summary>Returns a new incoming frame built from this outgoing frame. This method is used for colocated
        /// calls.</summary>
        internal abstract IncomingFrame ToIncoming();

        /// <summary>Gets or builds a combined fields dictionary using <see cref="Fields"/> and
        /// <see cref="FieldsDefaults"/>. This method is used for colocated calls.</summary>
        internal IReadOnlyDictionary<int, ReadOnlyMemory<byte>> GetAllFields()
        {
            if (_fields == null)
            {
                return FieldsDefaults;
            }
            else
            {
                // Need to marshal/unmarshal these fields
                var writer = new BufferWriter(Encoding.V20);
                WriteFields(writer);
                return writer.Finish().ToSingleBuffer().ReadFieldValue(reader => reader.ReadFieldDictionary());
            }
        }

        /// <summary>Writes the header of a frame. This header does not include the frame's prologue.</summary>
        /// <param name="writer">The buffer encoder.</param>
        internal abstract void WriteHeader(BufferWriter writer);

        private protected OutgoingFrame(Protocol protocol, FeatureCollection features, RpcStreamWriter? streamWriter)
        {
            Protocol = protocol;
            Protocol.CheckSupported();
            Features = features;
            StreamWriter = streamWriter;
        }

        private protected void WriteFields(BufferWriter writer)
        {
            Debug.Assert(Protocol == Protocol.Ice2);
            Debug.Assert(writer.Encoding == Encoding.V20);

            // can be larger than necessary, which is fine
            int sizeLength =
                BufferWriter.GetSizeLength20(FieldsDefaults.Count + (_fields?.Count ?? 0));

            int size = 0;

            BufferWriter.Position start = writer.StartFixedLengthSize(sizeLength);

            // First write the fields then the remaining FieldsDefaults.

            if (_fields is Dictionary<int, Action<BufferWriter>> fields)
            {
                foreach ((int key, Action<BufferWriter> action) in fields)
                {
                    writer.WriteVarInt(key);
                    BufferWriter.Position startValue = writer.StartFixedLengthSize(2);
                    action(writer);
                    writer.EndFixedLengthSize(startValue, 2);
                    size++;
                }
            }
            foreach ((int key, ReadOnlyMemory<byte> value) in FieldsDefaults)
            {
                if (_fields == null || !_fields.ContainsKey(key))
                {
                    writer.WriteVarInt(key);
                    writer.WriteSize(value.Length);
                    writer.WriteByteSpan(value.Span);
                    size++;
                }
            }
            writer.RewriteFixedLengthSize20(size, start, sizeLength);
        }
    }
}

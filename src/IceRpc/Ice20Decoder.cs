// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using IceRpc.Transports.Internal;
using System.Collections;
using System.Collections.Immutable;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace IceRpc
{
    /// <summary>Decoder for the Ice 2.0 encoding.</summary>
    public class Ice20Decoder : IceDecoder
    {
        private readonly IClassFactory _classFactory;

        /// <summary>Decodes a field value.</summary>
        /// <typeparam name="T">The decoded type.</typeparam>
        /// <param name="value">The field value as a byte buffer encoded with the Ice 2.0 encoding.</param>
        /// <param name="decodeFunc">The decode function for this field value.</param>
        /// <param name="connection">The connection that received this field (used only for proxies).</param>
        /// <param name="invoker">The invoker of proxies in the decoded type.</param>
        /// <returns>The decoded value.</returns>
        /// <exception cref="InvalidDataException">Thrown when <paramref name="decodeFunc"/> finds invalid data.
        /// </exception>
        public static T DecodeFieldValue<T>(
            ReadOnlyMemory<byte> value,
            Func<Ice20Decoder, T> decodeFunc,
            Connection? connection = null,
            IInvoker? invoker = null)
        {
            var decoder = new Ice20Decoder(value, connection, invoker);
            T result = decodeFunc(decoder);
            decoder.CheckEndOfBuffer(skipTaggedParams: false);
            return result;
        }

        /// <inheritdoc/>
        public override RemoteException DecodeException()
        {
            string errorMessage = DecodeString();
            var origin = new RemoteExceptionOrigin(this);
            string typeId = DecodeString();
            RemoteException remoteEx = _classFactory.CreateRemoteException(typeId, errorMessage, origin) ??
                new RemoteException(errorMessage, origin);

            remoteEx.Decode(this);
            return remoteEx;
        }

        /// <summary>Decodes fields.</summary>
        /// <returns>The fields as an immutable dictionary.</returns>
        /// <remarks>The values of the dictionary reference memory in the decoder's underlying buffer.</remarks>
        public ImmutableDictionary<int, ReadOnlyMemory<byte>> DecodeFieldDictionary()
        {
            int size = DecodeSize();
            if (size == 0)
            {
                return ImmutableDictionary<int, ReadOnlyMemory<byte>>.Empty;
            }
            else
            {
                var builder = ImmutableDictionary.CreateBuilder<int, ReadOnlyMemory<byte>>();
                for (int i = 0; i < size; ++i)
                {
                    (int key, ReadOnlyMemory<byte> value) = DecodeField();
                    builder.Add(key, value);
                }
                return builder.ToImmutable();
            }
        }

        /// <inheritdoc/>
        public override T? DecodeNullableClass<T>() where T : class =>
            throw new NotSupportedException("cannot decode a class with the Ice 2.0 encoding");

        /// <inheritdoc/>
        public override Proxy? DecodeNullableProxy()
        {
            Debug.Assert(Connection != null);

            var proxyData = new ProxyData20(this);

            if (proxyData.Path == null)
            {
                return null;
            }

            Protocol protocol = proxyData.Protocol ?? Protocol.Ice2;
            Endpoint? endpoint = proxyData.Endpoint is EndpointData data ? data.ToEndpoint() : null;
            ImmutableList<Endpoint> altEndpoints =
                proxyData.AltEndpoints?.Select(data => data.ToEndpoint()).ToImmutableList() ??
                    ImmutableList<Endpoint>.Empty;

            if (endpoint == null && altEndpoints.Count > 0)
            {
                throw new InvalidDataException("received proxy with only alt endpoints");
            }

            try
            {
                Proxy proxy;

                if (endpoint == null && protocol != Protocol.Ice1)
                {
                    proxy = Proxy.FromConnection(Connection, proxyData.Path, Invoker);
                }
                else
                {
                    proxy = new Proxy(proxyData.Path, protocol);
                    proxy.Endpoint = endpoint;
                    proxy.AltEndpoints = altEndpoints;
                    proxy.Invoker = Invoker;
                }

                proxy.Encoding = proxyData.Encoding is string encoding ?
                    Encoding.FromString(encoding) : proxy.Protocol.GetEncoding();

                return proxy;
            }
            catch (Exception ex)
            {
                throw new InvalidDataException("received invalid proxy", ex);
            }
        }

        /// <inheritdoc/>
        public override int DecodeSize() => checked((int)DecodeVarULong());

        /// <inheritdoc/>
        [EditorBrowsable(EditorBrowsableState.Never)]
        public override void IceEndDerivedExceptionSlice() =>
            throw new NotSupportedException("cannot decode a derived exception with the Ice 2.0 encoding");

        /// <inheritdoc/>
        [EditorBrowsable(EditorBrowsableState.Never)]
        public override void IceEndException()
        {
        }

        /// <inheritdoc/>
        [EditorBrowsable(EditorBrowsableState.Never)]
        public override void IceStartDerivedExceptionSlice() =>
            throw new NotSupportedException("cannot decode a derived exception with the Ice 2.0 encoding");

        /// <inheritdoc/>
        [EditorBrowsable(EditorBrowsableState.Never)]
        public override void IceStartException()
        {
        }

        internal static (int Size, int SizeLength) DecodeSize(ReadOnlySpan<byte> from)
        {
            ulong size = (from[0] & 0x03) switch
            {
                0 => (uint)from[0] >> 2,
                1 => (uint)BitConverter.ToUInt16(from) >> 2,
                2 => BitConverter.ToUInt32(from) >> 2,
                _ => BitConverter.ToUInt64(from) >> 2
            };

            checked // make sure we don't overflow
            {
                return ((int)size, DecodeSizeLength(from[0]));
            }
        }

        internal static int DecodeSizeLength(byte b) => DecodeVarLongLength(b);

        /// <summary>Constructs a new decoder for the Ice 2.0 encoding.</summary>
        /// <param name="buffer">The byte buffer.</param>
        /// <param name="connection">The connection.</param>
        /// <param name="invoker">The invoker.</param>
        /// <param name="classFactory">The class factory, used to decode exceptions.</param>
        internal Ice20Decoder(
            ReadOnlyMemory<byte> buffer,
            Connection? connection = null,
            IInvoker? invoker = null,
            IClassFactory? classFactory = null)
            : base(buffer, connection, invoker) => _classFactory = classFactory ?? ClassFactory.Default;

        private protected override void SkipSize() => Skip(DecodeSizeLength(_buffer.Span[Pos]));

        // With Ice 2.0, all sizes use the same variable-length encoding:
        private protected override void SkipFixedLengthSize() => SkipSize();

        private protected override void SkipTagged(EncodingDefinitions.TagFormat format)
        {
            switch (format)
            {
                case EncodingDefinitions.TagFormat.F1:
                    Skip(1);
                    break;
                case EncodingDefinitions.TagFormat.F2:
                    Skip(2);
                    break;
                case EncodingDefinitions.TagFormat.F4:
                    Skip(4);
                    break;
                case EncodingDefinitions.TagFormat.F8:
                    Skip(8);
                    break;
                case EncodingDefinitions.TagFormat.Size:
                    SkipSize();
                    break;
                case EncodingDefinitions.TagFormat.VSize:
                case EncodingDefinitions.TagFormat.FSize:
                    Skip(DecodeSize());
                    break;
                default:
                    throw new InvalidDataException(
                        $"cannot skip tagged parameter or data member with tag format '{format}'");
            }
        }
    }
}

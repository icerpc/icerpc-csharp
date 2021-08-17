// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports.Internal;
using System.Collections;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace IceRpc.Internal
{
    /// <summary>Decoder for the Ice 2.0 encoding.</summary>
    internal class Ice20Decoder : IceDecoder
    {
        internal override SlicedData? SlicedData => null;

        private readonly IClassFactory _classFactory;

        public override T DecodeClass<T>() =>
            throw new NotSupportedException("cannot decode a class with the Ice 2.0 encoding");

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

        public override T? DecodeNullableClass<T>() where T : class =>
            throw new NotSupportedException("cannot decode a class with the Ice 2.0 encoding");

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

        public override int DecodeSize() => checked((int)DecodeVarULong());

        public override void IceEndDerivedExceptionSlice() =>
            throw new NotSupportedException("cannot decode a derived exception with the Ice 2.0 encoding");

        public override void IceEndException()
        {
        }

        public override void IceEndSlice() =>
            throw new NotSupportedException("cannot decode a class with the Ice 2.0 encoding");

        public override void IceStartDerivedExceptionSlice() =>
            throw new NotSupportedException("cannot decode a derived exception with the Ice 2.0 encoding");

        public override void IceStartException()
        {
        }

        public override SlicedData? IceStartFirstSlice() =>
            throw new NotSupportedException("cannot decode a class with the Ice 2.0 encoding");

        public override void IceStartNextSlice() =>
            throw new NotSupportedException("cannot decode a class with the Ice 2.0 encoding");

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

// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Slice.Internal;
using System.Collections.Immutable;
using System.Reflection;

namespace IceRpc.Slice
{
    /// <summary>Decoder for the Ice 2.0 encoding.</summary>
    public class Ice20Decoder : IceDecoder
    {
        private static readonly ActivatorFactory<Ice20Decoder> _activatorFactory =
            new(type => type == typeof(RemoteException) || type.BaseType == typeof(RemoteException));

        private readonly IActivator<Ice20Decoder>? _activator;

        /// <summary>Gets or creates an activator for the Slice types in the specified assembly and its referenced
        /// assemblies.</summary>
        /// <param name="assembly">The assembly.</param>
        /// <returns>An activator that activates the Slice types defined in <paramref name="assembly"/> provided this
        /// assembly contains generated code (as determined by the presence of the <see cref="SliceAttribute"/>
        /// attribute). Types defined in assemblies referenced by <paramref name="assembly"/> are included as well,
        /// recursively. The types defined in the referenced assemblies of an assembly with no generated code are not
        /// considered.</returns>
        public static IActivator<Ice20Decoder> GetActivator(Assembly assembly) => _activatorFactory.Get(assembly);

        /// <summary>Gets or creates an activator for the Slice types defined in the specified assemblies and their
        /// referenced assemblies.</summary>
        /// <param name="assemblies">The assemblies.</param>
        /// <returns>An activator that activates the Slice types defined in <paramref name="assemblies"/> and their
        /// referenced assemblies. See <see cref="GetActivator(Assembly)"/>.</returns>
        public static IActivator<Ice20Decoder> GetActivator(IEnumerable<Assembly> assemblies) =>
            Activator<Ice20Decoder>.Merge(assemblies.Select(assembly => _activatorFactory.Get(assembly)));

        /// <inheritdoc/>
        public override RemoteException DecodeException()
        {
            string typeId = DecodeString();
            var remoteEx = _activator?.CreateInstance(typeId, this) as RemoteException;

            if (remoteEx != null)
            {
                SkipTaggedParams(); // TODO: revisit
            }
            // else we can't decode this exception so we return an UnknownSlicedRemoteException instead of throwing
            // throwing "can't decode remote exception".

            return remoteEx ?? new UnknownSlicedRemoteException(typeId, this);
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
        public override Proxy? DecodeNullableProxy()
        {
            if (Connection == null)
            {
                throw new InvalidOperationException("cannot decode a proxy from an decoder with a null Connection");
            }

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

        /// <summary>Decodes a buffer.</summary>
        /// <typeparam name="T">The decoded type.</typeparam>
        /// <param name="buffer">The byte buffer encoded with the Ice 2.0 encoding.</param>
        /// <param name="decodeFunc">The decode function for buffer.</param>
        /// <returns>The decoded value.</returns>
        /// <exception cref="InvalidDataException">Thrown when <paramref name="decodeFunc"/> finds invalid data.
        /// </exception>
        internal static T DecodeBuffer<T>(ReadOnlyMemory<byte> buffer, Func<Ice20Decoder, T> decodeFunc)
        {
            var decoder = new Ice20Decoder(buffer);
            T result = decodeFunc(decoder);
            decoder.CheckEndOfBuffer(skipTaggedParams: false);
            return result;
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
        /// <param name="activator">The activator used to create remote exceptions from type IDs.</param>
        internal Ice20Decoder(
            ReadOnlyMemory<byte> buffer,
            Connection? connection = null,
            IInvoker? invoker = null,
            IActivator<Ice20Decoder>? activator = null)
            : base(buffer, connection, invoker) => _activator = activator;

        /// <summary>Decodes a field.</summary>
        /// <returns>The key and value of the field. The read-only memory for the value is backed by the buffer, the
        /// data is not copied.</returns>
        internal (int Key, ReadOnlyMemory<byte> Value) DecodeField()
        {
            int key = DecodeVarInt();
            int entrySize = DecodeSize();
            ReadOnlyMemory<byte> value = _buffer.Slice(Pos, entrySize);
            Pos += entrySize;
            return (key, value);
        }

        private protected override AnyClass? DecodeAnyClass() =>
            throw new NotSupportedException("cannot decode a class with the Ice 2.0 encoding");

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

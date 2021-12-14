// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Slice.Internal;
using System.Buffers;
using System.Reflection;

namespace IceRpc.Slice
{
    /// <summary>A factory for Ice decoders.</summary>
    /// <paramtype name="T">The Ice decoder class.</paramtype>
    public interface IIceDecoderFactory<out T> where T : IceDecoder
    {
        /// <summary>Returns the Ice encoding that this decoder factory supports.</summary>
        IceEncoding Encoding { get; }

        /// <summary>Creates an Ice decoder.</summary>
        /// <param name="buffer">The buffer to decode.</param>
        /// <param name="connection">The connection that received this buffer.</param>
        /// <param name="invoker">The invoker of proxies decoded by this decoder.</param>
        /// <returns>A new Ice decoder.</returns>
        T CreateIceDecoder(ReadOnlyMemory<byte> buffer, Connection? connection, IInvoker? invoker);

        /// <summary>Creates an Ice decoder.</summary>
        /// <param name="sequence">The sequence to decode.</param>
        /// <param name="connection">The connection that received this buffer.</param>
        /// <param name="invoker">The invoker of proxies decoded by this decoder.</param>
        /// <returns>A new Ice decoder.</returns>
        // TODO: add support of decoding ReadOnlySequence<byte> directly.
        T CreateIceDecoder(ReadOnlySequence<byte> sequence, Connection? connection, IInvoker? invoker) =>
            CreateIceDecoder(sequence.ToSingleBuffer(), connection, invoker);
    }

    /// <summary>The default implementation of <see cref="IIceDecoderFactory{T}"/> for <see cref="Ice11Decoder"/>.
    /// </summary>
    public class Ice11DecoderFactory : IIceDecoderFactory<Ice11Decoder>
    {
        /// <inheritdoc/>
        public IceEncoding Encoding => IceRpc.Encoding.Ice11;

        private readonly IActivator<Ice11Decoder> _activator;
        private readonly int _classGraphMaxDepth;

        /// <summary>Constructs an Ice decoder factory for encoding Ice 1.1.</summary>
        /// <param name="activator">The activator to use for the new Ice decoders.</param>
        /// <param name="classGraphMaxDepth">The maximum depth of class graphs decoded by the new Ice decoders.</param>
        public Ice11DecoderFactory(IActivator<Ice11Decoder> activator, int classGraphMaxDepth = 100)
        {
            _activator = activator;
            _classGraphMaxDepth = classGraphMaxDepth;
        }

        Ice11Decoder IIceDecoderFactory<Ice11Decoder>.CreateIceDecoder(
            ReadOnlyMemory<byte> buffer,
            Connection? connection,
            IInvoker? invoker) => new(buffer, connection, invoker, _activator, _classGraphMaxDepth);
    }

    /// <summary>The default implementation of <see cref="IIceDecoderFactory{T}"/> for <see cref="Ice20Decoder"/>.
    /// </summary>
    public class Ice20DecoderFactory : IIceDecoderFactory<Ice20Decoder>
    {
        /// <inheritdoc/>
        public IceEncoding Encoding => IceRpc.Encoding.Ice20;

        private readonly IActivator<Ice20Decoder> _activator;

        /// <summary>Constructs an Ice decoder factory for encoding Ice 2.0.</summary>
        /// <param name="activator">The activator to use for the new Ice decoders.</param>
        public Ice20DecoderFactory(IActivator<Ice20Decoder> activator) => _activator = activator;

        Ice20Decoder IIceDecoderFactory<Ice20Decoder>.CreateIceDecoder(
            ReadOnlyMemory<byte> buffer,
            Connection? connection,
            IInvoker? invoker) => new(buffer, connection, invoker, _activator);
    }

    /// <summary>A struct that holds default Ice decoder factories for all supported Ice encodings.</summary>
    public readonly record struct DefaultIceDecoderFactories
    {
        /// <summary>The default Ice decoder factory for the Ice 1.1 encoding.</summary>
        public IIceDecoderFactory<Ice11Decoder> Ice11DecoderFactory { get; }

        /// <summary>The default Ice decoder factory for the Ice 2.0 encoding.</summary>
        public IIceDecoderFactory<Ice20Decoder> Ice20DecoderFactory { get; }

        /// <summary>Constructs a default Ice decoder factories struct.</summary>
        /// <param name="assembly">An assembly that contains Slice generated code. See
        /// <see cref="Ice11Decoder.GetActivator(Assembly)"/>. and <see cref="Ice20Decoder.GetActivator(Assembly)"/>.
        /// </param>
        public DefaultIceDecoderFactories(Assembly assembly)
        {
            Ice11DecoderFactory = new Ice11DecoderFactory(Ice11Decoder.GetActivator(assembly));
            Ice20DecoderFactory = new Ice20DecoderFactory(Ice20Decoder.GetActivator(assembly));
        }
    }
}

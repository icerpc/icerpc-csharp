/// Copyright (c) ZeroC, Inc. All rights reserved.

using System.Reflection;

namespace IceRpc
{
    public interface IIceDecoderFactory<out T> where T : IceDecoder
    {
        T CreateIceDecoder(ReadOnlyMemory<byte> buffer, Connection? connection, IInvoker? invoker);
    }

    public class Ice11DecoderFactory : IIceDecoderFactory<Ice11Decoder>
    {
        private readonly IActivator<Ice11Decoder> _activator;
        private readonly int _classGraphMaxDepth;

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

    public class Ice20DecoderFactory : IIceDecoderFactory<Ice20Decoder>
    {
        private readonly IActivator<Ice20Decoder> _activator;

        public Ice20DecoderFactory(IActivator<Ice20Decoder> activator) => _activator = activator;

        Ice20Decoder IIceDecoderFactory<Ice20Decoder>.CreateIceDecoder(
            ReadOnlyMemory<byte> buffer,
            Connection? connection,
            IInvoker? invoker) => new(buffer, connection, invoker, _activator);
    }

    /// <summary>A struct that holds default Ice decoder factories for all supported Ice encodings.</summary>
    public readonly struct DefaultIceDecoderFactories
    {
        public IIceDecoderFactory<Ice11Decoder> Ice11DecoderFactory { get; }
        public IIceDecoderFactory<Ice20Decoder> Ice20DecoderFactory { get; }

        public DefaultIceDecoderFactories(Assembly assembly)
        {
            Ice11DecoderFactory = new Ice11DecoderFactory(Ice11Decoder.GetActivator(assembly));
            Ice20DecoderFactory = new Ice20DecoderFactory(Ice20Decoder.GetActivator(assembly));
        }
    }
}

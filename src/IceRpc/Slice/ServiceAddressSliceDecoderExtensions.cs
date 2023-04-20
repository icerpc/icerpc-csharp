// Copyright (c) ZeroC, Inc.

using IceRpc.Ice;
using IceRpc.Slice.Internal;
using IceRpc.Transports;
using IceRpc.Transports.Internal;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;

namespace IceRpc.Slice;

/// <summary>Provides extension methods for decoding service addresses.</summary>
public static class ServiceAddressSliceDecoderExtensions
{
    /// <summary>Decodes a service address.</summary>
    /// <param name="decoder">The Slice decoder.</param>
    /// <returns>The decoded service address.</returns>
    public static ServiceAddress DecodeServiceAddress(this ref SliceDecoder decoder)
    {
        if (decoder.Encoding == SliceEncoding.Slice1)
        {
            return decoder.DecodeNullableServiceAddress() ??
                throw new InvalidDataException("Decoded null for a non-nullable service address.");
        }
        else
        {
            string serviceAddressString = decoder.DecodeString();
            try
            {
                if (serviceAddressString.StartsWith('/'))
                {
                    // relative service address
                    return new ServiceAddress { Path = serviceAddressString };
                }
                else
                {
                    return new ServiceAddress(new Uri(serviceAddressString, UriKind.Absolute));
                }
            }
            catch (Exception exception)
            {
                throw new InvalidDataException("Received an invalid service address.", exception);
            }
        }
    }

    /// <summary>Decodes a nullable service address (Slice1 only).</summary>
    /// <param name="decoder">The Slice decoder.</param>
    /// <returns>The decoded service address, or <see langword="null" />.</returns>
    public static ServiceAddress? DecodeNullableServiceAddress(this ref SliceDecoder decoder)
    {
        if (decoder.Encoding != SliceEncoding.Slice1)
        {
            throw new InvalidOperationException(
                $"Decoding a nullable Proxy with {decoder.Encoding} requires a bit sequence.");
        }
        string path = decoder.DecodeIdentityPath();
        return path != "/" ? decoder.DecodeServiceAddressCore(path) : null;
    }

    /// <summary>Decodes a server address (Slice1 only).</summary>
    /// <param name="decoder">The Slice decoder.</param>
    /// <param name="protocol">The protocol of this server address.</param>
    /// <returns>The server address decoded by this decoder.</returns>
    private static ServerAddress DecodeServerAddress(this ref SliceDecoder decoder, Protocol protocol)
    {
        Debug.Assert(decoder.Encoding == SliceEncoding.Slice1);

        // The Slice1 ice server addresses are transport-specific, and hard-coded here and in the
        // SliceEncoder. The preferred and fallback encoding for new transports is TransportCode.Uri.

        ServerAddress? serverAddress = null;
        var transportCode = (TransportCode)decoder.DecodeInt16();

        int size = decoder.DecodeInt32();
        if (size < 6)
        {
            throw new InvalidDataException($"The Slice1 encapsulation's size ({size}) is too small.");
        }

        // Remove 6 bytes from the encapsulation size (4 for encapsulation size, 2 for encoding).
        size -= 6;

        byte encodingMajor = decoder.DecodeUInt8();
        byte encodingMinor = decoder.DecodeUInt8();

        if (encodingMajor == 1 && encodingMinor <= 1)
        {
            long oldPos = decoder.Consumed;

            if (protocol == Protocol.Ice)
            {
                switch (transportCode)
                {
                    case TransportCode.Tcp:
                    case TransportCode.Ssl:
                    {
                        serverAddress = TcpClientTransport.DecodeServerAddress(
                            ref decoder,
                            transportCode == TransportCode.Tcp ? TransportNames.Tcp : TransportNames.Ssl);
                        break;
                    }

                    case TransportCode.Uri:
                        serverAddress = new ServerAddress(new Uri(decoder.DecodeString()));
                        if (serverAddress.Value.Protocol != protocol)
                        {
                            throw new InvalidDataException(
                                $"Expected {protocol} server address but received '{serverAddress.Value}'.");
                        }
                        break;

                    default:
                    {
                        // Create a server address for transport opaque
                        ImmutableDictionary<string, string>.Builder builder =
                            ImmutableDictionary.CreateBuilder<string, string>();

                        if (encodingMinor == 0)
                        {
                            builder.Add("e", "1.0");
                        }
                        // else no e

                        builder.Add("t", ((short)transportCode).ToString(CultureInfo.InvariantCulture));
                        builder.Add("v", decoder.ReadBytesAsBase64String(size));

                        serverAddress = new ServerAddress(
                            Protocol.Ice,
                            host: "opaque", // not a real host obviously
                            port: Protocol.Ice.DefaultPort,
                            TransportNames.Opaque,
                            builder.ToImmutable());
                        break;
                    }
                }
            }
            else if (transportCode == TransportCode.Uri)
            {
                // The server addresses of Slice1 encoded icerpc proxies only use TransportCode.Uri.
                serverAddress = new ServerAddress(new Uri(decoder.DecodeString()));
                if (serverAddress.Value.Protocol != protocol)
                {
                    throw new InvalidDataException(
                        $"Expected {protocol} server address but received '{serverAddress.Value}'.");
                }
            }

            if (serverAddress is not null)
            {
                // Make sure we read the full encapsulation.
                if (decoder.Consumed != oldPos + size)
                {
                    throw new InvalidDataException(
                        $"There are {oldPos + size - decoder.Consumed} bytes left in server address encapsulation.");
                }
            }
        }

        if (serverAddress is null)
        {
            throw new InvalidDataException(
                $"Cannot decode server address for protocol '{protocol}' and transport '{transportCode.ToString().ToLowerInvariant()}' with server address encapsulation encoded with encoding '{encodingMajor}.{encodingMinor}'.");
        }

        return serverAddress.Value;
    }

    /// <summary>Helper method to decode a service address encoded with Slice1.</summary>
    /// <param name="decoder">The Slice decoder.</param>
    /// <param name="path">The decoded path.</param>
    /// <returns>The decoded service address.</returns>
    private static ServiceAddress DecodeServiceAddressCore(this ref SliceDecoder decoder, string path)
    {
        // With Slice1, a proxy is encoded as a kind of discriminated union with:
        // - Identity
        // - If Identity is not the null identity:
        //     - the fragment, invocation mode, secure, protocol major and minor, and the encoding major and minor
        //     - a sequence of server addresses (can be empty)
        //     - an adapter ID string present only when the sequence of server addresses is empty

        string fragment = decoder.DecodeFragment();
        _ = decoder.DecodeInvocationMode();
        _ = decoder.DecodeBool();
        byte protocolMajor = decoder.DecodeUInt8();
        byte protocolMinor = decoder.DecodeUInt8();
        decoder.Skip(2); // skip encoding major and minor

        if (protocolMajor == 0)
        {
            throw new InvalidDataException("Received service address with protocol set to 0.");
        }
        if (protocolMinor != 0)
        {
            throw new InvalidDataException(
                $"Received service address with invalid protocolMinor value: {protocolMinor}.");
        }

        int count = decoder.DecodeSize();

        ServerAddress? serverAddress = null;
        IEnumerable<ServerAddress> altServerAddresses = ImmutableList<ServerAddress>.Empty;
        var protocol = Protocol.FromByteValue(protocolMajor);
        ImmutableDictionary<string, string> serviceAddressParams = ImmutableDictionary<string, string>.Empty;

        if (count == 0)
        {
            if (decoder.DecodeString() is string adapterId && adapterId.Length > 0)
            {
                serviceAddressParams = serviceAddressParams.Add("adapter-id", adapterId);
            }
        }
        else
        {
            serverAddress = decoder.DecodeServerAddress(protocol);
            if (count >= 2)
            {
                // A slice1 encoded server address consumes at least 8 bytes (2 bytes for the server address type and 6
                // bytes for the encapsulation header). SizeOf ServerAddress is large but less than 8 * 8.
                decoder.IncreaseCollectionAllocation(count * Unsafe.SizeOf<ServerAddress>());

                var serverAddressArray = new ServerAddress[count - 1];
                for (int i = 0; i < count - 1; ++i)
                {
                    serverAddressArray[i] = decoder.DecodeServerAddress(protocol);
                }
                altServerAddresses = serverAddressArray;
            }
        }

        try
        {
            if (!protocol.HasFragment && fragment.Length > 0)
            {
                throw new InvalidDataException($"Unexpected fragment in {protocol} service address.");
            }

            return new ServiceAddress(
                protocol,
                path,
                serverAddress,
                altServerAddresses.ToImmutableList(),
                serviceAddressParams,
                fragment);
        }
        catch (InvalidDataException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new InvalidDataException("Received invalid service address.", exception);
        }
    }
}

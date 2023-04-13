// Copyright (c) ZeroC, Inc.

using IceRpc.Ice;
using IceRpc.Slice.Internal;
using System.Collections.Immutable;
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

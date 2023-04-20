// Copyright (c) ZeroC, Inc.

using IceRpc.Ice;
using IceRpc.Slice.Internal;
using IceRpc.Transports;
using IceRpc.Transports.Internal;
using System.Diagnostics;
using System.Globalization;

namespace IceRpc.Slice;

/// <summary>Provides extension methods for encoding service addresses.</summary>
public static class ServiceAddressSliceEncoderExtensions
{
    /// <summary>Encodes a service address.</summary>
    /// <param name="encoder">The Slice encoder.</param>
    /// <param name="value">The value to encode.</param>
    public static void EncodeServiceAddress(this ref SliceEncoder encoder, ServiceAddress value)
    {
        if (encoder.Encoding == SliceEncoding.Slice1)
        {
            // With Slice1, a proxy is encoded as a kind of discriminated union with:
            // - Identity
            // - If Identity is not the null identity:
            //     - the fragment, invocation mode, secure, protocol major and minor, and the encoding major and minor
            //     - a sequence of server addresses (can be empty)
            //     - an adapter ID string present only when the sequence of server addresses is empty

            encoder.EncodeIdentityPath(value.Path);

            if (value.Protocol is not Protocol protocol)
            {
                throw new NotSupportedException("Cannot encode a relative service address with Slice1.");
            }

            encoder.EncodeFragment(value.Fragment);
            encoder.EncodeInvocationMode(InvocationMode.Twoway);
            encoder.EncodeBool(false);               // Secure
            encoder.EncodeUInt8(protocol.ByteValue); // Protocol Major
            encoder.EncodeUInt8(0);                  // Protocol Minor
            encoder.EncodeUInt8(1);                  // Encoding Major
            encoder.EncodeUInt8(1);                  // Encoding Minor

            if (value.ServerAddress is ServerAddress serverAddress)
            {
                encoder.EncodeSize(1 + value.AltServerAddresses.Count); // server address count
                encoder.EncodeServerAddress(serverAddress);
                foreach (ServerAddress altServer in value.AltServerAddresses)
                {
                    encoder.EncodeServerAddress(altServer);
                }
            }
            else
            {
                encoder.EncodeSize(0); // 0 server addresses
                int maxCount = value.Params.TryGetValue("adapter-id", out string? adapterId) ? 1 : 0;

                if (value.Params.Count > maxCount)
                {
                    throw new NotSupportedException(
                        "Cannot encode a service address with a parameter other than adapter-id using Slice1.");
                }
                encoder.EncodeString(adapterId ?? "");
            }
        }
        else
        {
            encoder.EncodeString(value.ToString()); // a URI or an absolute path
        }
    }

    /// <summary>Encodes a nullable service address (Slice1 only).</summary>
    /// <param name="encoder">The Slice encoder.</param>
    /// <param name="value">The service address to encode, or <see langword="null" />.</param>
    public static void EncodeNullableServiceAddress(this ref SliceEncoder encoder, ServiceAddress? value)
    {
        if (encoder.Encoding != SliceEncoding.Slice1)
        {
            throw new InvalidOperationException(
                "Encoding a nullable service address without a bit sequence is only supported with Slice1.");
        }

        if (value is not null)
        {
            encoder.EncodeServiceAddress(value);
        }
        else
        {
            Identity.Empty.Encode(ref encoder);
        }
    }

    /// <summary>Encodes a server address in a nested encapsulation (Slice1 only).</summary>
    /// <param name="encoder">The Slice encoder.</param>
    /// <param name="serverAddress">The server address to encode.</param>
    private static void EncodeServerAddress(this ref SliceEncoder encoder, ServerAddress serverAddress)
    {
        // If the server address does not specify a transport, we default to TCP.
        string transport = serverAddress.Transport ?? TransportNames.Tcp;

        // The Slice1 encoding of ice server addresses is transport-specific, and hard-coded here. The preferred and
        // fallback encoding for new transports is TransportCode.Uri.

        if (serverAddress.Protocol == Protocol.Ice && transport == TransportNames.Opaque)
        {
            // Opaque server address encoding

            (short transportCode, byte encodingMajor, byte encodingMinor, ReadOnlyMemory<byte> bytes) =
                serverAddress.ParseOpaqueParams();

            encoder.EncodeInt16(transportCode);

            // encapsulation size includes size-length and 2 bytes for encoding
            encoder.EncodeInt32(4 + 2 + bytes.Length);
            encoder.EncodeUInt8(encodingMajor);
            encoder.EncodeUInt8(encodingMinor);
            encoder.WriteByteSpan(bytes.Span);
        }
        else
        {
            TransportCode transportCode = serverAddress.Protocol == Protocol.Ice ?
                transport switch
                {
                    TransportNames.Ssl => TransportCode.Ssl,
                    TransportNames.Tcp => TransportCode.Tcp,
                    _ => TransportCode.Uri
                } :
                TransportCode.Uri;

            encoder.EncodeInt16((short)transportCode);

            int startPos = encoder.EncodedByteCount; // size includes size-length
            Span<byte> sizePlaceholder = encoder.GetPlaceholderSpan(4); // encapsulation size
            encoder.EncodeUInt8(1); // encoding version major
            encoder.EncodeUInt8(1); // encoding version minor

            switch (transportCode)
            {
                case TransportCode.Tcp:
                case TransportCode.Ssl:
                    TcpClientTransport.EncodeServerAddress(ref encoder, serverAddress);
                    break;

                default:
                    Debug.Assert(transportCode == TransportCode.Uri);
                    encoder.EncodeString(serverAddress.ToString());
                    break;
            }

            SliceEncoder.EncodeInt32(encoder.EncodedByteCount - startPos, sizePlaceholder);
        }
    }

    /// <summary>Parses the params of an opaque server address (Slice1 only).</summary>
    private static (short TransportCode, byte EncodingMajor, byte EncodingMinor, ReadOnlyMemory<byte> Bytes) ParseOpaqueParams(
       this ServerAddress serverAddress)
    {
        short transportCode = -1;
        ReadOnlyMemory<byte> bytes = default;
        byte encodingMajor = 1;
        byte encodingMinor = 1;

        foreach ((string name, string value) in serverAddress.Params)
        {
            switch (name)
            {
                case "e":
                    (encodingMajor, encodingMinor) = value switch
                    {
                        "1.0" => ((byte)1, (byte)0),
                        "1.1" => ((byte)1, (byte)1),
                        _ => throw new FormatException(
                            $"Invalid value for parameter 'e' in server address: '{serverAddress}'.")
                    };
                    break;

                case "t":
                    try
                    {
                        transportCode = short.Parse(value, CultureInfo.InvariantCulture);
                    }
                    catch (FormatException exception)
                    {
                        throw new FormatException(
                            $"Invalid value for parameter 't' in server address: '{serverAddress}'.", exception);
                    }

                    if (transportCode < 0)
                    {
                        throw new FormatException(
                            $"The value for parameter 't' is out of range in server address: '{serverAddress}'.");
                    }
                    break;

                case "v":
                    try
                    {
                        bytes = Convert.FromBase64String(value);
                    }
                    catch (FormatException exception)
                    {
                        throw new FormatException(
                            $"Invalid Base64 value in server address: '{serverAddress}'.",
                            exception);
                    }
                    break;

                default:
                    throw new FormatException($"Unknown parameter '{name}' in server address: '{serverAddress}'.");
            }
        }

        if (transportCode == -1)
        {
            throw new FormatException($"Missing 't' parameter in server address: '{serverAddress}'.");
        }
        else if (bytes.Length == 0)
        {
            throw new FormatException($"Missing 'v' parameter in server address: '{serverAddress}'.");
        }

        return (transportCode, encodingMajor, encodingMinor, bytes);
    }
}

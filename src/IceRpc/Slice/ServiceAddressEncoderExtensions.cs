// Copyright (c) ZeroC, Inc.

using IceRpc.Ice;
using IceRpc.Slice.Internal;

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
}

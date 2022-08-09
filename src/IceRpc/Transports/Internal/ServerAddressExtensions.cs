// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Slice.Internal;
using System.Globalization;

namespace IceRpc.Transports.Internal;

/// <summary>Extension methods for class ServerAddress.</summary>
internal static class ServerAddressExtensions
{
    internal static (TransportCode TransportCode, byte EncodingMajor, byte EncodingMinor, ReadOnlyMemory<byte> Bytes) ParseOpaqueParams(
       this ServerAddress serverAddress)
    {
        TransportCode? transportCode = null;
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
                        _ => throw new FormatException($"invalid value for e parameter in server address '{serverAddress}'")
                    };
                    break;

                case "t":
                    short t;
                    try
                    {
                        t = short.Parse(value, CultureInfo.InvariantCulture);
                    }
                    catch (FormatException ex)
                    {
                        throw new FormatException(
                            $"invalid value for parameter t in server address '{serverAddress}'", ex);
                    }

                    if (t < 0)
                    {
                        throw new FormatException(
                            $"value for t parameter out of range in server address '{serverAddress}'");
                    }

                    transportCode = (TransportCode)t;
                    break;

                case "v":
                    try
                    {
                        bytes = Convert.FromBase64String(value);
                    }
                    catch (FormatException ex)
                    {
                        throw new FormatException($"invalid Base64 value in server address '{serverAddress}'", ex);
                    }
                    break;

                default:
                    throw new FormatException($"unknown parameter '{name}' in server address '{serverAddress}'");
            }
        }

        if (transportCode is null)
        {
            throw new FormatException($"missing t parameter in server address '{serverAddress}'");
        }
        else if (bytes.Length == 0)
        {
            throw new FormatException($"missing v parameter in server address '{serverAddress}'");
        }

        return (transportCode.Value, encodingMajor, encodingMinor, bytes);
    }
}

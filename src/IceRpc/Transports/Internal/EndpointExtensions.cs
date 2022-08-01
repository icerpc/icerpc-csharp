// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Slice.Internal;
using System.Globalization;

namespace IceRpc.Transports.Internal;

/// <summary>Extension methods for class Endpoint.</summary>
internal static class EndpointExtensions
{
    internal static (TransportCode TransportCode, byte EncodingMajor, byte EncodingMinor, ReadOnlyMemory<byte> Bytes) ParseOpaqueParams(
       this Endpoint endpoint)
    {
        TransportCode? transportCode = null;
        ReadOnlyMemory<byte> bytes = default;
        byte encodingMajor = 1;
        byte encodingMinor = 1;

        foreach ((string name, string value) in endpoint.Params)
        {
            switch (name)
            {
                case "e":
                    (encodingMajor, encodingMinor) = value switch
                    {
                        "1.0" => ((byte)1, (byte)0),
                        "1.1" => ((byte)1, (byte)1),
                        _ => throw new FormatException($"invalid value for e parameter in endpoint '{endpoint}'")
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
                            $"invalid value for parameter t in endpoint '{endpoint}'", ex);
                    }

                    if (t < 0)
                    {
                        throw new FormatException(
                            $"value for t parameter out of range in endpoint '{endpoint}'");
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
                        throw new FormatException($"invalid Base64 value in endpoint '{endpoint}'", ex);
                    }
                    break;

                default:
                    throw new FormatException($"unknown parameter '{name}' in endpoint '{endpoint}'");
            }
        }

        if (transportCode is null)
        {
            throw new FormatException($"missing t parameter in endpoint '{endpoint}'");
        }
        else if (bytes.Length == 0)
        {
            throw new FormatException($"missing v parameter in endpoint '{endpoint}'");
        }

        return (transportCode.Value, encodingMajor, encodingMinor, bytes);
    }
}

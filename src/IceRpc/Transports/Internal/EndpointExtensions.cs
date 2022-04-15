// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Slice.Internal;
using System.Globalization;

namespace IceRpc.Transports.Internal
{
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
                    case "transport":
                        if (value != TransportNames.Opaque)
                        {
                            throw new FormatException(
                                $"invalid value for transport parameter in endpoint '{endpoint}'");
                        }
                        break;

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

            if (transportCode == null)
            {
                throw new FormatException($"missing t parameter in endpoint '{endpoint}'");
            }
            else if (bytes.Length == 0)
            {
                throw new FormatException($"missing v parameter in endpoint '{endpoint}'");
            }

            return (transportCode.Value, encodingMajor, encodingMinor, bytes);
        }

        /// <summary>Checks the parameters of a tcp endpoint and returns the value of the transport parameter. The "t"
        /// and "z" parameters are supported and ignored for compatibility with ZeroC Ice.</summary>
        /// <returns>The value of the transport parameter, or null if the transport parameter is not set.</returns>
        /// <exception cref="FormatException">Thrown when an endpoint parameter is unknown or transport has an invalid
        /// value.</exception>
        internal static string? ParseTcpParams(this Endpoint endpoint)
        {
            string? transportValue = null;

            foreach ((string name, string value) in endpoint.Params)
            {
                switch (name)
                {
                    case "transport":
                        transportValue = value switch
                        {
                            TransportNames.Tcp or TransportNames.Ssl => value,
                            _ => throw new FormatException(
                                    $"invalid value for transport parameter in endpoint '{endpoint}'")
                        };
                        break;

                    case "t":
                    case "z":
                        // we don't check the value since we ignore it
                        break;

                    default:
                        throw new FormatException($"unknown parameter '{name}' in endpoint '{endpoint}'");
                }
            }

            return transportValue;
        }

        /// <summary>Adds the transport parameter to this endpoint if null, and does nothing if it's already set to the
        /// correct value.</summary>
        /// <exception cref="ArgumentException">Thrown if endpoint already holds another transport.</exception>
        internal static Endpoint WithTransport(this Endpoint endpoint, string transport)
        {
            if (endpoint.Params.TryGetValue("transport", out string? endpointTransport))
            {
                if (endpointTransport != transport)
                {
                    throw new ArgumentException(
                        $"cannot use {transport} transport with endpoint '{endpoint}'",
                        nameof(endpoint));
                }
            }
            else
            {
                endpoint = endpoint with
                {
                    Params = endpoint.Params.Add("transport", transport)
                };
            }

            return endpoint;
        }
    }
}

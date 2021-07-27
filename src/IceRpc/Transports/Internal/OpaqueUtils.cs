// Copyright (c) ZeroC, Inc. All rights reserved.

using System;
using System.Globalization;

namespace IceRpc.Transports.Internal
{
    internal static class OpaqueUtils
    {
        internal static (TransportCode TransportCode, ReadOnlyMemory<byte> Bytes) ParseExternalOpaqueParams(
            EndpointRecord endpoint)
        {
            if (endpoint.Protocol != Protocol.Ice1)
            {
                throw new FormatException($"endpoint '{endpoint}': protocol/transport mistmatch");
            }

            TransportCode? transportCode = null;
            ReadOnlyMemory<byte> bytes = default;
            bool encodingFound = false;

            foreach ((string name, string value) in endpoint.ExternalParams)
            {
                switch (name)
                {
                    case "-e":
                        if (encodingFound)
                        {
                            throw new FormatException($"multiple -e parameters in endpoint '{endpoint}'");
                        }
                        if (value == "1.0" || value == "1.1")
                        {
                            encodingFound = true;
                        }
                        else
                        {
                            throw new FormatException($"invalid value for -e parameter in endpoint '{endpoint}'");
                        }
                        break;

                    case "-t":
                        if (transportCode != null)
                        {
                            throw new FormatException($"multiple -t parameters in endpoint '{endpoint}'");
                        }

                        short t;
                        try
                        {
                            t = short.Parse(value, CultureInfo.InvariantCulture);
                        }
                        catch (FormatException ex)
                        {
                            throw new FormatException(
                                $"invalid value for paramater -t in endpoint '{endpoint}'", ex);
                        }

                        if (t < 0)
                        {
                            throw new FormatException(
                                $"value for -t parameter out of range in endpoint '{endpoint}'");
                        }

                        transportCode = (TransportCode)t;
                        break;

                    case "-v":
                        if (bytes.Length > 0)
                        {
                            throw new FormatException($"multiple -v parameters in endpoint '{endpoint}'");
                        }

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
                throw new FormatException($"missing -t parameter in endpoint '{endpoint}'");
            }
            else if (bytes.Length == 0)
            {
                throw new FormatException($"missing -v parameter in endpoint '{endpoint}'");
            }

            return (transportCode.Value, bytes);
        }
    }
}

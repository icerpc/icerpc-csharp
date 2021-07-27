// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;

namespace IceRpc.Transports.Internal
{
    internal static class TcpUtils
    {
        internal const int DefaultTcpTimeout = 60_000;

        internal static (bool Compress, int Timeout, bool? Tls) ParseTcpParams(Endpoint endpoint)
        {
            bool? tls = null;

            foreach ((string name, string value) in endpoint.LocalParams)
            {
                if (endpoint.Protocol != Protocol.Ice1 && name == "_tls")
                {
                    if (tls != null)
                    {
                        throw new FormatException($"multiple _tls parameters in endpoint '{endpoint}'");
                    }
                    try
                    {
                        tls = bool.Parse(value);
                    }
                    catch (FormatException ex)
                    {
                        throw new FormatException($"invalid value for _tls parameter in endpoint '{endpoint}'", ex);
                    }
                }
                else
                {
                    throw new FormatException($"unknown parameter '{name}' in endpoint '{endpoint}'");
                }
            }

            (bool compress, int timeout) = ParseExternalTcpParams(endpoint);
            return (compress, timeout, tls);
        }

        internal static (bool Compress, int Timeout) ParseExternalTcpParams(Endpoint endpoint)
        {
            bool compress = false;
            int? timeout = null;

            foreach ((string name, string value) in endpoint.ExternalParams)
            {
                if (endpoint.Protocol == Protocol.Ice1)
                {
                    switch (name)
                    {
                        case "-t":
                            if (timeout != null)
                            {
                                throw new FormatException($"multiple -t parameters in endpoint '{endpoint}'");
                            }
                            if (value == "infinite")
                            {
                                timeout = -1;
                            }
                            else
                            {
                                timeout = int.Parse(value, CultureInfo.InvariantCulture); // timeout in ms, or -1
                                if (timeout == 0 || timeout < -1)
                                {
                                    throw new FormatException(
                                        $"invalid value for -t parameter in endpoint '{endpoint}'");
                                }
                            }
                            continue; // loop back

                        case "-z":
                            if (compress)
                            {
                                throw new FormatException($"multiple -z parameters in endpoint '{endpoint}'");
                            }
                            if (value.Length > 0)
                            {
                                throw new FormatException(
                                    $"invalid value '{value}' for parameter -z in endpoint '{endpoint}'");
                            }
                            compress = true;
                            continue; // loop back

                        default:
                            break;
                    }

                    throw new FormatException($"unknown parameter '{name}' in endpoint '{endpoint}'");
                }
            }
            return (compress, timeout ?? DefaultTcpTimeout);
        }
    }
}

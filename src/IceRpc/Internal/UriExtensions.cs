// Copyright (c) ZeroC, Inc.

using System.Collections.Immutable;

namespace IceRpc.Internal;

/// <summary>Extension methods for <see cref="Uri" />.</summary>
internal static class UriExtensions
{
    /// <summary>Parses the query portion of a URI into a dictionary of name/value. The value of the alt-server
    /// and transport parameters, if set, are returned separately.</summary>
    internal static (ImmutableDictionary<string, string> QueryParams, string? AltServerValue, string? TransportValue) ParseQuery(
        this Uri uri)
    {
        if (uri.Query.Length < 2)
        {
            // no query or empty query
            return (ImmutableDictionary<string, string>.Empty, null, null);
        }
        else
        {
            ImmutableDictionary<string, string> queryParams = ImmutableDictionary<string, string>.Empty;
            string? altServer = null;
            string? transport = null;

            foreach (string p in uri.Query.TrimStart('?').Split('&'))
            {
                int equalPos = p.IndexOf('=', StringComparison.Ordinal);
                string name = equalPos == -1 ? p : p[..equalPos];
                string value = equalPos == -1 ? "" : p[(equalPos + 1)..];

                if (name == "alt-server")
                {
                    altServer = altServer is null ? value : $"{altServer},{value}";
                }
                else if (name == "transport")
                {
                    // This is the regular parsing for query parameters, even though it's not meaningful for transport.
                    transport = transport is null ? value : $"{transport},{value}";
                }
                else
                {
                    if (name.Length == 0)
                    {
                        throw new FormatException($"Invalid empty query parameter name in URI '{uri.OriginalString}'.");
                    }

                    // we assume the C# URI parser validates the name and value sufficiently

                    if (queryParams.TryGetValue(name, out string? existingValue))
                    {
                        queryParams = queryParams.SetItem(name, $"{existingValue},{value}");
                    }
                    else
                    {
                        queryParams = queryParams.Add(name, value);
                    }
                }
            }
            return (queryParams, altServer, transport);
        }
    }
}

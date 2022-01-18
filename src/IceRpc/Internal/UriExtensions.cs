// Copyright (c) ZeroC, Inc. All rights reserved.

using System.Collections.Immutable;

namespace IceRpc.Internal
{
    /// <summary>Extension methods for <see cref="Uri"/>.</summary>
    internal static class UriExtensions
    {
        /// <summary>Parses the query portion of a URI into a dictionary of name/value. The value of the alt-endpoint
        /// parameter, if set, is returned separately.</summary>
        internal static (ImmutableDictionary<string, string> QueryParams, string? AltEndpointValue) ParseQuery(
            this Uri uri)
        {
            if (uri.Query.Length < 2)
            {
                // no query or empty query
                return (ImmutableDictionary<string, string>.Empty, null);
            }
            else
            {
                var queryParams = new Dictionary<string, string>();
                string? altEndpoint = null;

                foreach (string p in uri.Query.TrimStart('?').Split('&'))
                {
                    int equalPos = p.IndexOf('=', StringComparison.Ordinal);
                    string name = equalPos == -1 ? p : p[..equalPos];
                    string value = equalPos == -1 ? "" : p[(equalPos + 1)..];

                    if (name == "alt-endpoint")
                    {
                        altEndpoint = altEndpoint == null ? value : $"{altEndpoint},{value}";
                    }
                    else
                    {
                        if (name.Length == 0)
                        {
                            throw new FormatException(
                                $"invalid empty query parameter name in URI '{uri.OriginalString}'");
                        }

                        // we assume the C# URI parser validates the name and value sufficiently

                        if (queryParams.TryGetValue(name, out string? existingValue))
                        {
                            queryParams[name] = $"{existingValue},{value}";
                        }
                        else
                        {
                            queryParams.Add(name, value);
                        }
                    }
                }
                return (queryParams.ToImmutableDictionary(), altEndpoint);
            }
        }
    }
}

// Copyright (c) ZeroC, Inc. All rights reserved.

using System;
using System.Diagnostics;
using System.Linq;

namespace ZeroC.Ice
{
    public readonly partial struct Identity
    {
        /// <summary>The empty Identity.</summary>
        public static readonly Identity Empty = new Identity("", "");

        /// <summary>Converts the string representation of an identity to its equivalent Identity struct.</summary>
        /// <param name="s">A string [escapedCategory/]escapedName.</param>
        /// <param name="uriPath">When true (the default), the string is a relative URI. When false, the string
        /// uses the ice1 format. See also <see cref="ToStringMode"/>.</param>
        /// <exception cref="FormatException">s is not in the correct format.</exception>
        /// <returns>An Identity equivalent to the identity contained in s.</returns>
        public static Identity Parse(string s, bool uriPath = true) =>
            uriPath ? ParsePath(s)!.Value : Ice1Parser.ParseIdentity(s);

        /// <summary>Converts the string representation of an identity to its equivalent Identity struct.</summary>
        /// <param name="s">A string containing an identity to convert.</param>
        /// <param name="uriPath">When true, the string is a URI path. When false, the string uses the ice1 format. See
        /// also <see cref="ToStringMode"/>.</param>
        /// <param name="identity">When this method returns, contains an Identity struct equivalent to the identity
        /// contained in s, if the conversion succeeded, or a default identity if failed. </param>
        /// <returns>true if s was converted successfully; otherwise, false.</returns>
        public static bool TryParse(string s, bool uriPath, out Identity identity)
        {
            try
            {
                identity = Parse(s, uriPath);
                return true;
            }
            catch
            {
                identity = default;
                return false;
            }
        }

        /// <summary>Converts an identity to a normalized URI path.</summary>
        /// <returns>A URI path.</returns>
        public override string ToString()
        {
            if (string.IsNullOrEmpty(Name))
            {
                // This struct was default initialized (null) or poorly initialized (empty name).
                return "/";
            }
            Debug.Assert(Category != null);

            string path = Proxy.NormalizePath(Name);
            if (Name[0] == '/')
            {
                // If Name starts with /, NormalizePath does not add an extra leading /, so we add it back.
                path = $"/{path}";
            }

            if (Category.Length == 0)
            {
                // start with double `/` when normalized name contains `/` to ensure proper round-trip.
                return path[1..].Contains('/') ? $"/{path}" : path;
            }
            else
            {
                return $"/{Uri.EscapeDataString(Category)}{path}";
            }
        }

        /// <summary>Converts an object identity to a string, using the format specified by ToStringMode.</summary>
        /// <param name="mode">Specifies if and how non-printable ASCII characters are escaped in the result. See
        /// <see cref="ToStringMode"/>.</param>
        /// <returns>The string representation of the object identity.</returns>
        public string ToString(ToStringMode mode)
        {
            if (string.IsNullOrEmpty(Name))
            {
                return "";
            }
            Debug.Assert(Category != null);

            string escapedName = StringUtil.EscapeString(Name, mode, '/');

            if (Category.Length == 0)
            {
                return escapedName;
            }
            else
            {
                string escapedCategory = StringUtil.EscapeString(Category, mode, '/');
                return $"{escapedCategory}/{escapedName}";
            }
        }

        internal static Identity? ParsePath(string? path)
        {
            if (path == null)
            {
                return null;
            }

            // Discard leading /
            string[] segments = path.Length > 0 && path[0] == '/' ? path[1..].Split('/') : path.Split('/');
            (string name, string category) = segments.Length switch
            {
                0 => throw new FormatException($"invalid identity `{path}'"),
                1 => (Uri.UnescapeDataString(segments[0]), ""),
                _ => (string.Join('/', segments.Skip(1).Select(s => Uri.UnescapeDataString(s))),
                      Uri.UnescapeDataString(segments[0])),
            };

            return name.Length > 0 ? new Identity(name, category) :
                throw new FormatException($"invalid empty name in identity `{path}'");
        }
    }

    /// <summary>The output mode or format for <see cref="Identity.ToString(ToStringMode)"/>.</summary>
    public enum ToStringMode : byte
    {
        /// <summary>Characters with ordinal values greater than 127 are kept as-is in the resulting string.
        /// Non-printable ASCII characters with ordinal values 127 and below are encoded as \\t, \\n (etc.). This
        /// corresponds to the default mode with Ice 3.7.</summary>
        Unicode,

        /// <summary>Characters with ordinal values greater than 127 are encoded as universal character names in
        /// the resulting string: \\unnnn for BMP characters and \\Unnnnnnnn for non-BMP characters.
        /// Non-printable ASCII characters with ordinal values 127 and below are encoded as \\t, \\n (etc.)
        /// or \\unnnn. This is an optional mode provided by Ice 3.7.</summary>
        ASCII,

        /// <summary>Characters with ordinal values greater than 127 are encoded as a sequence of UTF-8 bytes using
        /// octal escapes. Characters with ordinal values 127 and below are encoded as \\t, \\n (etc.) or
        /// an octal escape. This is the format used by Ice 3.6 and earlier Ice versions.</summary>
        Compat
    }
}

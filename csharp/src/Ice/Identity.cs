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

        /// <summary>Creates an Identity from a URI path.</summary>
        /// <param name="s">A URI path.</param>
        /// <exception cref="FormatException">s is not in the correct format.</exception>
        /// <returns>A new Identity struct.</returns>
        public static Identity Parse(string s) => ParsePath(s)!.Value;

        /// <summary>Creates an Identity from a string in the ice1 format.</summary>
        /// <param name="s">A "stringified identity" in the ice1 format.</param>
        /// <exception cref="FormatException">s is not in the correct format.</exception>
        /// <returns>A new Identity struct.</returns>
        public static Identity ParseIce1(string s)
        {
            // Find unescaped separator; note that the string may contain an escaped backslash before the separator.
            int slash = -1, pos = 0;
            while ((pos = s.IndexOf('/', pos)) != -1)
            {
                int escapes = 0;
                while (pos - escapes > 0 && s[pos - escapes - 1] == '\\')
                {
                    escapes++;
                }

                // We ignore escaped escapes
                if (escapes % 2 == 0)
                {
                    if (slash == -1)
                    {
                        slash = pos;
                    }
                    else
                    {
                        // Extra unescaped slash found.
                        throw new FormatException($"unescaped backslash in identity `{s}'");
                    }
                }
                pos++;
            }

            string category;
            string? name = null;
            if (slash == -1)
            {
                try
                {
                    name = StringUtil.UnescapeString(s, 0, s.Length, "/");
                }
                catch (ArgumentException ex)
                {
                    throw new FormatException($"invalid name in identity `{s}'", ex);
                }
                category = "";
            }
            else
            {
                try
                {
                    category = StringUtil.UnescapeString(s, 0, slash, "/");
                }
                catch (ArgumentException ex)
                {
                    throw new FormatException($"invalid category in identity `{s}'", ex);
                }

                if (slash + 1 < s.Length)
                {
                    try
                    {
                        name = StringUtil.UnescapeString(s, slash + 1, s.Length, "/");
                    }
                    catch (ArgumentException ex)
                    {
                        throw new FormatException($"invalid name in identity `{s}'", ex);
                    }
                }
            }

            return name?.Length > 0 ? new Identity(name, category) :
                throw new FormatException($"invalid empty name in identity `{s}'");
        }

        /// <summary>Attempts to create an Identity from a URI path.</summary>
        /// <param name="s">A URI path.</param>
        /// <param name="identity">When this method succeeds, contains an Identity struct parsed from s.</param>
        /// <returns>True if <c>s</c> was parsed successfully; otherwise, false.</returns>
        public static bool TryParse(string s, out Identity identity)
        {
            try
            {
                identity = Parse(s);
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

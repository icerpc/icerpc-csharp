// Copyright (c) ZeroC, Inc. All rights reserved.

using NUnit.Framework;
using System.Collections.Immutable;

namespace IceRpc.Tests;

/// <summary>Proxy interop tests.</summary>
[Parallelizable(scope: ParallelScope.All)]
public class ProxyTests
{
    /// <summary>Provides test case data for <see cref="Equal_proxies_produce_the_same_hash_code(string, IProxyFormat)"/>
    /// test.</summary>
    private static IEnumerable<TestCaseData> ProxyHashCodeSource
    {
        get
        {
            foreach ((string Str, string _, string _) in _validIceFormatProxies)
            {
                yield return new TestCaseData(Str, IceProxyFormat.Default);
            }
        }
    }

    /// <summary>Provides test case data for <see cref="Parse_an_invalid_proxy(string, IProxyFormat)"/>
    /// test.</summary>
    private static IEnumerable<TestCaseData> ProxyParseInvalidSource
    {
        get
        {
            foreach (string str in _invalidIceFormatProxies)
            {
                yield return new TestCaseData(str, IceProxyFormat.Default);
            }
        }
    }

    /// <summary>Provides test case data for <see cref="Parse_a_proxy_string(string, IProxyFormat, string, string)"/>
    /// test.</summary>
    private static IEnumerable<TestCaseData> ProxyParseSource
    {
        get
        {
            foreach ((string Str, string Path, string Fragment) in _validIceFormatProxies)
            {
                yield return new TestCaseData(Str, IceProxyFormat.Default, Path, Fragment);
            }
        }
    }

    /// <summary>Provides test case data for <see cref="Convert_a_proxy_to_a_string(string, IceProxyFormat)"/> test.
    /// </summary>
    private static IEnumerable<TestCaseData> ProxyToStringSource
    {
        get
        {
            foreach ((string Str, string _, string _) in _validIceFormatProxies)
            {
                yield return new TestCaseData(Str, IceProxyFormat.ASCII);
                yield return new TestCaseData(Str, IceProxyFormat.Compat);
                yield return new TestCaseData(Str, IceProxyFormat.Unicode);
            }
        }
    }

    /// <summary>Provides test case data for
    /// <see cref="Parse_a_proxy_from_an_ice_proxy_string_and_an_ice_format(string, IceProxyFormat)"/> test.
    /// </summary>
    private static IEnumerable<TestCaseData> PathToProxyPathSource
    {
        get
        {
            (string, string)[] testData =
            {
                ("test", "\x7f€"),
                ("banana \x0E-\ud83c\udf4c\u20ac\u00a2\u0024", "greek \ud800\udd6a"),
                ("test", ",X2QNUAzSBcJ_e$AV;E\\"),
                ("test", ",X2QNUAz\\SB\\/cJ_e$AV;E\\\\"),
                ("/test", "cat/"),
            };
            foreach ((string name, string category) in testData)
            {
                var path = $"/{Uri.EscapeDataString(category)}/{Uri.EscapeDataString(name)}";
                foreach (IceProxyFormat format in
                    ImmutableList.Create(IceProxyFormat.Unicode, IceProxyFormat.ASCII, IceProxyFormat.Compat))
                    {
                        yield return new TestCaseData(path, format);
                    }
            }
        }
    }

    /// <summary>Provides test case data for
    /// <see cref="Convert_a_proxy_to_a_string_with_ice_format(string, string, IceProxyFormat)"/> test. </summary>
    private static IEnumerable<TestCaseData> PathToProxyStringSource
    {
        get
        {
            (string, string, string, IceProxyFormat)[] testData =
            {
                (
                    "test",
                    "\x7f€",
                    "\\177\\342\\202\\254/test",
                    IceProxyFormat.Compat
                ),
                (
                    "banana \x0E-\ud83c\udf4c\u20ac\u00a2\u0024",
                    "greek \ud800\udd6a",
                    "\"greek \\360\\220\\205\\252/banana \\016-\\360\\237\\215\\214\\342\\202\\254\\302\\242$\"",
                    IceProxyFormat.Compat
                ),
                (
                    "test",
                    "\x7f€",
                    "\\u007f\\u20ac/test",
                    IceProxyFormat.ASCII
                ),
                (
                    "banana \x0E-\ud83c\udf4c\u20ac\u00a2\u0024",
                    "greek \ud800\udd6a",
                    "\"greek \\U0001016a/banana \\u000e-\\U0001f34c\\u20ac\\u00a2$\"",
                    IceProxyFormat.ASCII
                ),
                (
                    "test",
                    "\x7f€",
                    "\\u007f€/test",
                    IceProxyFormat.Unicode
                ),
                (
                    "test",
                    ",X2QNUAz\\SB\\/cJ_e$AV;E\\\\",
                    ",X2QNUAz\\\\SB\\\\\\/cJ_e$AV;E\\\\\\\\/test",
                    IceProxyFormat.Unicode
                ),
                (
                    "/test",
                    "cat/",
                    "cat\\//\\/test",
                    IceProxyFormat.Unicode
                ),
                (
                    "banana \x0E-\ud83c\udf4c\u20ac\u00a2\u0024",
                    "greek \ud800\udd6a",
                    "\"greek \ud800\udd6a/banana \\u000e-\ud83c\udf4c\u20ac\u00a2$\"",
                    IceProxyFormat.Unicode
                )
            };
            foreach ((string name, string category, string expected, IceProxyFormat format) in testData)
            {
                var path = $"/{Uri.EscapeDataString(category)}/{Uri.EscapeDataString(name)}";
                yield return new TestCaseData(path, expected, format);
            }
        }
    }

    /// <summary>A collection of proxy strings that are invalid for the Ice proxy format.</summary>
    private static readonly string[] _invalidIceFormatProxies = new string[]
        {
            "ice + tcp://host.zeroc.com:foo", // missing host
            "",
            "\"\"",
            "\"\" test", // invalid trailing characters
            "id@server test",
            "id -e A.0:tcp -h foobar",
            "id -f \"facet x",
            "id -f \'facet x",
            "test -f facet@test @test",
            "test -p 2.0",
            "xx\01FooBar", // Illegal character < 32
            "xx\\ud911", // Illegal surrogate
            "test/foo/bar",
            "cat//test"
        };

    /// <summary>A collection of proxy strings that are valid for the Ice proxy format, with its expected path and
    /// fragment.</summary>
    private static readonly (string Str, string Path, string Fragment)[] _validIceFormatProxies = new (string, string, string)[]
        {
            ("ice -t:tcp -h localhost -p 10000", "/ice", ""),
            ("icerpc:ssl -h localhost -p 10000", "/icerpc", ""),
            ("identity:tcp -h \"::0\"", "/identity", ""),
            ("identity:coloc -h *", "/identity", ""),
            ("identity -e 4.5:coloc -h *", "/identity", ""),
            ("name -f facet:coloc -h localhost", "/name", "facet"),
            ("category/name -f facet:coloc -h localhost", "/category/name", "facet"),
            ("cat$gory/nam$ -f fac$t:coloc -h localhost", "/cat%24gory/nam%24", "fac%24t"),
            ("\\342\\x82\\254\\60\\x9\\60\\", "/%E2%82%AC0%090%5C", ""),
            ("bar/foo", "/bar/foo", ""),
            ("foo", "/foo", "")
        };

    /// <summary>Checks that a proxy can be converted into a string using any of the supported formats.</summary>
    /// <param name="str">The string used to create the source proxy.</param>
    /// <param name="format">The proxy format for the string conversion.</param>
    [Test, TestCaseSource(nameof(ProxyToStringSource))]
    public void Convert_a_proxy_to_a_string(string str, IProxyFormat format)
    {
        var proxy = Proxy.Parse(str, format: format);

        string str2 = proxy.ToString(format);

        Assert.That(Proxy.Parse(str2, format: format), Is.EqualTo(proxy));
    }

    /// <summary>Checks that two equal proxies always produce the same hash code.</summary>
    /// <param name="str">The string proxy to test.</param>
    /// <param name="format">The proxy format used by <paramref name="str"/>.</param>
    [Test, TestCaseSource(nameof(ProxyHashCodeSource))]
    public void Equal_proxies_produce_the_same_hash_code(string str, IProxyFormat format)
    {
        var proxy1 = Proxy.Parse(str, format: format);
        var proxy2 = Proxy.Parse(proxy1.ToString(format), format: format);

        var hashCode1 = proxy1.GetHashCode();

        Assert.That(proxy1, Is.EqualTo(proxy2));
        Assert.That(hashCode1, Is.EqualTo(proxy1.GetHashCode()));
        Assert.That(hashCode1, Is.EqualTo(proxy2.GetHashCode()));
    }

    /// <summary>Checks that a string can be correctly parsed as a proxy.</summary>
    /// <param name="str">The string to parse as a proxy.</param>
    /// <param name="format">The format of <paramref name="str"/> string.</param>
    /// <param name="path">The expected path for the parsed proxy.</param>
    /// <param name="fragment">The expected fragment for the parsed proxy.</param>
    [Test, TestCaseSource(nameof(ProxyParseSource))]
    public void Parse_a_proxy_string(string str, IProxyFormat format, string path, string fragment)
    {
        var proxy = Proxy.Parse(str, format: format);

        Assert.That(proxy.Path, Is.EqualTo(path));
        Assert.That(proxy.Fragment, Is.EqualTo(fragment));
    }

    /// <summary>Check that parsing a string that is not valid according the given <paramref name="format"/> throws
    /// <see cref="FormatException"/>.</summary>
    /// <param name="str">The string to parse as a proxy.</param>
    /// <param name="format">The format use to parse the string as a proxy.</param>
    [Test, TestCaseSource(nameof(ProxyParseInvalidSource))]
    public void Parse_an_invalid_proxy(string str, IProxyFormat format) =>
        Assert.Throws(Is.InstanceOf<FormatException>(), () => Proxy.Parse(str, format: format));

    /// <summary>Verifies that a path can be converted to an Ice string with a specified format (using ToString).
    /// </summary>
    /// <param name="path">The path used to create the proxy.</param>
    /// <param name="expected">The "stringified" formatted path to check against.</param>
    /// <param name="format">The format being used to create the proxy.</param>
    [Test, TestCaseSource(nameof(PathToProxyStringSource))]
    public void Convert_a_proxy_to_a_string_with_ice_format(string path, string expected, IceProxyFormat format)
    {
        var proxy = new Proxy(Protocol.Ice) { Path = path };

        string iceProxyString = proxy.ToString(format)[..^10]; // trim " -t -e 1.1"

        Assert.That(iceProxyString, Is.EqualTo(expected));
    }

    /// <summary>Verifies that an Ice proxy string can be parsed into a proxy</summary>
    /// <param name="path">The path used to create the proxy.</param>
    /// <param name="format">The format being used to create the proxy.</param>
    [Test, TestCaseSource(nameof(PathToProxyPathSource))]
    public void Parse_an_ice_proxy_string(string path, IceProxyFormat format)
    {
        var proxy = new Proxy(Protocol.Ice) { Path = path };
        string iceProxyString = proxy.ToString(format);

        var iceProxy = Proxy.Parse(iceProxyString, format: IceProxyFormat.Default);

        Assert.That(iceProxy.Path, Is.EqualTo(path));
    }
}

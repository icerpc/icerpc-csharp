// Copyright (c) ZeroC, Inc. All rights reserved.

using NUnit.Framework;

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
}

// Copyright (c) ZeroC, Inc. All rights reserved.

using NUnit.Framework;
using System.Collections.Immutable;

namespace IceRpc.Tests;

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

            foreach ((string Str, string _, string _) in _validUriFormatProxies)
            {
                yield return new TestCaseData(Str, UriProxyFormat.Instance);
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

            foreach (string str in _invalidUriFormatProxies)
            {
                yield return new TestCaseData(str, UriProxyFormat.Instance);
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

            foreach ((string Str, string Path, string Fragment) in _validUriFormatProxies)
            {
                yield return new TestCaseData(Str, UriProxyFormat.Instance, Path, Fragment);
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

            foreach ((string Str, string _, string _) in _validUriFormatProxies)
            {
                yield return new TestCaseData(Str, UriProxyFormat.Instance);
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

    /// <summary>A collection of proxy strings that are invalid for the the URI proxy format.</summary>
    private static readonly string[] _invalidUriFormatProxies = new string[]
        {
            "", 
            "\"\"",
            "icerpc://host/path?alt-endpoint=", // alt-endpoint authority cannot be empty
            "icerpc://host/path?alt-endpoint=/foo", // alt-endpoint cannot have a path
            "icerpc://host/path?alt-endpoint=icerpc://host", // alt-endpoint cannot have a scheme
            "icerpc:path",                  // bad path
            "icerpc:/host/path#fragment",   // bad fragment
            "icerpc:/path#fragment",        // bad fragment
            "icerpc://user@host/path",      // bad user info
            "ice://host/s1/s2/s3",          // too many slashes in path
            "ice://host/cat/",              // empty identity name
            "ice://host/",                  // empty identity name
            "ice://host//",                 // empty identity name
            "ice:/path?alt-endpoint=foo",   // alt-endpoint proxy parameter
            "ice:/path?adapter-id",         // empty adapter-id
            "ice:/path?adapter-id=foo&foo", // extra parameter
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

    /// <summary>A collection of proxy strings that are valid for the URI proxy format, with its expected path and
    /// fragment.</summary>
    private static readonly (string Str, string Path, string Fragment)[] _validUriFormatProxies = new (string, string, string)[]
        {
            ("icerpc://host.zeroc.com/path?encoding=foo", "/path", ""),
            ("ice://host.zeroc.com/identity#facet", "/identity", "facet"),
            ("ice://host.zeroc.com/identity#facet#?!$x", "/identity", "facet#?!$x"),
            ("ice://host.zeroc.com/identity#", "/identity", ""),
            ("ice://host.zeroc.com/identity#%24%23f", "/identity", "%24%23f"),
            ("ice://host.zeroc.com/identity?xyz=false", "/identity", ""),
            ("ice://host.zeroc.com/identity?xyz=true", "/identity", ""),
            ("ice:/path?adapter-id=foo", "/path", ""),
            ("icerpc://host.zeroc.com:1000/category/name", "/category/name", ""),
            ("icerpc://host.zeroc.com:1000/loc0/loc1/category/name", "/loc0/loc1/category/name", ""),
            ("icerpc://host.zeroc.com/category/name%20with%20space", "/category/name%20with%20space", ""),
            ("icerpc://host.zeroc.com/category/name with space", "/category/name%20with%20space", ""),
            ("icerpc://host.zeroc.com//identity", "//identity", ""),
            ("icerpc://host.zeroc.com//identity?alt-endpoint=host2.zeroc.com", "//identity", ""),
            ("icerpc://host.zeroc.com//identity?alt-endpoint=host2.zeroc.com:10000", "//identity", ""),
            ("icerpc://[::1]:10000/identity?alt-endpoint=host1:10000,host2,host3,host4", "/identity", ""),
            ("icerpc://[::1]:10000/identity?alt-endpoint=host1:10000&alt-endpoint=host2,host3&alt-endpoint=[::2]", 
             "/identity", 
             ""),
            ("icerpc://[::1]/path?alt-endpoint=host1?adapter-id=foo=bar$name=value&alt-endpoint=host2?foo=bar$123=456",
             "/path",
             ""),
            ("ice:/location/identity#facet", "/location/identity", "facet"),
            ("ice:///location/identity#facet", "/location/identity", "facet"), // we tolerate an empty host
            ("icerpc://host.zeroc.com//identity", "//identity", ""),
            ("ice://host.zeroc.com/\x7f€$%/!#$'()*+,:;=@[] %2F", "/%7F%E2%82%AC$%25/!", "$'()*+,:;=@[]%20%2F"),
            // TODO: add test with # in fragment
            ("ice://host.zeroc.com/identity#\x7f€$%/!$'()*+,:;=@[] %2F", "/identity", "%7F%E2%82%AC$%25/!$'()*+,:;=@[]%20%2F"),
            (@"icerpc://host.zeroc.com/foo\bar\n\t!", "/foo/bar/n/t!", ""), // \ becomes / another syntax for empty port
            ("icerpc://host.zeroc.com:/identity", "/identity", ""),
            ("icerpc://com.zeroc.ice/identity?transport=iaps&option=a,b%2Cb,c&option=d", "/identity", ""),
            ("icerpc://host.zeroc.com/identity?transport=100", "/identity", ""),
            // leading :: to make the address IPv6-like
            ("icerpc://[::ab:cd:ef:00]/identity?transport=bt", "/identity", ""),
            ("icerpc://host.zeroc.com:10000/identity?transport=tcp", "/identity", ""),
            ("icerpc://host.zeroc.com/identity?transport=ws&option=/foo%2520/bar", "/identity", ""),
            ("icerpc://mylocation.domain.com/foo/bar?transport=loc", "/foo/bar", ""),
            ("icerpc://host:10000?transport=coloc", "/", ""),
            ("icerpc:/tcp -p 10000", "/tcp%20-p%2010000", ""), // not recommended
            ("icerpc://host.zeroc.com/identity?transport=ws&option=/foo%2520/bar", "/identity", ""),
            ("ice://0.0.0.0/identity#facet", "/identity", "facet"), // Any IPv4 in proxy endpoint (unusable but parses ok)
            ("ice://[::0]/identity#facet", "/identity", "facet"), // Any IPv6 in proxy endpoint (unusable but parses ok)
            // IDN
            ("icerpc://München-Ost:10000/path", "/path", ""),
            ("icerpc://xn--mnchen-ost-9db.com/path", "/path", ""),
            // relative proxies
            ("/foo/bar", "/foo/bar", ""),
            ("//foo/bar", "//foo/bar", ""),
            ("/foo:bar", "/foo:bar", ""),
            // non-supported protocols
            ("foobar://host:10000/path", "/path", ""),
            ("foobar://host/path#fragment", "/path", "fragment"),
            ("foobar:path", "path", ""),  // not a valid path since it doesn't start with /, and that's ok
            ("foobar:path#fragment", "path", "fragment"),
        };

    /// <summary>Checks that a proxy can be converted into a string using any of the supported formats.</summary>
    /// <param name="str">The string used to create the source proxy.</param>
    /// <param name="format">The proxy format for the string conversion.</param>
    [Test, TestCaseSource(nameof(ProxyToStringSource))]
    public void Convert_a_proxy_to_a_string(string str, IProxyFormat format)
    {
        // Arrange
        var proxy = Proxy.Parse(str, format: format);

        // Act
        string str2 = proxy.ToString(format);

        // Assert
        Assert.That(Proxy.Parse(str2, format: format), Is.EqualTo(proxy));
    }

    /// <summary>Checks that two equal proxies always produce the same hash code.</summary>
    /// <param name="str">The string proxy to test.</param>
    /// <param name="format">The proxy format used by <paramref name="str"/>.</param>
    [Test, TestCaseSource(nameof(ProxyHashCodeSource))]
    public void Equal_proxies_produce_the_same_hash_code(string str, IProxyFormat format)
    {
        // Arrange
        var proxy1 = Proxy.Parse(str, format: format);
        var proxy2 = Proxy.Parse(proxy1.ToString(format), format: format);

        // Act
        var hashCode1 = proxy1.GetHashCode();

        // Assert
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
        // Act
        var proxy = Proxy.Parse(str, format: format);

        // Assert
        Assert.That(proxy.Path, Is.EqualTo(path));
        Assert.That(proxy.Fragment, Is.EqualTo(fragment));
    }

    /// <summary>Check that parsing a string that is not valid according the given <paramref name="format"/> throws 
    /// <see cref="FormatException"/>.</summary>
    /// <param name="str">The string to parse as a proxy.</param>
    /// <param name="format">The format use to parse the string as a proxy.</param>
    [Test, TestCaseSource(nameof(ProxyParseInvalidSource))]
    public void Parse_an_invalid_proxy(string str, IProxyFormat format)
    {
        // Act
        var act = () => Proxy.Parse(str, format: format);

        // Assert
        Assert.Throws(Is.InstanceOf<FormatException>(), () => act());
    }

    /// <summary>Test that setting the alt endpoints containing endpoints that uses a protocol different than the proxy
    /// protocol throws <see cref="ArgumentException"/>.
    [Test]
    public void Set_the_alt_endpoints_using_a_diferent_protocol_fails()
    {
        // Arrange
        var prx = Proxy.Parse("hello:tcp -h localhost -p 10000", format: IceProxyFormat.Default);
        var endpoint1 = Proxy.Parse("hello:tcp -h localhost -p 10000", format: IceProxyFormat.Default).Endpoint!.Value;
        var endpoint2 = Proxy.Parse("icerpc://host.zeroc.com/hello").Endpoint!.Value;
        var altEndpoints = new Endpoint[] { endpoint1, endpoint2 }.ToImmutableList();

        // Act
        var act = () => prx.AltEndpoints = altEndpoints;

        // Assert
        Assert.Throws<ArgumentException>(() => act());
        Assert.That(prx.Protocol, Is.EqualTo(endpoint1!.Protocol));
        Assert.That(prx.Protocol, Is.Not.EqualTo(endpoint2!.Protocol));
    }

    /// <summary>Test that setting an endpoint that uses a protocol different than the proxy protocol
    /// throws <see cref="ArgumentException"/>.
    [Test]
    public void Set_the_endpoint_using_a_diferent_protocol_fails()
    {
        // Arrange
        var prx = Proxy.Parse("hello:tcp -h localhost -p 10000", format: IceProxyFormat.Default);
        var endpoint = Proxy.Parse("icerpc://host.zeroc.com/hello").Endpoint!.Value;

        // Act
        var act = () => prx.Endpoint = endpoint;

        // Assert
        Assert.Throws<ArgumentException>(() => act());
        Assert.That(prx.Protocol, Is.Not.EqualTo(endpoint!.Protocol));
    }
}

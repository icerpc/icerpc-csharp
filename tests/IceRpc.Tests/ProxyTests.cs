// Copyright (c) ZeroC, Inc. All rights reserved.

using NUnit.Framework;

namespace IceRpc.Tests;

[Parallelizable(scope: ParallelScope.All)]
public class ProxyTests
{
    /// <summary>Provides test case data for <see cref="Parse_a_proxy_string_with_ice_format"/> test.</summary>
    public static IEnumerable<TestCaseData> ParseIceFormatProxySource
    {
        get
        {
            foreach ((string Str, string Path, string Fragment) in _validIceFormatProxies)
            {
                yield return new TestCaseData(Str, Path, Fragment);
            }
        }
    }

    /// <summary>Provides test case data for <see cref="Parse_a_proxy_string_with_ice_format"/> test.</summary>
    public static IEnumerable<TestCaseData> ParseUriFormatProxySource
    {
        get
        {
            foreach ((string Str, string Path, string Fragment) in _validUriFormatProxies)
            {
                yield return new TestCaseData(Str, Path, Fragment);
            }
        }
    }

    /// <summary>Provides test case data for <see cref="Convert_a_proxy_using_ice_format_to_a_string(string, IceProxyFormat)"/> test.</summary>
    public static IEnumerable<TestCaseData> ToStringIceFormatProxySource
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

    /// <summary>Provides test case data for <see cref="Convert_a_proxy_using_uri_format_to_a_string(string, IceProxyFormat)"/> test.</summary>
    public static IEnumerable<TestCaseData> ToStringUriFormatProxySource
    {
        get
        {
            foreach ((string Str, string _, string _) in _validUriFormatProxies)
            {
                yield return new TestCaseData(Str);
            }
        }
    }

    /// <summary>A collection of valid proxy strings using the default Ice proxy format, with its expected Path
    /// and Fragment.</summary>
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

    /// <summary>A collection of valid proxy strings using the URI proxy format, with its expected Path and Fragment.</summary>
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

    /// <summary>Check that a proxy using Ice format can be converted into a string.</summary>
    /// <param name="str">The string used to create the proxy.</param>
    /// <param name="format">The proxy format for the string conversion.</param>
    [Test, TestCaseSource(nameof(ToStringIceFormatProxySource))]
    public void Convert_a_proxy_using_ice_format_to_a_string(string str, IceProxyFormat format)
    {
        // Arrange
        var proxy = Proxy.Parse(str, format: IceProxyFormat.Default);

        // Act
        string str2 = proxy.ToString(format);

        // Assert
        Assert.That(Proxy.Parse(str2, format: IceProxyFormat.Default), Is.EqualTo(proxy));
    }

    /// <summary>Check that a proxy using Ice format can be converted into a string.</summary>
    /// <param name="str">The string used to create the proxy.</param>
    /// <param name="format">The proxy format for the string conversion.</param>
    [Test, TestCaseSource(nameof(ToStringUriFormatProxySource))]
    public void Convert_a_proxy_using_uri_format_to_a_string(string str)
    {
        // Arrange
        var proxy = Proxy.Parse(str);

        // Act
        string str2 = proxy.ToString();

        // Assert
        Assert.That(Proxy.Parse(str2), Is.EqualTo(proxy));
    }

    /// <summary>Tests that parsing an invalid proxy fails with <see cref="FormatException"/>.</summary>
    /// <param name="str">The string to parse as a proxy.</param>
    [TestCase("ice + tcp://host.zeroc.com:foo")] // missing host
    [TestCase("")]
    [TestCase("\"\"")]
    [TestCase("\"\" test")] // invalid trailing characters
    [TestCase("id@server test")]
    [TestCase("id -e A.0:tcp -h foobar")]
    [TestCase("id -f \"facet x")]
    [TestCase("id -f \'facet x")]
    [TestCase("test -f facet@test @test")]
    [TestCase("test -p 2.0")]
    [TestCase("xx\01FooBar")] // Illegal character < 32
    [TestCase("xx\\ud911")] // Illegal surrogate
    [TestCase("test/foo/bar")]
    [TestCase("cat//test")]
    [TestCase("cat/")] // Empty name
    public void Parse_a_proxy_string_with_invalid_ice_format(string str) =>
        Assert.Throws<FormatException>(() => Proxy.Parse(str, format: IceProxyFormat.Default));

    /// <summary>Tests that parsing an invalid proxy fails with <see cref="FormatException"/>.</summary>
    /// <param name="str">The string to parse as a proxy.</param>
    [TestCase("")]
    [TestCase("\"\"")]
    [TestCase("icerpc://host/path?alt-endpoint=")] // alt-endpoint authority cannot be empty
    [TestCase("icerpc://host/path?alt-endpoint=/foo")] // alt-endpoint cannot have a path
    [TestCase("icerpc://host/path?alt-endpoint=icerpc://host")] // alt-endpoint cannot have a scheme
    [TestCase("icerpc:path")]                  // bad path
    [TestCase("icerpc:/host/path#fragment")]   // bad fragment
    [TestCase("icerpc:/path#fragment")]        // bad fragment
    [TestCase("icerpc://user@host/path")]      // bad user info
    [TestCase("ice://host/s1/s2/s3")]          // too many slashes in path
    [TestCase("ice://host/cat/")]              // empty identity name
    [TestCase("ice://host/")]                  // empty identity name
    [TestCase("ice://host//")]                 // empty identity name
    [TestCase("ice:/path?alt-endpoint=foo")]   // alt-endpoint proxy parameter
    [TestCase("ice:/path?adapter-id")]         // empty adapter-id
    [TestCase("ice:/path?adapter-id=foo&foo")] // extra parameter
    public void Parse_a_proxy_string_with_invalid_uri_format(string str) =>
        Assert.Catch<FormatException>(() => Proxy.Parse(str));

    /// <summary>Check that a string using the Ice proxy format can be correctly parse as a proxy.</summary>
    /// <param name="str">The string to parse as a proxy.</param>
    /// <param name="path">The expected path.</param>
    /// <param name="fragment">The expected fragment.</param>
    [Test, TestCaseSource(nameof(ParseIceFormatProxySource))]
    public void Parse_a_proxy_string_with_ice_format(string str, string path, string fragment)
    {
        // Act
        var proxy = Proxy.Parse(str, format: IceProxyFormat.Default);

        // Assert
        Assert.That(proxy.Protocol, Is.EqualTo(Protocol.Ice));
        Assert.That(proxy.Path, Is.EqualTo(path));
        Assert.That(proxy.Fragment, Is.EqualTo(fragment));
    }

    /// <summary>Check that a string using the URI proxy format can be correctly parse as a proxy.</summary>
    /// <param name="str">The string to parse.</param>
    /// <param name="path">The expected <see cref="Proxy.Path"/> for the proxy or null.</param>
    /// <param name="fragment">The expected <see cref="Proxy.Fragment"/> for the proxy or null.</param>
    [Test, TestCaseSource(nameof(ParseUriFormatProxySource))]
    public void Parse_a_proxy_string_with_uri_format(string str, string path, string fragment)
    {
        // Act
        var proxy = Proxy.Parse(str);

        // Assert
        Assert.That(proxy.Path, Is.EqualTo(path));
        Assert.That(proxy.Fragment, Is.EqualTo(fragment));
    }
}

// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Configure;
using IceRpc.Slice;
using IceRpc.Transports;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using System.Collections.Immutable;

namespace IceRpc.Tests;

[Parallelizable(scope: ParallelScope.All)]
public class ProxyTests
{
    /// <summary>Provides test case data for <see cref="Equal_proxies_produce_the_same_hash_code(string)"/>
    /// test.</summary>
    private static IEnumerable<TestCaseData> ProxyHashCodeSource
    {
        get
        {
            foreach ((string Str, string _, string _) in _validUriFormatProxies)
            {
                yield return new TestCaseData(Str);
            }
        }
    }

    /// <summary>Provides test case data for <see cref="Parse_an_invalid_proxy_string(string)"/>
    /// test.</summary>
    private static IEnumerable<TestCaseData> ProxyParseInvalidSource
    {
        get
        {
            foreach (string str in _invalidUriFormatProxies)
            {
                yield return new TestCaseData(str);
            }
        }
    }

    /// <summary>Provides test case data for <see cref="Parse_a_proxy_string(string, string, string)"/>
    /// test.</summary>
    private static IEnumerable<TestCaseData> ProxyParseSource
    {
        get
        {
            foreach ((string Str, string Path, string Fragment) in _validUriFormatProxies)
            {
                yield return new TestCaseData(Str, Path, Fragment);
            }
        }
    }

    /// <summary>Provides test case data for <see cref="Convert_a_proxy_to_a_string(string)"/> test.
    /// </summary>
    private static IEnumerable<TestCaseData> ProxyToStringSource
    {
        get
        {
            foreach ((string Str, string _, string _) in _validUriFormatProxies)
            {
                yield return new TestCaseData(Str);
            }
        }
    }

    /// <summary>Provides test case data for <see cref="Parse_proxy_alt_endpoints(string)"/> test.
    /// </summary>
    private static IEnumerable<TestCaseData> AltEndpointsSource
    {
        get
        {
            foreach ((string str, Endpoint[] altEntpoints) in _altEdpoints)
            {
                yield return new TestCaseData(str, altEntpoints);
            }
        }
    }

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

    private static readonly Dictionary<string, Endpoint[]> _altEdpoints = new()
    {
        ["icerpc://localhost/path?alt-endpoint=host1,host2"] = new Endpoint[]
        {
            new Endpoint { Host = "host1" },
            new Endpoint { Host = "host2" },
        },
        ["icerpc://localhost/path?alt-endpoint=host1:10001,host2:10002"] = new Endpoint[]
        {
            new Endpoint { Host = "host1", Port = 10001 },
            new Endpoint { Host = "host2", Port = 10002 },
        },
        ["icerpc://localhost/path?alt-endpoint=host1:10001&alt-endpoint=host2:10002"] = new Endpoint[]
        {
            new Endpoint { Host = "host1", Port = 10001 },
            new Endpoint { Host = "host2", Port = 10002 },
        },
    };

    /// <summary>Verifies that adapter-id param cannot be set to an empty value.</summary>
    [Test]
    public void Adapter_id_cannot_be_empty()
    {
        // Arrange
        var proxy = Proxy.Parse("ice://localhost/hello");

        // Act/Assert
        Assert.That(() => proxy.Params = proxy.Params.SetItem("adapter-id", ""), Throws.ArgumentException);
    }

    /// <summary>Verifies that the proxy endpoint cannot be set when the proxy contains any params.</summary>
    [Test]
    public void Cannot_set_endpoint_on_a_proxy_with_parameters()
    {
        // Arrange
        var proxy = new Proxy(Protocol.Ice)
        {
            Params = new Dictionary<string, string> { ["adapter-id"] = "value" }.ToImmutableDictionary(),
        };

        // Act/Assert
        Assert.That(
            () => proxy.Endpoint = new Endpoint(proxy.Protocol) { Host = "localhost" },
            Throws.TypeOf<InvalidOperationException>());
    }

    /// <summary>Verifies that the "fragment" cannot be set when the protocol has no fragment.</summary>
    [TestCase("icerpc")]
    [TestCase("")]
    public void Cannot_set_fragment_if_protocol_has_no_fragment(string protocolName)
    {
        var protocol = Protocol.FromString(protocolName);
        var proxy = new Proxy(protocol);

        Assert.That(
            () => proxy = proxy with { Fragment = "bar" },
            Throws.TypeOf<InvalidOperationException>());

        Assert.That(protocol.HasFragment, Is.False);
    }

    /// <summary>Verifies that the proxy params cannot be set when the proxy has an endpoint.</summary>
    [Test]
    public void Cannot_set_params_on_a_proxy_with_endpoints()
    {
        var proxy = Proxy.Parse("icerpc://localhost/hello");

        Assert.That(
            () => proxy.Params = proxy.Params.Add("name", "value"),
            Throws.TypeOf<InvalidOperationException>());
    }

    /// <summary>Verifies that a proxy can be converted into a string using any of the supported formats.</summary>
    /// <param name="str">The string used to create the source proxy.</param>
    /// <param name="format">The proxy format for the string conversion.</param>
    [Test, TestCaseSource(nameof(ProxyToStringSource))]
    public void Convert_a_proxy_to_a_string(string str)
    {
        var proxy = Proxy.Parse(str);

        string str2 = proxy.ToString();

        Assert.That(Proxy.Parse(str2), Is.EqualTo(proxy));
    }

    /// <summary>Verifies that two equal proxies always produce the same hash code.</summary>
    /// <param name="str">The string proxy to test.</param>
    [Test, TestCaseSource(nameof(ProxyHashCodeSource))]
    public void Equal_proxies_produce_the_same_hash_code(string str)
    {
        var proxy1 = Proxy.Parse(str);
        var proxy2 = Proxy.Parse(proxy1.ToString());

        var hashCode1 = proxy1.GetHashCode();

        Assert.That(proxy1, Is.EqualTo(proxy2));
        Assert.That(hashCode1, Is.EqualTo(proxy1.GetHashCode()));
        Assert.That(hashCode1, Is.EqualTo(proxy2.GetHashCode()));
    }

    /// <summary>Verifies that a proxy created from a client connection has the expected path, connection and endpoint
    /// properties.</summary>
    [Test]
    public async Task From_connection_with_a_client_connection()
    {
        await using var connection = new Connection(new Endpoint(Protocol.IceRpc));

        var proxy = Proxy.FromConnection(connection, "/");

        Assert.Multiple(() =>
        {
            Assert.That(proxy.Path, Is.EqualTo("/"));
            Assert.That(proxy.Connection, Is.EqualTo(connection));
            Assert.That(proxy.Endpoint, Is.EqualTo(connection.RemoteEndpoint));
        });
    }

    /// <summary>Verifies that a proxy created from a server connection has the expected path, connection and endpoint
    /// properties.</summary>
    [Test]
    public async Task From_connection_with_a_server_connection()
    {
        // Arrange
        await using var networkConnection = new MockNetworkConnection();
        await using var serverConnection = new Connection(networkConnection, Protocol.IceRpc, new ConnectionOptions());

        // Act
        var proxy = Proxy.FromConnection(serverConnection, "/");

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(proxy.Path, Is.EqualTo("/"));
            Assert.That(proxy.Connection, Is.EqualTo(serverConnection));
            Assert.That(proxy.Endpoint, Is.Null);
        });
    }

    /// <summary>Verifies that a proxy created from a path has the expected protocol, path and endpoint properties.
    /// </summary>
    [TestCase("/")]
    [TestCase("/foo/bar/")]
    public void From_path(string path)
    {
        var proxy = Proxy.FromPath(path);

        Assert.Multiple(() =>
        {
            Assert.That(proxy.Protocol, Is.EqualTo(Protocol.Relative));
            Assert.That(proxy.Path, Is.EqualTo(path));
            Assert.That(proxy.Endpoint, Is.Null);
        });
    }

    /// <summary>Verifies that a string can be correctly parsed as a proxy.</summary>
    /// <param name="str">The string to parse as a proxy.</param>
    /// <param name="format">The format of <paramref name="str"/> string.</param>
    /// <param name="path">The expected path for the parsed proxy.</param>
    /// <param name="fragment">The expected fragment for the parsed proxy.</param>
    [Test, TestCaseSource(nameof(ProxyParseSource))]
    public void Parse_a_proxy_string(string str, string path, string fragment)
    {
        var proxy = Proxy.Parse(str);

        Assert.That(proxy.Path, Is.EqualTo(path));
        Assert.That(proxy.Fragment, Is.EqualTo(fragment));
    }

    /// <summary>Verifies that parsing a string that is not valid according the given <paramref name="format"/> throws
    /// <see cref="FormatException"/>.</summary>
    /// <param name="str">The string to parse as a proxy.</param>
    /// <param name="format">The format use to parse the string as a proxy.</param>
    [Test, TestCaseSource(nameof(ProxyParseInvalidSource))]
    public void Parse_an_invalid_proxy_string(string str) =>
        Assert.Throws(Is.InstanceOf<FormatException>(), () => Proxy.Parse(str));

    [Test, TestCaseSource(nameof(AltEndpointsSource))]
    public void Parse_proxy_alt_endpoints(string str, Endpoint[] altEndpoints)
    {
        var proxy = Proxy.Parse(str);

        Assert.That(proxy.AltEndpoints, Is.EqualTo(altEndpoints));
    }

    /// <summary>Verifies that the proxy encoding is set from the parsed encoding parameter.</summary>
    /// <param name="str">The proxy string to parse.</param>
    /// <param name="encoding">The expected encoding.</param>
    [TestCase("ice://localhost/foo?encoding=1.0", "1.0")]
    [TestCase("ice://localhost/foo?encoding=1.1", "1.1")]
    [TestCase("ice://localhost/foo?encoding=2.0", "2.0")]
    [TestCase("ice://localhost/foo?encoding=json", "json")]
    public void Parse_proxy_encoding(string str, string encodingStr)
    {
        var encoding = Encoding.FromString(encodingStr);

        var proxy = Proxy.Parse(str);

        Assert.That(proxy.Encoding, Is.EqualTo(encoding));
    }

    /// <summary>Verifies that the proxy invoker of the <see cref="SliceDecodePayloadOptions"/> is used for proxies
    /// received over an incoming connection.</summary>
    [Test]
    public async Task Proxy_invoker_is_set_to_the_slice_decode_options_feature_proxy_invoker()
    {
        var service = new SendProxyTest();
        var pipeline = new Pipeline();
        await using ServiceProvider serviceProvider = new IntegrationTestServiceCollection()
            .AddTransient<IDispatcher>(_ =>
            {
                var router = new Router();
                router.Map<ISendProxyTest>(service);
                router.UseFeature(new SliceDecodePayloadOptions { ProxyInvoker = pipeline });
                return router;
            })
            .BuildServiceProvider();
        var prx = SendProxyTestPrx.FromConnection(serviceProvider.GetRequiredService<Connection>());

        await prx.SendProxyAsync(prx);

        Assert.That(service.ReceivedPrx, Is.Not.Null);
        Assert.That(service.ReceivedPrx.Value.Proxy.Invoker, Is.EqualTo(pipeline));
    }

    /// <summary>Verifies that a proxy received over an incoming connection uses the default invoker.</summary>
    [Test]
    public async Task Proxy_received_over_an_incoming_connection_uses_the_default_invoker()
    {
        var service = new SendProxyTest();
        await using ServiceProvider serviceProvider = new IntegrationTestServiceCollection()
            .AddTransient<IDispatcher>(_ => service)
            .BuildServiceProvider();
        var prx = SendProxyTestPrx.FromConnection(serviceProvider.GetRequiredService<Connection>());

        await prx.SendProxyAsync(prx);

        Assert.That(service.ReceivedPrx, Is.Not.Null);
        Assert.That(service.ReceivedPrx.Value.Proxy.Invoker, Is.EqualTo(Proxy.DefaultInvoker));
    }

    /// <summary>Verifies that a proxy received over an outgoing connection inherits the callers invoker.</summary>
    [Test]
    public async Task Proxy_received_over_an_outgoing_connection_inherits_the_callers_invoker()
    {
        await using ServiceProvider serviceProvider = new IntegrationTestServiceCollection()
            .AddTransient<IDispatcher>(_ => new ReceiveProxyTest())
            .BuildServiceProvider();
        var invoker = new Pipeline();
        var prx = ReceiveProxyTestPrx.FromConnection(
            serviceProvider.GetRequiredService<Connection>(),
            invoker: invoker);

        ReceiveProxyTestPrx received = await prx.ReceiveProxyAsync();

        Assert.That(received.Proxy.Invoker, Is.EqualTo(invoker));
    }

    /// <summary>Verifies that setting the alt endpoints containing endpoints that uses a protocol different than the
    /// proxy protocol throws <see cref="ArgumentException"/>.</summary>
    [Test]
    public void Set_the_alt_endpoints_using_a_diferent_protocol_fails()
    {
        var prx = Proxy.Parse("ice://host.zeroc.com:10000/hello");
        var endpoint1 = Proxy.Parse("ice://host.zeroc.com:10001/hello").Endpoint!.Value;
        var endpoint2 = Proxy.Parse("icerpc://host.zeroc.com/hello").Endpoint!.Value;
        var altEndpoints = new Endpoint[] { endpoint1, endpoint2 }.ToImmutableList();

        Assert.Throws<ArgumentException>(() => prx.AltEndpoints = altEndpoints);

        // Ensure the alt endpoints weren't updated
        Assert.That(prx.AltEndpoints, Is.Empty);
    }

    /// <summary>Verifies that setting an endpoint that uses a protocol different than the proxy protocol throws
    /// <see cref="ArgumentException"/>.</summary>
    [Test]
    public void Set_the_endpoint_using_a_diferent_protocol_fails()
    {
        var prx = Proxy.Parse("ice://host.zeroc.com/hello");
        var endpoint = prx.Endpoint;
        var newEndpoint = Proxy.Parse("icerpc://host.zeroc.com/hello").Endpoint!.Value;

        Assert.Throws<ArgumentException>(() => prx.Endpoint = newEndpoint);

        // Ensure the endpoint wasn't updated
        Assert.That(prx.Endpoint, Is.EqualTo(endpoint));
    }

    /// <summary>Verifies that we can set the fragment on an ice proxy.</summary>
    [Test]
    public void Set_fragment_on_an_ice_proxy()
    {
        var proxy = new Proxy(Protocol.Ice);

        proxy = proxy with { Fragment = "bar" };

        Assert.That(proxy.Fragment, Is.EqualTo("bar"));
        Assert.That(proxy.Protocol.HasFragment, Is.True);
    }

    /// <summary>INetworkConnection mock used to create a server connection.</summary>
    private class MockNetworkConnection : INetworkConnection
    {
        public TimeSpan LastActivity => throw new NotImplementedException();

        public Task<NetworkConnectionInformation> ConnectAsync(CancellationToken cancel) => throw new NotImplementedException();
        public ValueTask DisposeAsync() => default;
        public bool HasCompatibleParams(Endpoint remoteEndpoint) => throw new NotImplementedException();
    }

    private class ReceiveProxyTest : Service, IReceiveProxyTest
    {
        public ValueTask<ReceiveProxyTestPrx> ReceiveProxyAsync(Dispatch dispatch, CancellationToken cancel) =>
            new(ReceiveProxyTestPrx.FromPath("/hello"));
    }

    private class SendProxyTest : Service, ISendProxyTest
    {
        public SendProxyTestPrx? ReceivedPrx { get; private set; }

        public ValueTask SendProxyAsync(
            SendProxyTestPrx proxy,
            Dispatch dispatch,
            CancellationToken cancel = default)
        {
            ReceivedPrx = proxy;
            return default;
        }
    }
}

// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Features;
using IceRpc.Slice;
using IceRpc.Tests.Common;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using System.Collections.Immutable;

namespace IceRpc.Tests;

[Parallelizable(scope: ParallelScope.All)]
public class ServiceAddressTests
{
    /// <summary>Provides test case data for <see cref="Equal_service_addresses_produce_the_same_hash_code(string)"/>
    /// test.</summary>
    private static IEnumerable<TestCaseData> ServiceAddressHashCodeSource
    {
        get
        {
            foreach ((string Str, string _, string _) in _validUriFormatProxies)
            {
                yield return new TestCaseData(Str);
            }
        }
    }

    /// <summary>Provides test case data for <see cref="Parse_an_invalid_service_address_string(string)"/>
    /// test.</summary>
    private static IEnumerable<TestCaseData> ServiceAddressParseInvalidSource
    {
        get
        {
            foreach (string str in _invalidUriFormatProxies)
            {
                yield return new TestCaseData(str);
            }
        }
    }

    /// <summary>Provides test case data for <see cref="Parse_a_service_address_string(string, string, string)"/>
    /// test.</summary>
    private static IEnumerable<TestCaseData> ServiceAddressParseSource
    {
        get
        {
            foreach ((string Str, string Path, string Fragment) in _validUriFormatProxies)
            {
                yield return new TestCaseData(Str, Path, Fragment);
            }
        }
    }

    /// <summary>Provides test case data for <see cref="Convert_a_service_address_to_a_string(string)"/> test.
    /// </summary>
    private static IEnumerable<TestCaseData> ServiceAddressToStringSource
    {
        get
        {
            foreach ((string Str, string _, string _) in _validUriFormatProxies)
            {
                yield return new TestCaseData(Str);
            }
        }
    }

    private static IEnumerable<TestCaseData> ServiceAddressToStringWithSetupSource
    {
        get
        {
            foreach ((ServiceAddress serviceAddress, string expected) in ServiceAddressToStringData)
            {
                yield return new TestCaseData(serviceAddress, expected);
            }
        }
    }

    /// <summary>Provides test case data for <see cref="Parse_service_address_alt_endpoints(string)"/> test.
    /// </summary>
    private static IEnumerable<TestCaseData> AltEndpointsSource
    {
        get
        {
            foreach ((string str, Endpoint[] altEndpoints) in _altEndpoints)
            {
                yield return new TestCaseData(str, altEndpoints);
            }
        }
    }

    private static IEnumerable<TestCaseData> ServiceAddressEqualitySource
    {
        get
        {
            foreach ((ServiceAddress serviceAddress1, ServiceAddress? serviceAddress2, bool expected) in ServiceAddressEqualityData)
            {
                yield return new TestCaseData(serviceAddress1, serviceAddress2, expected);
            }
        }
    }

    private static IEnumerable<TestCaseData> ServiceAddressToUriSource {
        get {
            foreach ((ServiceAddress serviceAddress, string expected) in ServiceAddressToUriData)
            {
                yield return new TestCaseData(serviceAddress, expected);
            }
        }
    }

    private static (ServiceAddress, ServiceAddress?, bool)[] ServiceAddressEqualityData {
        get {
            ServiceAddress serviceAddress = new ServiceAddress(Protocol.Ice) with { Path = "/foo" };
            return new[] {
                (serviceAddress, serviceAddress, true),
                (serviceAddress, null, false),
                (serviceAddress, new ServiceAddress(Protocol.IceRpc), false), // Different protocol.
                (new ServiceAddress() with {Path = "/foo"}, new ServiceAddress() with {Path = "/bar"}, false),  // Relative service addresses
                (new ServiceAddress(new Uri("foo://host/123")), new ServiceAddress(new Uri("foo://host/123")), true)  // Unsupported protocol.
            };
        }
    }

    private static(ServiceAddress, string)[] ServiceAddressToStringData {
        get {
             // Service address with alt endpoints
            var serviceAddressWithAltEndpoints = new ServiceAddress(new Uri("ice://localhost:8080/foo?abc=123#bar"));
            serviceAddressWithAltEndpoints.AltEndpoints = ImmutableList.Create(
                Endpoint.FromString("ice://localhost:10000?transport=fizz"),
                Endpoint.FromString("ice://localhost:10101?transport=buzz")
            );

            // Service address with Params
            var serviceAddressWithParams = new ServiceAddress(Protocol.IceRpc);
            var myParams = new Dictionary<string, string> { { "foo", "bar" } }.ToImmutableDictionary();
            serviceAddressWithParams = serviceAddressWithParams with { Params = myParams };

            return new[] {
                (
                    serviceAddressWithAltEndpoints,
                    "ice://localhost:8080/foo?abc=123&alt-endpoint=localhost:10000?transport=fizz,localhost:10101?transport=buzz#bar"
                ),
                (
                    serviceAddressWithParams,
                    "icerpc:/?foo=bar"
                )
            };
        }
    }

    private static (ServiceAddress, string)[] ServiceAddressToUriData {
        get {
            var serviceAddress = new ServiceAddress(new Uri("ice://localhost:8080/foo?abc=123#bar"));
            ServiceAddress relativeServiceAddress = new ServiceAddress() with { Path = "/foo" };
            ServiceAddress protocolRelativeServiceAddress = new ServiceAddress(Protocol.IceRpc) with { Path = "/foo" };
            return new (ServiceAddress, string)[] {
                (serviceAddress, new Uri("ice://localhost:8080/foo?abc=123#bar").ToString()), // OriginalUri set
                (relativeServiceAddress, new Uri("/foo", UriKind.Relative).ToString()), // Relative service address with no protocol
                (protocolRelativeServiceAddress, "icerpc:/foo"), // Protocol relative service address
            };
        }
    }

    /// <summary>A collection of service address strings that are invalid.</summary>
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
            "ice:/path?alt-endpoint=foo",   // alt-endpoint service address parameter
            "ice:/path?adapter-id",         // empty adapter-id
            "ice:/path?adapter-id=foo&foo", // extra parameter
        };

    /// <summary>A collection of service address URI strings that are valid, with its expected path and fragment.
    /// </summary>
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
            ("icerpc:?foo=bar", "/", ""),
            ("icerpc://host.zeroc.com", "/", ""),
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
            ("ice://0.0.0.0/identity#facet", "/identity", "facet"), // Any IPv4 in service address endpoint (unusable but parses ok)
            ("ice://[::0]/identity#facet", "/identity", "facet"), // Any IPv6 in service address (unusable but parses ok)
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

    private static readonly Dictionary<string, Endpoint[]> _altEndpoints = new()
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
        var serviceAddress = ServiceAddress.Parse("ice://localhost/hello");

        // Act/Assert
        Assert.That(() => serviceAddress.Params = serviceAddress.Params.SetItem("adapter-id", ""), Throws.ArgumentException);
    }

    [Test]
    public void Cannot_set_alt_endpoints_on_unsupported_protocol()
    {
        // Arrange
        var serviceAddress = new ServiceAddress(new Uri("foobar://localhost/hello"));

        // Constructing alternate endpoints.
        var altEndpoints = ImmutableList.Create(Endpoint.FromString("icerpc://localhost:10000?transport=foobar"));

        // Act/Assert
        Assert.Throws<InvalidOperationException>(() => serviceAddress.AltEndpoints = altEndpoints);
    }

    /// <summary>Verifies that the service address endpoint cannot be set when the service address contains any params.
    /// </summary>
    [Test]
    public void Cannot_set_endpoint_on_a_service_address_with_parameters()
    {
        // Arrange
        var serviceAddress = new ServiceAddress(Protocol.Ice)
        {
            Params = new Dictionary<string, string> { ["adapter-id"] = "value" }.ToImmutableDictionary(),
        };

        // Act/Assert
        Assert.That(
            () => serviceAddress.Endpoint = new Endpoint(serviceAddress.Protocol!) { Host = "localhost" },
            Throws.TypeOf<InvalidOperationException>());
    }

    /// <summary>Verifies that the service address cannot contain alt endpoints when the service address endpoint is
    /// null.</summary>
    [Test]
    public void Service_address_cannot_contain_alt_endpoints_when_endpoint_is_null()
    {
        // Arrange
        // Construct a serviceAddress from a protocol since it will have an empty endpoint.
        var serviceAddress = new ServiceAddress(Protocol.IceRpc);

        // Constructing alternate endpoints.
        var altEndpoints = ImmutableList.Create(Endpoint.FromString("icerpc://localhost:10000?transport=foobar"));

        // Act/Assert
        Assert.Throws<InvalidOperationException>(() => serviceAddress.AltEndpoints = altEndpoints);
        return;
    }

    /// <summary>Verifies that the service address endpoint cannot be null when the service address contains has alt
    /// endpoints.</summary>
    [Test]
    public void Cannot_clear_endpoint_when_alt_endpoints_is_not_empty()
    {
        // Arrange
        // Creating a proxy with an endpoint.
        var serviceAddress = new ServiceAddress(new Uri("icerpc://localhost:8080/foo"));

        // Constructing alternate endpoints.
        var altEndpoints = ImmutableList.Create(Endpoint.FromString("icerpc://localhost:10000?transport=foobar"));
        serviceAddress.AltEndpoints = altEndpoints;

        // Act/Assert
        Assert.Throws<InvalidOperationException>(() => serviceAddress.Endpoint = null);
    }

    [Test]
    public void Cannot_set_path_on_unsupported_protocol()
    {
        // Arrange
        // Can only construct a service address with an unsupported protocol using the URI constructor.
        var uri = new Uri("foo://localhost:8080");
        var serviceAddress = new ServiceAddress(uri);

        // Act/Assert
        Assert.Throws<InvalidOperationException>(() => serviceAddress.Path = "/bar");
    }

    /// <summary>Verifies that the "fragment" cannot be set when the protocol is null or has no fragment.</summary>
    [TestCase("icerpc")]
    [TestCase("")]
    public void Cannot_set_fragment_if_protocol_has_no_fragment(string protocolName)
    {
        Protocol? protocol = protocolName.Length > 0 ? Protocol.FromString(protocolName) : null;
        var serviceAddress = new ServiceAddress(protocol);

        Assert.That(() => serviceAddress.Fragment = "bar", Throws.TypeOf<InvalidOperationException>());

        if (protocol is not null)
        {
            Assert.That(protocol.HasFragment, Is.False);
        }
    }

    /// <summary>Verifies that the service address params cannot be set when the service address has an endpoint.
    /// </summary>
    [Test]
    public void Cannot_set_params_on_a_service_address_with_endpoints()
    {
        var serviceAddress = ServiceAddress.Parse("icerpc://localhost/hello");

        Assert.That(
            () => serviceAddress.Params = serviceAddress.Params.Add("name", "value"),
            Throws.TypeOf<InvalidOperationException>());
    }

    /// <summary>Verifies that a service address can be converted into a string using any of the supported formats.
    /// </summary>
    /// <param name="str">The string used to create the source serviceAddress</param>
    [Test, TestCaseSource(nameof(ServiceAddressToStringSource))]
    public void Convert_a_service_address_to_a_string(string str)
    {
        var serviceAddress = ServiceAddress.Parse(str);

        string str2 = serviceAddress.ToString();

        Assert.That(ServiceAddress.Parse(str2), Is.EqualTo(serviceAddress));
    }

    /// <summary>Verifies that two equal proxies always produce the same hash code.</summary>
    /// <param name="str">The service address to test.</param>
    [Test, TestCaseSource(nameof(ServiceAddressHashCodeSource))]
    public void Equal_service_addresses_produce_the_same_hash_code(string str)
    {
        var serviceAddress1 = ServiceAddress.Parse(str);
        var serviceAddress2 = ServiceAddress.Parse(serviceAddress1.ToString());

        int hashCode1 = serviceAddress1.GetHashCode();

        Assert.Multiple(() =>
        {
            Assert.That(serviceAddress1, Is.EqualTo(serviceAddress2));
            Assert.That(hashCode1, Is.EqualTo(serviceAddress1.GetHashCode()));
            Assert.That(hashCode1, Is.EqualTo(serviceAddress2.GetHashCode()));
        });
    }

    /// <summary>Verifies that a service address created from a path has the expected protocol, path and endpoint
    /// properties.</summary>
    [TestCase("/")]
    [TestCase("/foo/bar/")]
    public void From_path(string path)
    {
        var serviceAddress = new ServiceAddress { Path = path };

        Assert.Multiple(() =>
        {
            Assert.That(serviceAddress.Protocol, Is.Null);
            Assert.That(serviceAddress.Path, Is.EqualTo(path));
            Assert.That(serviceAddress.Endpoint, Is.Null);
        });
    }

    [Test]
    public void Invalid_fragment_throws_exception()
    {
        // Arrange
        var serviceAddress = new ServiceAddress(Protocol.IceRpc);

        // Act/Assert
        Assert.Throws<ArgumentException>(() => serviceAddress.Fragment = "foo<");
    }

    [Test]
    public void Invalid_path_throws_exception()
    {
        // Arrange
        var serviceAddress = new ServiceAddress(Protocol.IceRpc);

        // Act/Assert
        Assert.Throws<ArgumentException>(() => serviceAddress.Path = "foo<");
    }

    /// <summary>Verifies that a string can be correctly parsed as a service address</summary>
    /// <param name="str">The string to parse as a service address</param>
    /// <param name="format">The format of <paramref name="str"/> string.</param>
    /// <param name="path">The expected path for the parsed service address</param>
    /// <param name="fragment">The expected fragment for the parsed service address</param>
    [Test, TestCaseSource(nameof(ServiceAddressParseSource))]
    public void Parse_a_service_address_string(string str, string path, string fragment)
    {
        var serviceAddress = ServiceAddress.Parse(str);

        Assert.Multiple(() =>
        {
            Assert.That(serviceAddress.Path, Is.EqualTo(path));
            Assert.That(serviceAddress.Fragment, Is.EqualTo(fragment));
        });
    }

    /// <summary>Verifies that parsing a string that is not valid according the given <paramref name="format"/> throws
    /// <see cref="FormatException"/>.</summary>
    /// <param name="str">The string to parse as a service address</param>
    /// <param name="format">The format use to parse the string as a service address</param>
    [Test, TestCaseSource(nameof(ServiceAddressParseInvalidSource))]
    public void Parse_an_invalid_service_address_string(string str) =>
        Assert.Throws(Is.InstanceOf<FormatException>(), () => ServiceAddress.Parse(str));

    [Test, TestCaseSource(nameof(AltEndpointsSource))]
    public void Parse_service_address_alt_endpoints(string str, Endpoint[] altEndpoints)
    {
        var serviceAddress = ServiceAddress.Parse(str);

        Assert.That(serviceAddress.AltEndpoints, Is.EqualTo(altEndpoints));
    }

    /// <summary>Verifies that the proxy invoker of the <see cref="ISliceDecodeFeature"/> is used for proxies
    /// received over an incoming connection.</summary>
    // TODO: move this test to Slice
    [Test]
    public async Task Proxy_invoker_is_set_to_the_slice_decode_options_feature_service_address_invoker()
    {
        var service = new SendProxyTest();
        var pipeline = new Pipeline();
        var router = new Router();
        router.Map<ISendProxyTest>(service);
        router.UseFeature<ISliceDecodeFeature>(new SliceDecodeFeature { ProxyInvoker = pipeline });
        await using ServiceProvider provider = new ServiceCollection()
            .AddColocTest(router)
            .BuildServiceProvider(validateScopes: true);

        var proxy = new SendProxyTestProxy(provider.GetRequiredService<ClientConnection>());
        provider.GetRequiredService<Server>().Listen();

        await proxy.SendProxyAsync(proxy);

        Assert.That(service.ReceivedProxy, Is.Not.Null);
        Assert.That(service.ReceivedProxy.Value.Invoker, Is.EqualTo(pipeline));
    }

    /// <summary>Verifies that a proxy received over an incoming connection uses the default invoker.</summary>
    // TODO: move this test to Slice
    [Test]
    public async Task Proxy_received_over_an_incoming_connection_uses_the_default_invoker()
    {
        var service = new SendProxyTest();
        await using ServiceProvider provider = new ServiceCollection()
            .AddColocTest(service)
            .BuildServiceProvider(validateScopes: true);

        var proxy = new SendProxyTestProxy(provider.GetRequiredService<ClientConnection>());
        provider.GetRequiredService<Server>().Listen();

        await proxy.SendProxyAsync(proxy);

        Assert.That(service.ReceivedProxy, Is.Not.Null);
        Assert.That(service.ReceivedProxy.Value.Invoker, Is.EqualTo(InvalidOperationInvoker.Instance));
    }

    /// <summary>Verifies that a service address received over an outgoing connection inherits the callers invoker.
    /// </summary>
    [Test]
    public async Task Proxy_received_over_an_outgoing_connection_inherits_the_callers_invoker()
    {
        await using ServiceProvider provider = new ServiceCollection()
            .AddColocTest(new ReceiveProxyTest())
            .BuildServiceProvider(validateScopes: true);

        provider.GetRequiredService<Server>().Listen();
        IConnection connection = provider.GetRequiredService<ClientConnection>();
        IInvoker invoker = new Pipeline().Into(connection);
        var proxy = new ReceiveProxyTestProxy(invoker);

        ReceiveProxyTestProxy received = await proxy.ReceiveProxyAsync();

        Assert.That(received.Invoker, Is.EqualTo(invoker));
    }

    [Test, TestCaseSource(nameof(ServiceAddressToUriSource))]
    public void Relative_service_address_to_uri(ServiceAddress serviceAddress, string expected)
    {
        // Act
        var result = serviceAddress.ToUri();

        // Assert
        Assert.That(result.ToString(), Is.EqualTo(expected));
    }

    [Test, TestCaseSource(nameof(ServiceAddressEqualitySource))]
    public void Service_address_equality(ServiceAddress serviceAddress1, ServiceAddress? serviceAddress2, bool expected)
    {
        // Act
        bool result = serviceAddress1 == serviceAddress2;

        // Assert
        Assert.That(result, Is.EqualTo(expected));
    }

    [Test, TestCaseSource(nameof(ServiceAddressToStringWithSetupSource))]
    public void Service_address_to_string(ServiceAddress serviceAddress, string expected)
    {
        // Act
        string result = serviceAddress.ToString();

        // Assert
        Assert.That(result, Is.EqualTo(expected));
    }

    /// <summary>Verifies that setting the alt endpoints containing endpoints that uses a protocol different than the
    /// proxy protocol throws <see cref="ArgumentException"/>.</summary>
    [Test]
    public void Setting_alt_endpoints_with_a_different_protocol_fails()
    {
        var serviceAddress = ServiceAddress.Parse("ice://host.zeroc.com:10000/hello");
        Endpoint endpoint1 = ServiceAddress.Parse("ice://host.zeroc.com:10001/hello").Endpoint!.Value;
        Endpoint endpoint2 = ServiceAddress.Parse("icerpc://host.zeroc.com/hello").Endpoint!.Value;
        var altEndpoints = new Endpoint[] { endpoint1, endpoint2 }.ToImmutableList();

        Assert.Multiple(() =>
        {
            Assert.Throws<ArgumentException>(() => serviceAddress.AltEndpoints = altEndpoints);

            // Ensure the alt endpoints weren't updated
            Assert.That(serviceAddress.AltEndpoints, Is.Empty);
        });
    }

    /// <summary>Verifies that setting an endpoint that uses a protocol different than the service address protocol
    /// throws <see cref="ArgumentException"/>.</summary>
    [Test]
    public void Setting_endpoint_with_a_different_protocol_fails()
    {
        var serviceAddress = ServiceAddress.Parse("ice://host.zeroc.com/hello");
        Endpoint? endpoint = serviceAddress.Endpoint;
        Endpoint newEndpoint = ServiceAddress.Parse("icerpc://host.zeroc.com/hello").Endpoint!.Value;

        Assert.Multiple(() =>
        {
            Assert.Throws<ArgumentException>(() => serviceAddress.Endpoint = newEndpoint);

            // Ensure the endpoint wasn't updated
            Assert.That(serviceAddress.Endpoint, Is.EqualTo(endpoint));
        });
    }

    /// <summary>Verifies that we can set the fragment on an ice service address</summary>
    [Test]
    public void Set_fragment_on_an_ice_service_address()
    {
        var serviceAddress = new ServiceAddress(Protocol.Ice);

        serviceAddress = serviceAddress with { Fragment = "bar" };

        Assert.Multiple(() =>
        {
            Assert.That(serviceAddress.Fragment, Is.EqualTo("bar"));
            Assert.That(serviceAddress.Protocol!.HasFragment, Is.True);
        });
    }

    [Test]
    public void Uri_constructor_with_relative_uri_produces_relative_service_address()
    {
        // Arrange
        var uri = new Uri("/foo", UriKind.Relative);

        // Act
        var serviceAddress = new ServiceAddress(uri);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(serviceAddress.Path, Is.EqualTo("/foo"));
            Assert.That(serviceAddress.Protocol, Is.Null);
        });
    }

    private class ReceiveProxyTest : Service, IReceiveProxyTest
    {
        public ValueTask<ReceiveProxyTestProxy> ReceiveProxyAsync(IFeatureCollection features, CancellationToken cancel) =>
            new(new ReceiveProxyTestProxy("/hello"));
    }

    private class SendProxyTest : Service, ISendProxyTest
    {
        public SendProxyTestProxy? ReceivedProxy { get; private set; }

        public ValueTask SendProxyAsync(
            SendProxyTestProxy proxy,
            IFeatureCollection features,
            CancellationToken cancel = default)
        {
            ReceivedProxy = proxy;
            return default;
        }
    }
}

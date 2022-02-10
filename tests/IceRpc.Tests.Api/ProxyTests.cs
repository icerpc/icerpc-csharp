// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Configure;
using IceRpc.Slice;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using System.Collections.Immutable;

namespace IceRpc.Tests.Api
{
    [Parallelizable(scope: ParallelScope.All)]
    [Timeout(5000)]
    // [Log(LogAttributeLevel.Information)]
    public class ProxyTests
    {
        [TestCase("ice")]
        [TestCase("icerpc")]
        public async Task Proxy_ServiceAsync(string protocol)
        {
            await using ServiceProvider serviceProvider = new IntegrationTestServiceCollection()
                .UseProtocol(protocol)
                .AddTransient<IDispatcher, Greeter>()
                .BuildServiceProvider();

            var prx = GreeterPrx.FromConnection(serviceProvider.GetRequiredService<Connection>());
            await prx.IcePingAsync();

            string[] ids = new string[]
            {
                "::IceRpc::Slice::Service",
                "::IceRpc::Tests::Api::Greeter",
            };
            CollectionAssert.AreEqual(ids, await prx.IceIdsAsync());

            Assert.That(await prx.IceIsAAsync("::IceRpc::Tests::Api::Greeter"), Is.True);
            Assert.That(await prx.IceIsAAsync("::IceRpc::Tests::Api::Foo"), Is.False);

            Assert.AreEqual(prx, await prx.AsAsync<GreeterPrx>());

            // Test that Service operation correctly forward the cancel param
            var canceled = new CancellationToken(canceled: true);
            Assert.CatchAsync<OperationCanceledException>(async () => await prx.IcePingAsync(cancel: canceled));
            Assert.CatchAsync<OperationCanceledException>(async () => await prx.IceIdsAsync(cancel: canceled));
            Assert.CatchAsync<OperationCanceledException>(
                async () => await prx.IceIsAAsync("::IceRpc::Tests::Api::Greeter", cancel: canceled));
            Assert.CatchAsync<OperationCanceledException>(
                async () => await prx.AsAsync<GreeterPrx>(cancel: canceled));

            // Test that Service operations correctly forward the context
            var invocation = new Invocation
            {
                Features = new FeatureCollection().WithContext(new Dictionary<string, string> { ["foo"] = "bar" })
            };

            var pipeline = new Pipeline();
            prx.Proxy.Invoker = pipeline;
            pipeline.Use(next => new InlineInvoker((request, cancel) =>
            {
                Assert.AreEqual(request.Features.GetContext(), invocation.Features.GetContext());
                return next.InvokeAsync(request, cancel);
            }));

            await prx.IcePingAsync(invocation);
            await prx.IceIdsAsync(invocation);
            await prx.IceIsAAsync("::IceRpc::Tests::Api::Greeter", invocation);
            await prx.AsAsync<GreeterPrx>(invocation);
        }

        [TestCase("icerpc://localhost:10000/test?alt-endpoint=host2")]
        [TestCase("ice://localhost:10000/test")]
        [TestCase("icerpc:/test?name=value")]
        [TestCase("ice:/test#fragment")]
        [TestCase("/x/y/z")]
        [TestCase("foobar:/path#fragment")]
        [TestCase("foobar://host/path#fragment")]
        public void Proxy_GetSetProperty(string s)
        {
            var proxy = Proxy.Parse(s);

            Proxy proxy2 = proxy.Protocol == Protocol.Relative ?
                new Proxy(proxy.Protocol) { Path = proxy.Path } : new Proxy(proxy.OriginalUri!);

            // Can always read all properties
            Assert.Multiple(
                () =>
                {
                    Assert.That(proxy, Is.EqualTo(proxy2));

                    CollectionAssert.AreEqual(proxy.AltEndpoints, proxy2.AltEndpoints);
                    Assert.That(proxy.Connection, Is.EqualTo(proxy2.Connection));
                    Assert.That(proxy.Encoding, Is.EqualTo(proxy2.Encoding));
                    Assert.That(proxy.Endpoint, Is.EqualTo(proxy2.Endpoint));
                    Assert.That(proxy.Fragment, Is.EqualTo(proxy2.Fragment));
                    Assert.That(proxy.Invoker, Is.EqualTo(proxy2.Invoker));
                    Assert.That(proxy.OriginalUri, Is.EqualTo(proxy2.OriginalUri));
                    CollectionAssert.AreEqual(proxy.Params, proxy2.Params);
                    Assert.That(proxy.Path, Is.EqualTo(proxy2.Path));
                });

            if (proxy.OriginalUri is Uri uri)
            {
                Assert.That(proxy.ToUri(), Is.EqualTo(uri));

                if (proxy.Protocol.IsSupported)
                {
                    proxy.Endpoint = proxy.Endpoint;
                    Assert.That(proxy.OriginalUri, Is.Null);
                    Assert.That(proxy.ToUri().IsAbsoluteUri, Is.True);
                }
            }
            else
            {
                Assert.That(proxy.ToUri().IsAbsoluteUri, Is.False);
            }

            if (proxy.Protocol.IsSupported)
            {
                // Basic sets/init

                proxy2 = proxy.Endpoint is Endpoint endpoint ?
                    proxy with { Endpoint = endpoint with { Port = (ushort)(endpoint.Port + 1) } } :
                    proxy with { Path = $"{proxy.Path}/extra" };

                Assert.That(proxy, Is.Not.EqualTo(proxy2));

                proxy.AltEndpoints = ImmutableList<Endpoint>.Empty;
                Assert.That(proxy.OriginalUri, Is.Null);
                proxy.Endpoint = null;
                proxy.Connection = null;
                proxy = proxy with { Fragment = "" }; // always ok
                if (proxy.Protocol.HasFragment)
                {
                    proxy = proxy with { Fragment = "bar" };
                }
                else
                {
                    Assert.Throws<InvalidOperationException>(() => proxy = proxy with { Fragment = "bar" });
                }
                proxy.Invoker = Proxy.DefaultInvoker;
                proxy.Params = ImmutableDictionary<string, string>.Empty;
                proxy = proxy with { Path = "/foo" };

                // Erroneous sets/init

                Assert.Throws<InvalidOperationException>(
                    () => proxy.AltEndpoints = ImmutableList.Create(new Endpoint(proxy.Protocol)));

                Assert.Throws<ArgumentException>(
                    () => proxy.Endpoint = Endpoint.FromString(
                        proxy.Protocol == Protocol.IceRpc ? "ice://localhost" : "icerpc://localhost"));

                proxy.Endpoint = new Endpoint(proxy.Protocol) { Host = "localhost" };

                proxy.Params = ImmutableDictionary<string, string>.Empty; // always ok
                if (proxy.Protocol != Protocol.Ice)
                {
                    Assert.Throws<InvalidOperationException>(() => proxy.Params = proxy.Params.Add("name", "value"));
                }

                proxy.Endpoint = null;

                proxy.Params = proxy.Params.Add("adapter-id", "value");

                if (proxy.Protocol == Protocol.Ice)
                {
                    Assert.Throws<ArgumentException>(() => proxy.Params = proxy.Params.SetItem("adapter-id", ""));
                }

                Assert.Throws<InvalidOperationException>(
                    () => proxy.Endpoint = new Endpoint(proxy.Protocol) { Host = "localhost" });
            }
            else
            {
                Assert.Throws<InvalidOperationException>(() => proxy.AltEndpoints = ImmutableList<Endpoint>.Empty);
                Assert.Throws<InvalidOperationException>(() => proxy.Connection = null);
                Assert.Throws<InvalidOperationException>(() => proxy.Endpoint = "icerpc://host");
                Assert.Throws<InvalidOperationException>(() => proxy = proxy with { Fragment = "bar" });
                Assert.Throws<InvalidOperationException>(() => proxy.Invoker = Proxy.DefaultInvoker);
                Assert.Throws<InvalidOperationException>(
                    () => proxy.Params = ImmutableDictionary<string, string>.Empty);

                if (proxy.Protocol != Protocol.Relative)
                {
                    Assert.Throws<InvalidOperationException>(() => proxy = proxy with { Path = "/foo" });
                }
                else
                {
                    proxy = proxy with { Path = "/foo" };
                }
            }
        }

        [Test]
        public void Proxy_SetProperty_ArgumentException()
        {
            var iceProxy = Proxy.Parse("hello:tcp -h localhost -p 10000", format: IceProxyFormat.Default);
            Assert.AreEqual(Protocol.Ice, iceProxy.Protocol);
            var icerpcProxy = Proxy.Parse("icerpc://host.zeroc.com/hello");
            Assert.AreEqual(Protocol.IceRpc, icerpcProxy.Protocol);

            // Endpoints protocol must match the proxy protocol
            Assert.Throws<ArgumentException>(() => iceProxy.Endpoint = icerpcProxy.Endpoint);
            Assert.Throws<ArgumentException>(() => icerpcProxy.Endpoint = iceProxy.Endpoint);
        }

        /// <summary>Test the parsing of valid proxies.</summary>
        /// <param name="str">The string to parse as a proxy.</param>
        [TestCase("ice -t:tcp -h localhost -p 10000")]
        [TestCase("icerpc:ssl -h localhost -p 10000")]
        [TestCase("identity:tcp -h 0.0.0.0")] // Any IPv4 in proxy endpoint (unusable but parses ok)
        [TestCase("identity:tcp -h \"::0\"")] // Any IPv6 address in proxy endpoint (unusable but parses ok)
        [TestCase("identity:coloc -h *")]
        [TestCase("identity -e 4.5:coloc -h *")]
        [TestCase("name -f facet:coloc -h localhost", "/name", "facet")]
        [TestCase("category/name -f facet:coloc -h localhost", "/category/name", "facet")]
        [TestCase("cat$gory/nam$ -f fac$t:coloc -h localhost", "/cat%24gory/nam%24", "fac%24t")]
        [TestCase("\\342\\x82\\254\\60\\x9\\60\\", "/%E2%82%AC0%090%5C")]
        [TestCase("bar/foo", "/bar/foo")]
        [TestCase("foo", "/foo")]
        public void Proxy_Parse_ValidInputIceFormat(string str, string? path = null, string? fragment = null)
        {
            var proxy = Proxy.Parse(str, format: IceProxyFormat.Default);

            if (path != null)
            {
                Assert.AreEqual(path, proxy.Path);
            }

            if (fragment != null)
            {
                Assert.That(proxy.Fragment, Is.EqualTo(fragment));
            }

            Assert.AreEqual(Protocol.Ice, proxy.Protocol);
            Assert.That(Proxy.TryParse(
                proxy.ToString(IceProxyFormat.Default),
                invoker: null,
                format: IceProxyFormat.Default,
                out Proxy? proxy2),
                Is.True);
            Assert.AreEqual(proxy, proxy2); // round-trip works

            // Also try with non-default ToStringMode
            proxy2 = Proxy.Parse(proxy.ToString(IceProxyFormat.ASCII), format: IceProxyFormat.Default);
            Assert.AreEqual(proxy, proxy2);

            proxy2 = Proxy.Parse(proxy.ToString(IceProxyFormat.Compat), format: IceProxyFormat.Default);
            Assert.AreEqual(proxy, proxy2);

            var prx = GreeterPrx.Parse(str, format: IceProxyFormat.Default);
            Assert.AreEqual(Protocol.Ice, prx.Proxy.Protocol);
            Assert.That(GreeterPrx.TryParse(
                prx.ToString(IceProxyFormat.Default),
                invoker: null,
                format: IceProxyFormat.Default,
                out GreeterPrx prx2),
                Is.True);
            Assert.AreEqual(prx, prx2); // round-trip works
        }

        [TestCase("icerpc://host.zeroc.com/path?encoding=foo")]
        [TestCase("ice://host.zeroc.com/identity#facet", "/identity", "facet")]
        [TestCase("ice://host.zeroc.com/identity#facet#?!$x", "/identity", "facet#?!$x")]
        [TestCase("ice://host.zeroc.com/identity#", "/identity", "")]
        [TestCase("ice://host.zeroc.com/identity#%24%23f", "/identity", "%24%23f")]
        [TestCase("ice://host.zeroc.com/identity?tls=false")]
        [TestCase("ice://host.zeroc.com/identity?tls=true")]
        [TestCase("ice:/path?adapter-id=foo")]
        [TestCase("icerpc://host.zeroc.com:1000/category/name")]
        [TestCase("icerpc://host.zeroc.com:1000/loc0/loc1/category/name")]
        [TestCase("icerpc://host.zeroc.com/category/name%20with%20space", "/category/name%20with%20space")]
        [TestCase("icerpc://host.zeroc.com/category/name with space", "/category/name%20with%20space")]
        [TestCase("icerpc://host.zeroc.com//identity")]
        [TestCase("icerpc://host.zeroc.com//identity?alt-endpoint=host2.zeroc.com")]
        [TestCase("icerpc://host.zeroc.com//identity?alt-endpoint=host2.zeroc.com:10000")]
        [TestCase("icerpc://[::1]:10000/identity?alt-endpoint=host1:10000,host2,host3,host4")]
        [TestCase("icerpc://[::1]:10000/identity?alt-endpoint=host1:10000&alt-endpoint=host2,host3&alt-endpoint=[::2]")]
        [TestCase("icerpc://[::1]/path?alt-endpoint=host1?adapter-id=foo=bar$name=value&alt-endpoint=host2?foo=bar$123=456")]
        [TestCase("ice:/location/identity#facet", "/location/identity")]
        [TestCase("ice:///location/identity#facet", "/location/identity")] // we tolerate an empty host
        [TestCase("icerpc://host.zeroc.com//identity")]
        [TestCase("ice://host.zeroc.com/\x7f€$%/!#$'()*+,:;=@[] %2F", "/%7F%E2%82%AC$%25/!", "$'()*+,:;=@[]%20%2F")]
        // TODO: add test with # in fragment
        [TestCase("ice://host.zeroc.com/identity#\x7f€$%/!$'()*+,:;=@[] %2F", "/identity", "%7F%E2%82%AC$%25/!$'()*+,:;=@[]%20%2F")]
        [TestCase(@"icerpc://host.zeroc.com/foo\bar\n\t!", "/foo/bar/n/t!")] // \ becomes /
        // another syntax for empty port
        [TestCase("icerpc://host.zeroc.com:/identity", "/identity")]
        [TestCase("icerpc://com.zeroc.ice/identity?transport=iaps&option=a,b%2Cb,c&option=d")]
        [TestCase("icerpc://host.zeroc.com/identity?transport=100")]
        // leading :: to make the address IPv6-like
        [TestCase("icerpc://[::ab:cd:ef:00]/identity?transport=bt")]
        [TestCase("icerpc://host.zeroc.com:10000/identity?transport=tcp")]
        [TestCase("icerpc://host.zeroc.com/identity?transport=ws&option=/foo%2520/bar")]
        [TestCase("icerpc://mylocation.domain.com/foo/bar?transport=loc", "/foo/bar")]
        [TestCase("icerpc://host:10000?transport=coloc")]
        [TestCase("icerpc:/tcp -p 10000", "/tcp%20-p%2010000")] // not recommended
        [TestCase("icerpc://host.zeroc.com/identity?transport=ws&option=/foo%2520/bar")]
        [TestCase("ice://0.0.0.0/identity#facet")] // Any IPv4 in proxy endpoint (unusable but parses ok)
        [TestCase("ice://[::0]/identity#facet")] // Any IPv6 in proxy endpoint (unusable but parses ok)
        // IDN
        [TestCase("icerpc://München-Ost:10000/path")]
        [TestCase("icerpc://xn--mnchen-ost-9db.com/path")]
        // relative proxies
        [TestCase("/foo/bar")]
        [TestCase("//foo/bar")]
        [TestCase("/foo:bar")]
        // non-supported protocols
        [TestCase("foobar://host:10000/path")]
        [TestCase("foobar://host/path#fragment", "/path", "fragment")]
        [TestCase("foobar:path", "path")]  // not a valid path since it doesn't start with /, and that's ok
        [TestCase("foobar:path#fragment", "path", "fragment")]
        public void Proxy_Parse_ValidInputUriFormat(string str, string? path = null, string? fragment = null)
        {
            var proxy = Proxy.Parse(str);
            Assert.That(Proxy.TryParse(proxy.ToString(), invoker: null, format: null, out Proxy? proxy2), Is.True);

            Assert.That(proxy, Is.EqualTo(proxy2));

            if (path != null)
            {
                Assert.That(proxy.Path, Is.EqualTo(path));
            }

            if (fragment != null)
            {
                Assert.That(proxy.Fragment, Is.EqualTo(fragment));
                Assert.That(proxy2!.Fragment, Is.EqualTo(fragment));
            }
        }

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
        public void Proxy_Parse_InvalidUriInput(string str)
        {
            Assert.Catch<FormatException>(() => Proxy.Parse(str));
            Assert.That(Proxy.TryParse(str, invoker: null, format: null, out _), Is.False);
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
        public void Proxy_Parse_InvalidIceInput(string str)
        {
            Assert.Throws<FormatException>(() => Proxy.Parse(str, format: IceProxyFormat.Default));
            Assert.That(Proxy.TryParse(str, invoker: null, format: IceProxyFormat.Default, out _), Is.False);
        }

        [Test]
        public void Proxy_Equals()
        {
            Assert.That(Proxy.Equals(null, null), Is.True);
            var prx = Proxy.Parse("icerpc://host.zeroc.com/identity");
            Assert.That(Proxy.Equals(prx, prx), Is.True);
            Assert.That(Proxy.Equals(prx, Proxy.Parse("icerpc://host.zeroc.com/identity")), Is.True);
            Assert.That(Proxy.Equals(null, prx), Is.False);
            Assert.That(Proxy.Equals(prx, null), Is.False);
        }

        /// <summary>Test that proxies that are equal produce the same hash code.</summary>
        [TestCase("hello:tcp -h localhost")]
        [TestCase("icerpc://localhost/path?alt-endpoint=[::1]")]
        public void Proxy_HashCode(string proxyString)
        {
            IProxyFormat? format = proxyString.StartsWith("ice", StringComparison.Ordinal) ?
                null : IceProxyFormat.Default;
            var proxy1 = Proxy.Parse(proxyString, format: format);
            var proxy2 = proxy1 with { }; // shallow clone
            var proxy3 = Proxy.Parse(proxy2.ToString());

            CheckGetHashCode(proxy1, proxy2);
            CheckGetHashCode(proxy1, proxy3);

            static void CheckGetHashCode(Proxy p1, Proxy p2)
            {
                Assert.AreEqual(p1, p2);
                Assert.AreEqual(p1.GetHashCode(), p2.GetHashCode());
                // The second attempt should hit the hash code cache
                Assert.AreEqual(p1.GetHashCode(), p2.GetHashCode());
            }
        }

        [Test]
        public async Task Proxy_ReceiveProxyAsync()
        {
            var service = new ProxyTest();

            await using ServiceProvider serviceProvider = new IntegrationTestServiceCollection()
                .AddTransient<IDispatcher>(_ => service)
                .BuildServiceProvider();

            var prx = ProxyTestPrx.FromConnection(serviceProvider.GetRequiredService<Connection>());

            ProxyTestPrx? received = await prx.ReceiveProxyAsync();
            Assert.That(received, Is.Null);

            // Check that the received proxy "inherits" the invoker of the caller.
            service.Prx = ProxyTestPrx.FromPath("/foo");
            received = await prx.ReceiveProxyAsync();
            Assert.That(received?.Proxy.Invoker, Is.EqualTo(Proxy.DefaultInvoker));

            var pipeline = new Pipeline();
            prx.Proxy.Invoker = pipeline;
            received = await prx.ReceiveProxyAsync();
            Assert.AreEqual(pipeline, received?.Proxy.Invoker);

            // Same with an endpoint
            service.Prx = ProxyTestPrx.Parse("icerpc://localhost/foo");
            received = await prx.ReceiveProxyAsync();
            Assert.AreEqual(service.Prx?.Proxy.Endpoint, received?.Proxy.Endpoint);
            Assert.AreEqual(pipeline, received?.Proxy.Invoker);
        }

        [Test]
        public async Task Proxy_SendProxyAsync()
        {
            var service = new ProxyTest();

            // First verify that the invoker of a proxy received over an incoming request is by default the default
            // invoker.
            await using ServiceProvider serviceProvider1 = new IntegrationTestServiceCollection()
                .AddTransient<IDispatcher>(_ => service)
                .BuildServiceProvider();

            var prx = ProxyTestPrx.FromConnection(serviceProvider1.GetRequiredService<Connection>());
            await prx.SendProxyAsync(prx);
            Assert.That(service.Prx, Is.Not.Null);
            Assert.That(service.Prx?.Proxy.Invoker, Is.EqualTo(Proxy.DefaultInvoker));

            // Now with a router and the ProxyInvoker middleware - we set the invoker on the proxy received by the
            // service.
            var pipeline = new Pipeline();

            await using ServiceProvider serviceProvider2 = new IntegrationTestServiceCollection()
                .AddTransient<IDispatcher>(_ =>
                {
                    var router = new Router();
                    router.Map<IProxyTest>(service);
                    router.UseFeature(new DecodePayloadOptions { ProxyInvoker = pipeline });
                    return router;
                })
                .BuildServiceProvider();
            prx = ProxyTestPrx.FromConnection(serviceProvider2.GetRequiredService<Connection>());

            service.Prx = null;
            await prx.SendProxyAsync(prx);
            Assert.That(service.Prx, Is.Not.Null);
            Assert.AreEqual(pipeline, service.Prx?.Proxy.Invoker);
        }

        [Test]
        public void Proxy_UriOptions()
        {
            string proxyString = "icerpc://localhost:10000/test";

            var proxy = Proxy.Parse(proxyString);

            Assert.AreEqual("/test", proxy.Path);

            string complicated = $"{proxyString}?encoding=1.1&alt-endpoint=localhost";
            proxy = Proxy.Parse(complicated);

            Assert.AreEqual(Encoding.Slice11, proxy.Encoding);
            Endpoint altEndpoint = proxy.AltEndpoints[0];
            Assert.AreEqual(1, proxy.AltEndpoints.Count);
        }

        [TestCase("1.3")]
        [TestCase("2.1")]
        public async Task Proxy_NotSupportedEncoding(string encoding)
        {
            await using ServiceProvider serviceProvider = new IntegrationTestServiceCollection()
                .AddTransient<IDispatcher, Greeter>()
                .BuildServiceProvider();

            var prx = GreeterPrx.FromConnection(serviceProvider.GetRequiredService<Connection>());
            prx.Proxy.Encoding = Encoding.FromString(encoding);
            await prx.IcePingAsync(); // works fine, we use the protocol's encoding in this case
        }

        [Test]
        public async Task Proxy_FactoryMethodsAsync()
        {
            Assert.AreEqual("/IceRpc.Slice.Service", ServicePrx.DefaultPath);

            var proxy = Proxy.FromPath("/test");
            Assert.AreEqual("/test", proxy.Path);
            Assert.That(proxy.Endpoint, Is.Null);

            Assert.AreEqual("/IceRpc.Tests.Api.Greeter", GreeterPrx.DefaultPath);

            var greeter = GreeterPrx.FromPath("/test");
            Assert.AreEqual("/test", greeter.Proxy.Path);
            Assert.That(greeter.Proxy.Endpoint, Is.Null);

            dynamic? capture = null;

            await using ServiceProvider serviceProvider = new IntegrationTestServiceCollection()
                .AddTransient<IDispatcher>(_ =>
                {
                    var router = new Router();
                    router.Use(next => new InlineDispatcher((request, cancel) =>
                    {
                        capture = new
                        {
                            ServerConnection = request.Connection,
                            Service = ServicePrx.FromConnection(request.Connection),
                            Greeter = GreeterPrx.FromConnection(request.Connection)
                        };
                        return new(new OutgoingResponse(request));
                    }));
                    return router;
                })
                .BuildServiceProvider();

            Connection connection = serviceProvider.GetRequiredService<Connection>();
            proxy = Proxy.FromConnection(connection, ServicePrx.DefaultPath);
            Assert.AreEqual(ServicePrx.DefaultPath, proxy.Path);
            Assert.AreEqual(connection, proxy.Connection);
            Assert.AreEqual(connection.RemoteEndpoint, proxy.Endpoint);

            greeter = GreeterPrx.FromConnection(connection);
            Assert.AreEqual(GreeterPrx.DefaultPath, greeter.Proxy.Path);
            Assert.AreEqual(connection, greeter.Proxy.Connection);
            Assert.AreEqual(connection.RemoteEndpoint, greeter.Proxy.Endpoint);

            await ServicePrx.FromConnection(connection).IcePingAsync();

            Assert.That(capture, Is.Not.Null);
            Assert.AreEqual(ServicePrx.DefaultPath, capture!.Service.Proxy.Path);
            Assert.AreEqual(capture.ServerConnection, capture.Service.Proxy.Connection);
            Assert.That(capture.Service.Proxy.Endpoint, Is.Null);

            Assert.AreEqual(GreeterPrx.DefaultPath, capture.Greeter.Proxy.Path);
            Assert.AreEqual(capture.ServerConnection, capture.Greeter.Proxy.Connection);
            Assert.That(capture.Greeter.Proxy.Endpoint, Is.Null);
        }

        public class Greeter : Service, IGreeter
        {
            public ValueTask SayHelloAsync(string message, Dispatch dispatch, CancellationToken cancel) => default;
        }

        private class ProxyTest : Service, IProxyTest
        {
            internal ProxyTestPrx? Prx { get; set; }

            public ValueTask<ProxyTestPrx?> ReceiveProxyAsync(Dispatch dispatch, CancellationToken cancel) => new(Prx);

            public ValueTask SendProxyAsync(ProxyTestPrx prx, Dispatch dispatch, CancellationToken cancel)
            {
                Prx = prx;
                return default;
            }
        }
    }
}

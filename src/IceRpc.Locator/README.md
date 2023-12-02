# Locator interceptor for IceRPC

IceRpc.Locator provides an [IceRPC][icerpc-csharp] interceptor that allows you to resolve ice service addresses using an
[Ice locator][locator].

Use this interceptor in IceRPC client applications that call services hosted by IceGrid-managed servers.

_Ice interop only_

[Source code][source] | [Package][package] | [Example][example] | [API reference][api] | [Ice interop][interop]

## Sample code

```csharp
using IceRpc;
using IceRpc.Ice;

// Create an invocation pipeline.
var pipeline = new Pipeline();

// You typically use the locator interceptor with a connection cache.
await using var connectionCache = new ConnectionCache();

// You can use the same invocation pipeline for all your proxies.
var locatorProxy = new LocatorProxy(
    pipeline,
    new Uri("ice://localhost/DemoIceGrid/Locator"));

// If you install the retry interceptor, install it before the locator interceptor.
pipeline = pipeline
    .UseRetry()
    .UseLocator(locatorProxy)
    .Into(connectionCache);

// A call on this proxy will use the locator to find the server address(es) associated
// with `/hello`.
// The locator interceptor caches successful resolutions.
var wellKnownProxy = new HelloProxy(pipeline, new Uri("ice:/hello"));

// The locator also resolves ice proxies with an adapter-id parameter.
var indirectProxy = new HelloProxy(
    pipeline,
    new Uri("ice:/hello?adapter-id=HelloAdapter"));
```

[api]: https://docs.icerpc.dev/api/csharp/api/IceRpc.Locator.html
[icerpc-csharp]: https://github.com/icerpc/icerpc-csharp
[interop]: https://docs.icerpc.dev/icerpc-for-ice-users
[example]: https://github.com/icerpc/icerpc-csharp/tree/0.2.x/examples/Interop/IceGrid
[locator]: https://doc.zeroc.com/ice/3.7/client-server-features/locators
[package]: https://www.nuget.org/packages/IceRpc.Locator
[source]: https://github.com/icerpc/icerpc-csharp/tree/0.2.x/src/IceRpc.Locator

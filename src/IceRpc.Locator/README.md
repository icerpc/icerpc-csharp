# Locator Interceptor for IceRPC

IceRpc.Locator provides an [IceRPC][icerpc] interceptor that allows you to resolve ice service addresses
using an [Ice locator][locator].

Use this interceptor in IceRPC client applications that call services hosted by IceGrid-managed servers.

_Ice interop only_

[Source code][source] | [Package][package] | [Example code][example] | [API reference documentation][api] | [Ice interop][interop]

## Sample code

```csharp
using IceRpc;
using IceRpc.Ice;

// You typically use the locator interceptor with a connection cache.
await using var connectionCache = new ConnectionCache();

var pipeline = new Pipeline();

// You can use the same invocation pipeline for all your proxies.
var locatorProxy = new LocatorProxy(pipeline, new Uri("ice://localhost/DemoIceGrid/Locator"));

// If you install the retry interceptor, install it before the locator interceptor.
pipeline = pipeline
    .UseRetry()
    .UseLocator(locatorProxy)
    .Into(connectionCache);
    
// A call on this proxy will use the locator to find the server address(es) associated with
// `/hello`. The locator interceptor caches successful resolutions.
var helloProxy = new HelloProxy(pipeline, new Uri("ice:/hello"));
```

[api]: https://api.testing.zeroc.com/csharp/api/IceRpc.Locator.html
[example]: https://github.com/icerpc/icerpc-csharp/tree/main/examples/Interop/IceGrid
[icerpc]: https://www.nuget.org/packages/IceRpc
[interop]: https://docs.testing.zeroc.com/docs/icerpc-for-ice-users
[locator]: https://doc.zeroc.com/ice/3.7/client-server-features/locators
[package]: https://www.nuget.org/packages/IceRpc.Locator
[source]: https://github.com/icerpc/icerpc-csharp/tree/main/src/IceRpc.Locator

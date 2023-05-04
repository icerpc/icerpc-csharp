---
uid: IceRpc.Retry.RetryInterceptor
example: [*content]
---

You would typicall use the `RetryInterceptor` with the [ConnectionCache](xref:IceRpc.ConnectionCache) invoker, and the
[LoggerInterceptor](xref:IceRpc.Logger.LoggerInterceptor)

[!code-csharp[](../../examples/IceRpc.Retry/Program-0.cs)]

For a detailed example see the [Retry Example](https://github.com/icerpc/icerpc-csharp/tree/main/examples/Retry) in our GitHub
repository.

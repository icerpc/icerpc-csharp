# Design Decisions

This document records deliberate design decisions in this repository: trust boundaries, conventions, and trade-offs
that can look like defects to a reader — or to an AI audit — seeing the code for the first time. Each entry states the
decision, the reasoning behind it, and the kind of change or finding it rules out.

Most entries originate from `ai-audit` issues closed as "not planned"; the issue numbers give the full discussion.

Read this document before:

- reporting a security, robustness, or design concern (audit finding, code review comment, new issue), or
- changing code in a way that reverses one of these decisions.

If a concern falls under an entry below, the rationale has already been considered: don't report it again, and cite the
canonical issue when relevant. Revisit a decision only with new evidence (an actual victim, a measurable impact), not
with the same argument.

For dismissals newer than this document, also consult:
`gh issue list --repo icerpc/icerpc-csharp --label ai-audit --state closed --search "reason:not-planned"`.

## Trust boundaries

### IDL files (`.slice` / `.proto` / `.ice`) are trusted build inputs

`.slice`, `.proto`, and `.ice` files are authored as part of the build, alongside `.cs` files — not untrusted runtime
data. Code-injection framing does not apply: anyone who can author IDL in a project can already author C#. Malformed
values (cs::type, cs::attribute, cs::identifier, csharp_namespace, deprecated message, attribute payloads, deeply
nested types, slicec symbol output) surface as C# compiler errors on the generated source, which is an acceptable
failure mode. In particular, `cs::identifier` emits exactly the string the user specifies: the generators do not
validate, `@`-escape, or otherwise fix a "bad" identifier such as a C# keyword.
(See #4444, #4449, #4459, #4467, #4470, #4485, #4495, #4496, #4497, #4502, #4503, #4813.)

### Toolchain downloads come from trusted upstreams

Binaries fetched during source build come from trusted upstreams (NuGet.org, the official protoc GitHub repo, the
icerpc GitHub org). We rely on HTTPS + the ecosystem trust model, consistent with the rest of the .NET/NuGet world.
Requests for integrity hashes on these specific sources are not actionable. (#4450, #4471.)

### `IceRpc` and `ZeroC` are reserved namespaces

A project that consumes generated code must not define its own types or namespaces named `IceRpc` or `ZeroC`.
Generated code relies on this reservation — it references framework namespaces with ordinary `using` directives and
unqualified names, not `global::` qualification on every reference. Shadowing a reserved namespace breaks the
compilation of the generated code; that's an error in the consuming project, not in the generators. Don't
`global::`-qualify the generated references to these namespaces as a defense against shadowing. (#4813.)

### Logger framing is not a transport boundary

`LoggerMiddleware` / `LoggerInterceptor` log what happened — including peer-influenced operation names and exceptions
translated by the protocol layer. Equivalent wire outcomes intentionally log the same way regardless of how the
dispatcher signaled them. Don't special-case `DispatchException` or `OperationCanceledException` in these logs.
(#4431, #4432, #4433.)

## Code generators

### Identifier mapping can collide; `cs::identifier` is the fix

The Slice and Protobuf code generators map IDL identifiers to C# identifiers via PascalCase / underscore stripping.
These mappings can collapse distinct source identifiers to the same C# name. Real-world collisions are rare; when they
happen the supported fix is `cs::identifier` (Slice) or a future `csharp_identifier`-style option (Protobuf). Don't
make validator changes that close the heuristic at the cost of breaking schemas that use it intentionally.
(#4446, #4467.)

### Output filename collisions fail at build time

Two `.slice` / `.proto` files sharing a basename collide in a shared `OutputDir`. The supported workaround is per-input
`OutputDir` metadata (which triggers separate generator runs). The current behavior fails at build time, not silently —
that's the contract we're keeping. (#4445, #4452, #4473.)

### By-design generator behavior

`CodeBlock` drops whitespace-only writes, `Indent()` shifts every line by 4 spaces (safe with the literal forms
generators actually emit), slicec rewrites generated `.cs` only when content changes, `[Obsolete]` is not propagated to
implementer-side interfaces. These are deliberate ergonomics, not omissions. (See #4444, #4472, #4487, #4488.)

## Protocols and encodings

### Unknown values are ignored for forward compatibility

Protocol convention (HTTP/2, QUIC, Slice unchecked enums) is that unknown values must be tolerated so newer peers can
extend the wire format without breaking older ones. Rejecting unknown CompressionFormat values, unknown Initialize
parameters, extra trailing baggage entries, etc., goes against the design. (See #4416, #4478, #4524.)

### Limits are enforced systemically, not at each call site

Some values look unbounded locally but are already capped by a higher-level limit: the `SliceDecoder`
collection-allocation budget, `MaxControlFrameBodySize`, the consistent 4-byte varuint62 size placeholder used at every
encode site. Adding a local guard on top is cosmetic or inconsistent; revisits should target the systemic limit, not
the call site. (#4464, #4490, #4499, #4524.)

### Compressed payloads are decoded as streams; the decoder enforces the bounds

The icerpc protocol defines request/response payloads as unbounded byte streams. The compressor interceptor and
middleware decompress lazily — bytes flow through as the consumer reads them, with no up-front materialization. There
is no buffer to overflow, so the "zip bomb" framing (amplification of a small input into a large in-memory allocation)
does not apply. Bounding still happens, just at the consumer's decoder: Slice / Protobuf reject malformed bytes
(`InvalidDataException`) and enforce per-decoder collection-allocation budgets. Don't add a size cap inside the
decompressor; the limit belongs to the decoder that materializes structured data from the stream. (#4507.)

### The host is assumed to be little-endian

The Slice and Ice encodings always encode multi-byte primitives in little-endian byte order. This C# implementation
performs no byte-order conversion — it assumes the host is little-endian, so host order and wire order coincide, and
the fast paths copy arrays of fixed-size primitives directly to and from the wire. A little-endian host is a
supported-platform constraint, consistent with the platforms .NET itself supports. Don't add host byte-order guards to
these codecs or route them through `BinaryPrimitives`. This decision is only about host byte order: a type whose
specified wire format is big-endian (such as `WellKnownTypes::Uuid`, RFC 9562 — #4801) still needs its byte-order
conversion. (#4804.)

## Runtime conventions

### `Complete` is synchronous and non-blocking

IceRPC completes every `PipeReader` and `PipeWriter` — including the payload writers installed by interceptors and
middleware — with `Complete`, never `CompleteAsync` (#965). We interpret `Complete` as a fast, non-blocking call, the
way modern C# treats a synchronous method that also has an async counterpart. This is our interpretation, not
Microsoft's (which considers such a method potentially blocking), and it was adopted deliberately. Two rules make it
work: IceRPC never calls `Complete()` on a writer with unflushed bytes (the payload is flushed before completion, and
the transport writers throw `InvalidOperationException` when this rule is broken), and a decorator or transport
implementation must not implement `Complete` by waiting. Switching call sites to `CompleteAsync` would fix nothing
anyway: `CompleteAsync` takes no cancellation token, so it cannot bound anything `Complete` couldn't. Don't add an
asynchronous finalization step ahead of `Complete` or switch to `CompleteAsync`, and don't treat a `Complete`
implementation that follows the rules above as sync-over-async. (#965, #4840.)

Known deviation: the compressor's payload writer writes the compression trailer during its graceful `Complete` — a
network write that can block on flow control when the peer stops reading without closing its input. That's a real bug,
tracked by #4911; don't file it again, and don't dismiss it as by-design.

### Activator caches pin generated-code assemblies

`IActivator.FromAssembly` caches the activator it builds for each assembly marked with `IceGeneratedCodeAttribute` —
and, through its recursive merge, for every marked assembly it references — in a process-wide cache holding strong
references. Unloading such assemblies (e.g. with a collectible `AssemblyLoadContext`) is unsupported: the cache pins
them for the lifetime of the process. Weak-key caching or unload-safety for this cache is not planned. (#4827.)

## Triage policy

### Contract violations are not defended against at every call site

We don't add defensive code for paths that can only fire when a documented contract is already violated
(e.g. `IAsyncDisposable.DisposeAsync` throwing, callers racing `IDuplexConnection.WriteAsync` and `ShutdownWriteAsync`,
slicec emitting structurally impossible symbol output, `[Service]` declared multiply on partials when the attribute
already disallows it). We prefer to surface real-world contract violations as their own bugs, not paper over them at
every call site. (See #4414, #4422, #4458, #4480, #4502, #4503.)

### Cost-benefit too thin to act on

Some fixes introduce more cost (per-request allocation, workaround complexity, or surface-area changes across core
abstractions) than the concern's severity justifies. Don't re-open low-severity concerns with the same shape; reopen
only with new evidence (actual victim, measurable impact). (See #4440, #4465, #4482.)

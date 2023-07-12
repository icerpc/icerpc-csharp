// Copyright (c) ZeroC, Inc.

using System.Runtime.CompilerServices;

// Make internals visible to the tests assembly, to allow writing unit tests for the internal classes
// TODO: move tests to Slice.Tests
[assembly: InternalsVisibleTo("IceRpc.Tests")]

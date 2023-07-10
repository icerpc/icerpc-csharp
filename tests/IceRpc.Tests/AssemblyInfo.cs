// Copyright (c) ZeroC, Inc.

[assembly: NUnit.Framework.Timeout(8000)]

// Limit the maximum number of NUnit workers to workaround failures when running with high number of workers
[assembly: NUnit.Framework.LevelOfParallelism(10)]

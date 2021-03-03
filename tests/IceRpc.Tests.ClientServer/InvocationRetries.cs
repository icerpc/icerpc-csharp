// Copyright (c) ZeroC, Inc. All rights reserved.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using ZeroC.Ice;

namespace IceRpc.Tests.ClientServer
{
    [Parallelizable]
    public class InvocationRetriesTests : ClientServerBaseTest
    {
        [Test]
        public void InvocationRetries_Cancelation()
        {
        }
    }
}

// Copyright (c) ZeroC, Inc. All rights reserved.

using System.IO;
using System.Threading.Tasks;
using IceRpc.Test;

namespace IceRpc.Test.SeqMapping
{
    public static class AllTests
    {
        public static async Task RunAsync(TestHelper helper, bool collocated)
        {
            Communicator communicator = helper.Communicator;
            TextWriter output = helper.Output;

            var cl = IMyClassPrx.Parse(helper.GetTestProxy("test", 0), communicator);
            output.Write("testing twoway operations... ");
            output.Flush();
            Twoways.Run(communicator, cl);
            output.WriteLine("ok");

            if (!collocated)
            {
                output.Write("testing twoway operations with AMI... ");
                output.Flush();
                TwowaysAMI.Run(communicator, cl);
                output.WriteLine("ok");
            }

            output.Write("shutting down server... ");
            output.Flush();
            await cl.ShutdownAsync();
            output.WriteLine("ok");
        }
    }
}

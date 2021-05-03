// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Text;
using System.Threading.Tasks;

// The new version of the Test helper classes. Will be renamed Test once all the tests have migrated to TestNew.
namespace IceRpc.Test
{
    public abstract class TestHelper
    {
        private TextWriter? _writer;

        public Communicator Communicator { get; private init; } = null!;

        public TextWriter Output
        {
            get => _writer ?? Console.Out;
            set => _writer = value;
        }

        static TestHelper()
        {
            // Replace the default trace listener that is responsible of displaying the retry/abort dialog
            // with our custom trace listener that always aborts upon failure.
            // see: https://docs.microsoft.com/en-us/dotnet/api/system.diagnostics.defaulttracelistener?view=net-5.0#remarks
            Trace.Listeners.Clear();
            Trace.Listeners.Add(new TestTraceListener());
        }

        public static void Assert([DoesNotReturnIf(false)] bool b, string message = "")
        {
            if (!b)
            {
                Fail(message, null);
            }
        }

        [DoesNotReturn]
        internal static void Fail(string? message, string? detailMessage)
        {
            var sb = new StringBuilder();
            sb.Append("failed:\n");
            if (message != null && message.Length > 0)
            {
                sb.Append("message: ").Append(message).Append('\n');
            }
            if (detailMessage != null && detailMessage.Length > 0)
            {
                sb.Append("details: ").Append(detailMessage).Append('\n');
            }
            try
            {
                sb.Append(new StackTrace(fNeedFileInfo: true).ToString()).Append('\n');
            }
            catch
            {
            }

            Console.WriteLine(sb.ToString());
            Environment.Exit(1);
        }

        public static Communicator CreateCommunicator()
        {
            var loggerFactory = LoggerFactory.Create(
                builder =>
                {
                    // builder.AddSimpleConsole(configure => configure.IncludeScopes = true);
                    // builder.SetMinimumLevel(LogLevel.Debug);
                });
            return new Communicator(loggerFactory: loggerFactory);
        }

        public abstract Task RunAsync(string[] args);

        public static async Task<int> RunTestAsync<T>(Communicator communicator, string[] args)
            where T : TestHelper, new()
        {
            try
            {
                var helper = new T() { Communicator = communicator };
                await helper.RunAsync(args);
                return 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
                return 1;
            }
        }

        public static T AddWithGuid<T>(Server server, IService service) where T : class, IServicePrx
        {
            var path = $"/{System.Guid.NewGuid()}";
            (server.Dispatcher as Router)!.Map(path, service);
            return server.CreateProxy<T>(path);
        }

        public virtual void ServerReady()
        {
        }

        // A custom trace listener that always aborts the application upon failure.
        internal class TestTraceListener : DefaultTraceListener
        {
            public override void Fail(string? message) => TestHelper.Fail(message, null);

            public override void Fail(string? message, string? detailMessage) => TestHelper.Fail(message, detailMessage);
        }
    }
}

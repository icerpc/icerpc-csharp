// Copyright (c) ZeroC, Inc.

using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;

public class SignTask : Task
{
    [Required]
    public string WorkingDirectory { get; set; }

    [Required]
    public ITaskItem[] Files { get; set; }

    public string AdditionalOptions { get; set; }

    public string SignTool { get; set; } = "signtool.exe";

    protected string GenerateCommandLineCommands()
    {
        CommandLineBuilder builder = new(false);
        builder.AppendSwitch("sign");
        builder.AppendSwitch("/v");
        if (AdditionalOptions is not null)
        {
            builder.AppendTextUnquoted(" ");
            builder.AppendTextUnquoted(AdditionalOptions);
        }
        builder.AppendFileNamesIfNotNull(Files, " ");
        return builder.ToString();
    }

    public override bool Execute()
    {
        try
        {
            string commandLineCommands = GenerateCommandLineCommands();
            int status = 0;
            string output = "";
            string error = "";
            int nRetries = 0;
            while (nRetries++ < 10)
            {
                output = "";
                error = "";
                status = RunCommand(WorkingDirectory, SignTool, commandLineCommands, ref output, ref error);
                if (status != 0 && error.IndexOf("timestamp server") != -1)
                {
                    Thread.Sleep(10);
                    continue;
                }
                break;
            }

            if (status == 0)
            {
                Log.LogMessage(MessageImportance.High, output.Trim());
            }
            else
            {
                Log.LogError(error.Trim());
            }

            return status == 0;
        }
        catch (Exception ex)
        {
            Log.LogMessage(MessageImportance.Normal, ex.ToString());
            Console.WriteLine(ex);
            throw;
        }
    }

    public class StreamReader
    {
        public string Output { get; private set; }

        public string Error { get; private set; }

        public void OnOutputDataReceived(object sendingProcess, DataReceivedEventArgs outLine)
        {
            if (outLine.Data is not null)
            {
                Output += outLine.Data + "\n";
            }
        }

        public void OnErrorDataReceived(object sendingProcess, DataReceivedEventArgs outLine)
        {
            if (outLine.Data is not null)
            {
                Error += outLine.Data + "\n";
            }
        }
    }

    public static int RunCommand(string workingDir, string command, string args, ref string output, ref string error)
    {
        Log.LogMessage(MessageImportance.Normal, $"command: {command} args: {args}");
        Process process = new();
        process.StartInfo.FileName = command;
        process.StartInfo.Arguments = args;
        process.StartInfo.CreateNoWindow = true;
        process.StartInfo.UseShellExecute = false;
        process.StartInfo.RedirectStandardError = true;
        process.StartInfo.RedirectStandardOutput = true;
        process.StartInfo.WorkingDirectory = workingDir;

        StreamReader streamReader = new();
        process.OutputDataReceived += new DataReceivedEventHandler(streamReader.OnOutputDataReceived);
        process.ErrorDataReceived += new DataReceivedEventHandler(streamReader.OnErrorDataReceived);

        try
        {
            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            process.WaitForExit();
            error = streamReader.Error;
            output = streamReader.Output;
            return process.ExitCode;
        }
        catch (Exception ex)
        {
            error = ex.ToString();
            return 1;
        }
    }
}

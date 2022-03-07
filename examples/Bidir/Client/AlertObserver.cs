// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Slice;
using System.Globalization;

namespace Demo;

public class AlertObserver : Service, IAlertObserver
{
    private static readonly string[] _allowedAnswers = { "Y", "N" };

    public ValueTask<bool> AlertAsync(Dispatch dispatch, CancellationToken cancel)
    {
        string answer = "";
        Console.WriteLine("Alert received...");
        while (!_allowedAnswers.Contains(answer))
        {
            Console.WriteLine("Do you want to handle the alert? [Y/N]: ");
            answer = Console.ReadLine() ?? "";
            if (_allowedAnswers.Contains(answer.ToUpper(CultureInfo.InvariantCulture)))
            {
                break;
            }
        }
        return new(answer == "Y");
    }
}

// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Slice;

namespace Demo;

public class AlertObserver: Service, IAlertObserver
{

    private static readonly string[] allowedAnswers = {"Y", "N"};
    public ValueTask<bool> AlertAsync(Dispatch dispatch, CancellationToken cancel)
    {
        string answer = "";
        Console.WriteLine("Alert received...");
        while (!allowedAnswers.Contains(answer))
        {
            Console.WriteLine("Do you want to handle the alert? [Y/N]: ");
            answer = Console.ReadLine() ?? "";
            if (allowedAnswers.Contains(answer.ToUpper()))
            {
                break;
            }
        }
        return new(answer == "Y");
    }
}

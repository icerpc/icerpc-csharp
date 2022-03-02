using IceRpc.Slice;

namespace Demo;

public class AlertRecipient: Service, IAlertRecipient
{
    public ValueTask<bool> AlertAsync(Dispatch dispatch, CancellationToken cancel)
    {
        string[] allowedAnswers = {"Y", "N"};
        string answer = "";
        Console.WriteLine("Alert received...");
        while (!allowedAnswers.Contains(answer))
        {
            Console.WriteLine("Do you want to handle the alert? [Y/N]: ");
            answer = Console.ReadLine() ?? ""
            if (allowedAnswers.Contains(answer.ToUpper()))
            {
                break;
            }
        }
        return new(answer == "Y");
    }
}

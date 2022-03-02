using IceRpc.Slice;

namespace Demo;

public class AlertRecipient: Service, IAlertRecipient
{
    public ValueTask<bool> AlertAsync(Dispatch dispatch, CancellationToken cancel)
    {
        string[] allowedAnswers = {"Y", "N"};
        string answer = "";
        Console.WriteLine("Alert recieved...");
        while (!allowedAnswers.Contains(answer))
        {
            Console.WriteLine($"Do you want to handle the alert? [Y/N]: ");
            string? input = Console.ReadLine();
            answer = (input != null ? input! : "");
            if (allowedAnswers.Contains(answer))
            {
                break;
            }
        }
        return new(answer == "Y");
    }
}

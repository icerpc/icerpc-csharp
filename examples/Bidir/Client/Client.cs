using IceRpc.Slice;

namespace Demo;

public class Client: Service, IClient
{
    public ValueTask<bool> AlertAsync(Dispatch dispatch, CancellationToken cancel)
    {
        string[] allowedAnswers = {"Y", "N"};
        string answer = "";
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

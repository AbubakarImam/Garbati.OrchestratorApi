namespace Garbati.OrchestratorApi.Exceptions;

public class AccountOpeningNotFoundException : Exception
{
    public AccountOpeningNotFoundException()
    {
    }

    public AccountOpeningNotFoundException(string message)
        : base(message)
    {
    }

    public AccountOpeningNotFoundException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

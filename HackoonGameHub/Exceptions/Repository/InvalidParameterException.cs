namespace Repository.Exceptions;

public class InvalidParameterException : Exception
{
    public InvalidParameterException() : base("Faltam Argumentos para Completar a ação!")
    {
    }

    public InvalidParameterException(string? message) : base(message)
    {
    }

    public InvalidParameterException(string? message, Exception? innerException) : base(message, innerException)
    {
    }
}
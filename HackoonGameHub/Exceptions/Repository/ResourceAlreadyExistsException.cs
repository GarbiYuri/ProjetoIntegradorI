namespace Repository.Exceptions;

public class ResourceAlreadyExistsException : Exception
{
    public ResourceAlreadyExistsException() : base("Recurso já Existe")
    {
    }

    public ResourceAlreadyExistsException(string? message) : base(message)
    {
    }

    public ResourceAlreadyExistsException(string? message, Exception? innerException) : base(message, innerException)
    {
    }
}
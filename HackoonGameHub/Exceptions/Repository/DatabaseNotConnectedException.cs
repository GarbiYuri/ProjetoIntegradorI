namespace Repository.Exceptions;

public class DatabaseNotConnectedException : Exception
{
    public DatabaseNotConnectedException() : base("O banco de Dados não foi Inicializado ou está desconectado!")
    {
    }

    public DatabaseNotConnectedException(string? message) : base(message)
    {
    }

    public DatabaseNotConnectedException(string? message, Exception? innerException) : base(message, innerException)
    {
    }
    
}
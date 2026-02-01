using Microsoft.AspNetCore.Http;

namespace PGSH.Infrastructure.Exceptions;

public abstract class DomainException : Exception
{
    protected DomainException(string message) : base(message) { }

    public virtual int StatusCode => StatusCodes.Status400BadRequest;
    public virtual string Title => "Domain Error";
}
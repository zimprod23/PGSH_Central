using PGSH.SharedKernel;

namespace PGSH.Domain.Users;

public sealed record Email
{
    private Email(string value) => Value = value;
    public string Value { get;}
    public static Result<Email> CreateEmail(string inputEmail)
    {
        if (string.IsNullOrEmpty(inputEmail))
        {
            return Result.Failure<Email>(EmailErrors.Empty);
        }else if(inputEmail.Split("@").Length < 2)
        {
            return Result.Failure<Email>(EmailErrors.InvalidFormat);
        }
        return Result.Success<Email>(new(inputEmail));
    }
}

public static class EmailErrors
{
    public static readonly Error Empty = new("Email.Empty", "Email is empty", ErrorType.NotFound);
    public static readonly Error InvalidFormat = new(
        "Email.InvalidFormat", "Email format is invalid",ErrorType.Failure); 
}
using Microsoft.AspNetCore.Http;

namespace PGSH.Infrastructure.Exceptions;

public abstract class DomainException : Exception
{
    protected DomainException(string message) : base(message) { }

    public virtual int StatusCode => StatusCodes.Status400BadRequest;
    public virtual string Title => "Domain Error";

    /// <summary>
    /// The sentence shown to the caller, or <c>null</c> to send only <see cref="Title"/>.
    /// </summary>
    /// <remarks>
    /// <para>⚠ <b>Opt-in, and deliberately not <see cref="Exception.Message"/>.</b> A subclass
    /// message is written for a log; a <c>detail</c> is written for whoever is looking at the screen,
    /// and the two are not always the same sentence. Returning <c>Message</c> for every domain
    /// exception would publish text nobody wrote for that purpose, so a subclass that has something
    /// worth showing says so by overriding this.</para>
    ///
    /// <para>⚠ <b>Above 500 only a 503's detail survives</b> — the client discards <c>detail</c> on a
    /// 500 and shows its own fixed sentence. So an exception whose explanation has to reach a human
    /// needs a status that carries it; see <c>CLAUDE.md</c> on <c>ErrorType.Problem</c>.</para>
    /// </remarks>
    public virtual string? Detail => null;
}
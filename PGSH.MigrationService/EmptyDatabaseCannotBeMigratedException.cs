namespace PGSH.MigrationService;

/// <summary>
/// The migration chain was pointed at a base that has never been migrated — a state it cannot build,
/// by design, and one whose remedy is a restore rather than a retry.
/// </summary>
/// <remarks>
/// <para>⚠ <b>It exists to be recognisable</b>, not to carry behaviour. The failure it replaces was a
/// <c>PostgresException</c> whose stack ended in <c>MigrateAsync</c>: true, and indistinguishable
/// from a migration that is genuinely broken. A named type lets a reader — or a log grep, or a future
/// health check — tell « nobody has restored this database » from « a migration is wrong », which are
/// the same red text and opposite acts.</para>
///
/// <para>It does not inherit <c>DomainException</c>: nothing here is a domain rule, and this service
/// serves no HTTP request that a status code could be mapped onto.</para>
/// </remarks>
public sealed class EmptyDatabaseCannotBeMigratedException(string message) : Exception(message);

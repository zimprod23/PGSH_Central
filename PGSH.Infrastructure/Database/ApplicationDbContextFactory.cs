using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace PGSH.Infrastructure.Database;

/// <summary>
/// Design-time factory used exclusively by EF Core tools (migrations add/remove/update).
/// Not used at runtime — the real context is registered via DI in the API.
/// </summary>
internal sealed class ApplicationDbContextFactory : IDesignTimeDbContextFactory<ApplicationDbContext>
{
    public ApplicationDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql("Host=localhost;Port=5432;Database=pgsh;Username=postgres;Password=postgres")
            .Options;

        return new ApplicationDbContext(options);
    }
}

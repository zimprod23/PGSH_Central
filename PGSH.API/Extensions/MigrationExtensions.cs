using Microsoft.EntityFrameworkCore;
using PGSH.Infrastructure.Database;

namespace PGSH.API.Extensions
{
    public static class MigrationExtensions
    {
        public static void ApplyMigrations(this IApplicationBuilder app) 
        {
            try
            {
                using IServiceScope scope = app.ApplicationServices.CreateScope();
                using ApplicationDbContext dbContext =
                      scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                //dbContext.Database.EnsureCreated();
                dbContext.Database.Migrate();
                Console.WriteLine("Applying migrations succeed");
            }
            catch (Exception ex) {
                Console.WriteLine("Applying migrations failed" + ex.ToString());
            }
           
        }
    }
}

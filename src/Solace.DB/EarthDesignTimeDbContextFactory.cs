using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Solace.DB;

public sealed class EarthDesignTimeDbContextFactory : IDesignTimeDbContextFactory<EarthDbContext>
{
    public EarthDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<EarthDbContext>();

        string? provider = Environment.GetEnvironmentVariable("EF_PROVIDER");
        bool usePostgres = string.Equals(provider, "Postgres", StringComparison.OrdinalIgnoreCase);

        Console.WriteLine("Using provider: " + provider);

        if (usePostgres)
        {
            optionsBuilder.UseNpgsql("Host=localhost;Database=dummy;",
                x => x.MigrationsAssembly("Solace.DB.Postgres"));
        }
        else
        {
            optionsBuilder.UseSqlite("Data Source=dummy.db",
                x => x.MigrationsAssembly("Solace.DB.Sqlite"));
        }

        return new EarthDbContext(optionsBuilder.Options);
    }
}
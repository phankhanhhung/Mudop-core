using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace BMMDL.Registry.Data;

/// <summary>
/// Design-time factory for EF Core migrations.
/// </summary>
public class RegistryDbContextFactory : IDesignTimeDbContextFactory<RegistryDbContext>
{
    public RegistryDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<RegistryDbContext>();
        
        // Default connection string for migrations (override in runtime)
        var connectionString = Environment.GetEnvironmentVariable("REGISTRY_DB_CONNECTION")
            ?? "Host=localhost;Database=bmmdl_registry;Username=postgres;Password=postgres";
        
        optionsBuilder.UseNpgsql(connectionString);
        
        return new RegistryDbContext(optionsBuilder.Options);
    }
}

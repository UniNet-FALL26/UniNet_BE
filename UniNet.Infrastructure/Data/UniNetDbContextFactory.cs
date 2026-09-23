using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace UniNet.Infrastructure.Data;

public sealed class UniNetDbContextFactory : IDesignTimeDbContextFactory<UniNetDbContext>
{
    public UniNetDbContext CreateDbContext(string[] args)
    {
        var connection = Environment.GetEnvironmentVariable("ConnectionStrings__UniNet")
            ?? "Host=localhost;Database=uninet;Username=postgres";
        return new(new DbContextOptionsBuilder<UniNetDbContext>().UseNpgsql(connection).Options);
    }
}

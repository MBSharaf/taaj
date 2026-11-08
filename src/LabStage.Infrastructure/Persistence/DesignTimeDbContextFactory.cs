using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace LabStage.Infrastructure.Persistence;

/// <summary>
/// Used only by `dotnet ef` (migrations add / script / bundle). The running app gets its connection string
/// from configuration; this targets the local development database unless LABSTAGE_DESIGN_CONNECTION is set.
/// </summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<LabStageDbContext>
{
    public LabStageDbContext CreateDbContext(string[] args)
    {
        var conn = Environment.GetEnvironmentVariable("LABSTAGE_DESIGN_CONNECTION")
                   ?? @"Server=(localdb)\mssqllocaldb;Database=LabStageEgyptDb;Trusted_Connection=True;MultipleActiveResultSets=true;TrustServerCertificate=True";
        return new LabStageDbContext(new DbContextOptionsBuilder<LabStageDbContext>().UseSqlServer(conn).Options);
    }
}

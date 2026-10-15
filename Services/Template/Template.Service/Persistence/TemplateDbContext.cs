using Microservices.Common.Persistence;
using Microservices.Template.Service.Domain;
using Microsoft.EntityFrameworkCore;

namespace Microservices.Template.Service.Persistence;

public class TemplateDbContext(DbContextOptions<TemplateDbContext> options) : BaseDbContext(options)
{
    public DbSet<TemplateItem> TemplateItems => Set<TemplateItem>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<TemplateItem>(b =>
        {
            b.HasIndex(x => new { x.TenantId, x.CreatedAt });
        });
    }
}

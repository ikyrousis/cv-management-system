using CVManagement.Entities;
using Microsoft.EntityFrameworkCore;

namespace CVManagement.Data;

internal class ApplicationDbContext : DbContext
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options)
    {
    }

    public DbSet<Candidate> Candidates => Set<Candidate>();

    public DbSet<Degree> Degrees => Set<Degree>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Candidate>(entity =>
        {
            entity.Property(c => c.FirstName).IsRequired();
            entity.Property(c => c.LastName).IsRequired();
            entity.Property(c => c.Email).IsRequired();

            // Derived from the name fields, so it is never stored.
            entity.Ignore(c => c.FullName);

            entity.HasOne(c => c.Degree)
                  .WithMany(d => d.Candidates)
                  .HasForeignKey(c => c.DegreeId)
                  .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<Degree>(entity =>
        {
            entity.Property(d => d.Name).IsRequired().HasMaxLength(100);
            entity.HasIndex(d => d.Name).IsUnique();
        });
    }
}

using CVManagement.Abstractions;
using CVManagement.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace CVManagement.Data;

internal class ApplicationDbContext : DbContext
{
    private readonly IEncryptor _encryptor;

    /// The instance the converters captured, which is what the model cache key has to follow.
    internal IEncryptor Encryptor => _encryptor;

    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options, IEncryptor encryptor)
        : base(options)
    {
        _encryptor = encryptor;
    }

    public DbSet<Candidate> Candidates => Set<Candidate>();

    public DbSet<Degree> Degrees => Set<Degree>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Candidate>(entity =>
        {
            entity.Property(c => c.FirstName).IsRequired().HasConversion(RequiredString("Candidate.FirstName"));
            entity.Property(c => c.LastName).IsRequired().HasConversion(RequiredString("Candidate.LastName"));
            entity.Property(c => c.Email).IsRequired().HasConversion(RequiredString("Candidate.Email"));
            entity.Property(c => c.Mobile).HasConversion(OptionalString("Candidate.Mobile"));
            entity.Property(c => c.CVFileName).HasConversion(OptionalString("Candidate.CVFileName"));
            entity.Property(c => c.CVFile).HasConversion(OptionalBytes("Candidate.CVFile"));

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

    // EF never passes nulls to converters; the null tests below exist to satisfy type checking.
    private ValueConverter<string, string> RequiredString(string column)
        => new(
            v => _encryptor.EncryptString(v, column),
            v => _encryptor.DecryptString(v, column));

    private ValueConverter<string?, string?> OptionalString(string column)
        => new(
            v => v == null ? v : _encryptor.EncryptString(v, column),
            v => v == null ? v : _encryptor.DecryptString(v, column));

    private ValueConverter<byte[]?, byte[]?> OptionalBytes(string column)
        => new(
            v => v == null ? v : _encryptor.EncryptBytes(v, column),
            v => v == null ? v : _encryptor.DecryptBytes(v, column));
}

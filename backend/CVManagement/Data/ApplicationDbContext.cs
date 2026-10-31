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

        var encryptedString = new ValueConverter<string, string>(
            v => _encryptor.EncryptString(v),
            v => _encryptor.DecryptString(v));

        // EF never passes nulls to converters; the nullable variants exist to satisfy type checking.
        var encryptedNullableString = new ValueConverter<string?, string?>(
            v => v == null ? v : _encryptor.EncryptString(v),
            v => v == null ? v : _encryptor.DecryptString(v));

        var encryptedBytes = new ValueConverter<byte[], byte[]>(
            v => _encryptor.EncryptBytes(v),
            v => _encryptor.DecryptBytes(v));

        var encryptedNullableBytes = new ValueConverter<byte[]?, byte[]?>(
            v => v == null ? v : _encryptor.EncryptBytes(v),
            v => v == null ? v : _encryptor.DecryptBytes(v));

        modelBuilder.Entity<Candidate>(entity =>
        {
            entity.Property(c => c.FirstName).IsRequired().HasConversion(encryptedString);
            entity.Property(c => c.LastName).IsRequired().HasConversion(encryptedString);
            entity.Property(c => c.Email).IsRequired().HasConversion(encryptedString);
            entity.Property(c => c.Mobile).HasConversion(encryptedNullableString);
            entity.Property(c => c.CVFileName).HasConversion(encryptedNullableString);
            entity.Property(c => c.CVFile).HasConversion(encryptedNullableBytes);

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

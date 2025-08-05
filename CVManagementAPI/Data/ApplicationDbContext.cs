using Microsoft.EntityFrameworkCore;
using CVManagementAPI.Models;

namespace CVManagementAPI.Data
{
    public class ApplicationDbContext : DbContext
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options)
        {
        }

        public DbSet<Candidate> Candidates { get; set; }
        public DbSet<Degree> Degrees { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            //Configure relationships
            modelBuilder.Entity<Candidate>()
                .HasOne(c => c.Degree)
                .WithMany(d => d.Candidates)
                .HasForeignKey(c => c.DegreeId)
                .OnDelete(DeleteBehavior.SetNull);

            //Seed initial data
            modelBuilder.Entity<Degree>().HasData(
                new Degree { Id = 1, Name = "BSc", CreationTime = DateTime.Now },
                new Degree { Id = 2, Name = "MSc", CreationTime = DateTime.Now },
                new Degree { Id = 3, Name = "PhD", CreationTime = DateTime.Now }
            );
        }
    }
}

using LolTracker.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace LolTracker.Api.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<Summoner> Summoners => Set<Summoner>();
    public DbSet<RankSnapshot> Snapshots => Set<RankSnapshot>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Summoner>()
            .HasIndex(s => new { s.Region, s.GameName, s.TagLine })
            .IsUnique();

        b.Entity<RankSnapshot>()
            .HasIndex(s => new { s.SummonerId, s.QueueType, s.TakenAt });

        b.Entity<RankSnapshot>()
            .HasOne(s => s.Summoner)
            .WithMany(s => s.Snapshots)
            .HasForeignKey(s => s.SummonerId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

using Microsoft.EntityFrameworkCore;
using Fcg.Games.Api.Models;

namespace Fcg.Games.Api.Data;

public class GamesDbContext : DbContext
{
    public GamesDbContext(DbContextOptions<GamesDbContext> options) : base(options) { }

    public DbSet<Game> Games { get; set; }
    public DbSet<Genre> Genres { get; set; }
    public DbSet<Promotion> Promotions { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Configure Promotion dates precision and indexes
        modelBuilder.Entity<Promotion>().HasIndex(p => p.GameId);
    }
}

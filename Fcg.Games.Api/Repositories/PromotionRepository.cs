using Fcg.Games.Api.Data;
using Fcg.Games.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Fcg.Games.Api.Repositories;

public class PromotionRepository
{
    private readonly GamesDbContext _context;
    public PromotionRepository(GamesDbContext context) => _context = context;

    public async Task<Promotion> CreateAsync(Promotion promotion)
    {
        _context.Promotions.Add(promotion);
        await _context.SaveChangesAsync();
        return promotion;
    }

    public async Task<Promotion?> GetByIdAsync(Guid id) => await _context.Promotions.FindAsync(id);
    public async Task<IEnumerable<Promotion>> GetAllAsync() => await _context.Promotions.ToListAsync();

    public async Task<IEnumerable<Promotion>> GetActivePromotionsForGamesAsync(IEnumerable<Guid> gameIds)
    {
        // Capture UtcNow in a local variable so EF Core can translate the comparison to SQL
        var now = DateTime.UtcNow;
        return await _context.Promotions
            .Where(p => gameIds.Contains(p.GameId) && p.StartDate <= now && p.EndDate >= now)
            .ToListAsync();
    }

    public async Task<bool> DeleteAsync(Guid id)
    {
        var p = await _context.Promotions.FindAsync(id);
        if (p == null) return false;
        _context.Promotions.Remove(p);
        await _context.SaveChangesAsync();
        return true;
    }
}

using Fcg.Games.Api.Data;
using Fcg.Games.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Fcg.Games.Api.Repositories;

public class GameRepository
{
    private readonly GamesDbContext _context;
    public GameRepository(GamesDbContext context) => _context = context;

    public async Task<Game> CreateAsync(Game game)
    {
        _context.Games.Add(game);
        await _context.SaveChangesAsync();
        return game;
    }

    public async Task<Game?> GetByIdAsync(Guid id) => await _context.Games.FindAsync(id);

    public async Task<IEnumerable<Game>> GetAllAsync() => await _context.Games.ToListAsync();

    public async Task<IEnumerable<Game>> GetGamesByIdsAsync(IEnumerable<Guid> guids) => await _context.Games.Where(g => guids.Contains(g.Id)).ToListAsync();

    public async Task<bool> DeleteAsync(Guid id)
    {
        var game = await _context.Games.FindAsync(id);
        if (game == null) return false;
        _context.Games.Remove(game);
        await _context.SaveChangesAsync();
        return true;
    }

    public async Task<(Game? game, Promotion? promotion)> GetByIdWithPromotionAsync(Guid id)
    {
        var game = await _context.Games.FindAsync(id);
        if (game == null) return (null, null);

        var promo = await _context.Promotions.Where(p => p.GameId == game.Id && p.StartDate <= DateTime.UtcNow && p.EndDate >= DateTime.UtcNow).OrderByDescending(p => p.DiscountPercentage).FirstOrDefaultAsync();
        return (game, promo);
    }
}

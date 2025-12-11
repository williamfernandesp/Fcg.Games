using Fcg.Games.Api.Data;
using Fcg.Games.Api.Models;
using Microsoft.EntityFrameworkCore;
using Fcg.Games.Api.Services;

namespace Fcg.Games.Api.Repositories;

public class GameRepository
{
    private readonly GamesDbContext _context;
    private readonly ElasticClientService _elastic;
    public GameRepository(GamesDbContext context, ElasticClientService elastic)
    {
        _context = context;
        _elastic = elastic;
    }

    public async Task<Game> CreateAsync(Game game)
    {
        _context.Games.Add(game);
        await _context.SaveChangesAsync();

        // Index in elastic
        try
        {
            await _elastic.IndexGameAsync(game);
        }
        catch (Exception ex)
        {
            // log but don't fail creation
            // if ILogger were available we would use it; swallow for now
        }

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

        // TODO: also delete from elastic index
        return true;
    }

    public async Task<(Game? game, Promotion? promotion)> GetByIdWithPromotionAsync(Guid id)
    {
        var game = await _context.Games.FindAsync(id);
        if (game == null) return (null, null);

        var now = DateTime.UtcNow;
        var promo = await _context.Promotions
            .Where(p => p.GameId == game.Id && p.StartDate <= now && p.EndDate >= now)
            .OrderByDescending(p => p.DiscountPercentage)
            .FirstOrDefaultAsync();
        return (game, promo);
    }

    // New: search using Elastic
    public async Task<IEnumerable<object>> SearchAsync(string q) => (await _elastic.SearchGamesAsync(q)).Cast<object>();
}

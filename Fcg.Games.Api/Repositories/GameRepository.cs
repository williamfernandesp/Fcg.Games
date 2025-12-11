using Fcg.Games.Api.Data;
using Fcg.Games.Api.Models;
using Microsoft.EntityFrameworkCore;
using Fcg.Games.Api.Services;

namespace Fcg.Games.Api.Repositories;

public class GameRepository
{
    private readonly GamesDbContext _context;
    private readonly ElasticClientService _elastic;
    private readonly PromotionRepository _promotionRepo;
    public GameRepository(GamesDbContext context, ElasticClientService elastic, PromotionRepository promotionRepo)
    {
        _context = context;
        _elastic = elastic;
        _promotionRepo = promotionRepo;
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

        // delete from elastic index
        try
        {
            await _elastic.DeleteGameAsync(id);
        }
        catch (Exception)
        {
            // swallow; deletion from DB succeeded
        }

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

    // New: search using Elastic, enriched with active promotions from DB
    public async Task<IEnumerable<object>> SearchAsync(string q)
    {
        var hits = (await _elastic.SearchGamesAsync(q)).ToList();
        if (!hits.Any()) return Enumerable.Empty<object>();

        // Extract ids from hits
        var ids = new List<Guid>();
        foreach (var src in hits)
        {
            if (src.TryGetProperty("id", out var idProp))
            {
                string? s = idProp.ValueKind == System.Text.Json.JsonValueKind.String ? idProp.GetString() : idProp.ToString();
                if (Guid.TryParse(s, out var gid)) ids.Add(gid);
            }
        }

        // Get active promotions for these games
        var promos = ids.Any() ? (await _promotionRepo.GetActivePromotionsForGamesAsync(ids)).ToList() : new List<Promotion>();

        var results = new List<object>();
        foreach (var src in hits)
        {
            Guid? gid = null;
            if (src.TryGetProperty("id", out var idProp))
            {
                string? s = idProp.ValueKind == System.Text.Json.JsonValueKind.String ? idProp.GetString() : idProp.ToString();
                if (Guid.TryParse(s, out var g)) gid = g;
            }

            Promotion? p = null;
            if (gid.HasValue)
                p = promos.FirstOrDefault(x => x.GameId == gid.Value);

            // extract fields safely
            string? idStr = null;
            if (src.TryGetProperty("id", out var ip))
            {
                idStr = ip.ValueKind == System.Text.Json.JsonValueKind.String ? ip.GetString() : ip.ToString();
            }

            string? title = null;
            if (src.TryGetProperty("title", out var tp))
            {
                title = tp.ValueKind == System.Text.Json.JsonValueKind.String ? tp.GetString() : tp.ToString();
            }

            string? description = null;
            if (src.TryGetProperty("description", out var dp))
            {
                description = dp.ValueKind == System.Text.Json.JsonValueKind.String ? dp.GetString() : dp.ToString();
            }

            decimal price = 0m;
            if (src.TryGetProperty("price", out var pp))
            {
                if (pp.ValueKind == System.Text.Json.JsonValueKind.Number && pp.TryGetDecimal(out var pd)) price = pd;
                else if (pp.ValueKind == System.Text.Json.JsonValueKind.String && decimal.TryParse(pp.GetString(), out var pd2)) price = pd2;
            }

            int genre = 0;
            if (src.TryGetProperty("genre", out var gp))
            {
                if (gp.ValueKind == System.Text.Json.JsonValueKind.Number && gp.TryGetInt32(out var gi)) genre = gi;
                else if (gp.ValueKind == System.Text.Json.JsonValueKind.String && int.TryParse(gp.GetString(), out var gi2)) genre = gi2;
            }

            object? promotionObj = null;
            if (p is not null)
            {
                var discounted = Math.Round(price * (1 - p.DiscountPercentage / 100m), 2);
                promotionObj = new { p.Id, p.DiscountPercentage, p.StartDate, p.EndDate, IsActive = p.IsActive, DiscountedPrice = discounted };
            }

            results.Add(new {
                id = idStr,
                title,
                description,
                price,
                genre,
                promotion = promotionObj
            });
        }

        return results;
    }
}

using Fcg.Games.Api.Data;
using Fcg.Games.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Fcg.Games.Api.Repositories;

public class GenreRepository
{
    private readonly GamesDbContext _context;
    public GenreRepository(GamesDbContext context) => _context = context;

    public async Task<Genre> CreateAsync(Genre genre)
    {
        _context.Genres.Add(genre);
        await _context.SaveChangesAsync();
        return genre;
    }

    public async Task<Genre?> GetByIdAsync(int id) => await _context.Genres.FindAsync(id);
    public async Task<IEnumerable<Genre>> GetAllAsync() => await _context.Genres.ToListAsync();

    public async Task<bool> DeleteAsync(int id)
    {
        var g = await _context.Genres.FindAsync(id);
        if (g == null) return false;
        _context.Genres.Remove(g);
        await _context.SaveChangesAsync();
        return true;
    }
}

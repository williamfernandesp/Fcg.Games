using System.ComponentModel.DataAnnotations;

namespace Fcg.Games.Api.Models;

public class Game
{
    [Key]
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public int Genre { get; set; }
}

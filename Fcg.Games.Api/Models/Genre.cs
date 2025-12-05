using System.ComponentModel.DataAnnotations;

namespace Fcg.Games.Api.Models;

public class Genre
{
    [Key]
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
}

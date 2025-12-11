using System.ComponentModel.DataAnnotations;

namespace Fcg.Games.Api.Models;

public class Promotion
{
    [Key]
    public Guid Id { get; set; }
    public Guid GameId { get; set; }
    public decimal DiscountPercentage { get; set; }
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public bool IsActive => DateTime.UtcNow >= StartDate && DateTime.UtcNow <= EndDate;
}

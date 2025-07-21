using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;

namespace Shuffull.Site.Models.Database;

[Index(nameof(MainGenreId)), Index(nameof(SubGenreId))]
public class GenreRelation
{
    [Key]
    public string GenreRelationId { get; set; }
    [Required]
    public string MainGenreId { get; set; }
    [Required]
    public string SubGenreId { get; set; }

    public Genre MainGenre { get; set; }
    public Genre SubGenre { get; set; }
}

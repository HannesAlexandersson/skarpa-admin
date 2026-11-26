using System;
using Supabase.Postgrest.Attributes;
using Supabase.Postgrest.Models;

namespace SkarpaAdminApi.Models;

[Table("booking_photos")]
public class BookingPhoto : BaseModel
{
    [PrimaryKey("id", false)]
    public Guid Id { get; set; }

    [Column("booking_id")]
    public Guid BookingId { get; set; }

    [Column("storage_path")]
    public string StoragePath { get; set; } = string.Empty;

    [Column("note")]
    public string? Note { get; set; }

    [Column("created_at")]
    public DateTimeOffset CreatedAt { get; set; }
}
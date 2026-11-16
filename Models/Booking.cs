using System;
using Supabase.Postgrest.Attributes;
using Supabase.Postgrest.Models;

namespace SkarpaAdminApi.Models;

[Table("bookings")]
public class Booking : BaseModel
{
    [PrimaryKey("id", false)]
    public Guid Id { get; set; }

    [Column("customer_id")]
    public Guid CustomerId { get; set; }

    // Joins the customer row automatically via the customer_id foreign key
    [Reference(typeof(Customer))]
    public Customer? Customer { get; set; }

    [Column("pickup_date")]
    public DateOnly PickupDate { get; set; }

    [Column("pickup_time")]
    public TimeOnly PickupTime { get; set; }

    [Column("submission_id")]
    public Guid SubmissionId { get; set; }

    [Column("pickup_address")]
    public string? PickupAddress { get; set; }

    [Column("created_at")]
    public DateTimeOffset CreatedAt { get; set; }

    [Column("updated_at")]
    public DateTimeOffset UpdatedAt { get; set; }
}
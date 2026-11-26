using System;
using Supabase.Postgrest.Attributes;
using Supabase.Postgrest.Models;

namespace SkarpaAdminApi.Models;

[Table("payments")]
public class Payment : BaseModel
{
    [PrimaryKey("id", false)] public Guid Id { get; set; }
    [Column("booking_id")] public Guid BookingId { get; set; }
    [Column("method_code")] public string MethodCode { get; set; } = "";
    [Column("status")] public string Status { get; set; } = "pending";
    [Column("amount_ore")] public int? AmountOre { get; set; }
    [Column("currency")] public string Currency { get; set; } = "SEK";
    [Column("invoiced_at")] public DateTimeOffset? InvoicedAt { get; set; }
    [Column("paid_at")] public DateTimeOffset? PaidAt { get; set; }
    [Column("provider_reference")] public string? ProviderReference { get; set; }
    [Column("created_at", ignoreOnInsert: true, ignoreOnUpdate: true)] public DateTimeOffset CreatedAt { get; set; }
}

[Table("payment_methods")]
public class PaymentMethod : BaseModel
{
    [PrimaryKey("code", true)] public string Code { get; set; } = "";
    [Column("label")] public string Label { get; set; } = "";
    [Column("timing")] public string Timing { get; set; } = "";
    [Column("active")] public bool Active { get; set; }
    [Column("sort_order")] public short SortOrder { get; set; }
}

[Table("pricing_tiers")]
public class PricingTier : BaseModel
{
    [PrimaryKey("min_knives", true)] public short MinKnives { get; set; }
    [Column("price_per_knife_ore")] public int PricePerKnifeOre { get; set; }
}

[Table("pricing_campaigns")]
public class PricingCampaign : BaseModel
{
    [PrimaryKey("id", false)] public Guid Id { get; set; }
    [Column("name")] public string Name { get; set; } = "";
    [Column("first_knife_price_ore")] public int FirstKnifePriceOre { get; set; }
    [Column("starts_on")] public DateOnly? StartsOn { get; set; }
    [Column("ends_on")] public DateOnly? EndsOn { get; set; }
    [Column("active")] public bool Active { get; set; }
}

[Table("pickup_hours")]
public class PickupHour : BaseModel
{
    [PrimaryKey("weekday", true)] public short Weekday { get; set; }
    [Column("opens_at")] public TimeOnly OpensAt { get; set; }
    [Column("closes_at")] public TimeOnly ClosesAt { get; set; }
    [Column("slot_minutes")] public int SlotMinutes { get; set; }
}

[Table("blocked_dates")]
public class BlockedDate : BaseModel
{
    [PrimaryKey("blocked_date", true)] public DateOnly Date { get; set; }
    [Column("reason")] public string? Reason { get; set; }
}

using SkarpaAdminApi.Models;
using Supabase;
using static Supabase.Postgrest.Constants;

namespace SkarpaAdminApi;

// Admin endpoints for payments, pricing and availability. All require the AdminOnly policy.
public static class AdminEndpoints
{
    static readonly string[] Statuses = ["pending", "invoiced", "paid", "refunded", "cancelled"];

    static PaymentDto ToDto(Payment p) => new(p.Id, p.BookingId, p.MethodCode, p.Status, p.AmountOre, p.Currency, p.InvoicedAt, p.PaidAt, p.CreatedAt);
    static CampaignDto ToDto(PricingCampaign c) => new(c.Id, c.Name, c.FirstKnifePriceOre, c.StartsOn, c.EndsOn, c.Active);
    static PickupHourDto ToDto(PickupHour h) => new(h.Weekday, h.OpensAt.ToString("HH:mm"), h.ClosesAt.ToString("HH:mm"), h.SlotMinutes);

    static string? ValidateCampaign(CampaignRequest r)
    {
        if (string.IsNullOrWhiteSpace(r.Name) || r.Name.Trim().Length > 120) return "Name must be 1-120 characters.";
        if (r.FirstKnifePriceOre < 0) return "Price must be zero or more.";
        if (r.StartsOn is { } s && r.EndsOn is { } e && e < s) return "End date must be on or after start date.";
        return null;
    }

    static async Task DeactivateOtherCampaigns(Client sb, Guid? keep)
    {
        var q = sb.From<PricingCampaign>().Filter("active", Operator.Equals, "true");
        if (keep is { } id) q = q.Filter("id", Operator.NotEqual, id.ToString());
        await q.Set(c => c.Active, false).Update();
    }

    public static void MapAdminEndpoints(this WebApplication app)
    {
        var api = app.MapGroup("/api").RequireAuthorization("AdminOnly");

        // Payments
        api.MapGet("/payments", async (string? status, Client sb) =>
        {
            var q = sb.From<Payment>().Order("created_at", Ordering.Descending);
            if (!string.IsNullOrEmpty(status)) q = q.Filter("status", Operator.Equals, status);
            return Results.Ok((await q.Get()).Models.Select(ToDto));
        });

        api.MapPatch("/payments/{id:guid}", async (Guid id, UpdatePaymentRequest req, Client sb) =>
        {
            var current = (await sb.From<Payment>().Filter("id", Operator.Equals, id.ToString()).Get()).Models.FirstOrDefault();
            if (current is null) return Results.NotFound();
            if (req.Status is not null)
            {
                if (!Statuses.Contains(req.Status)) return Results.BadRequest(new { error = "Invalid status." });
                current.Status = req.Status;
                if (req.Status == "invoiced" && current.InvoicedAt is null) current.InvoicedAt = DateTimeOffset.UtcNow;
                if (req.Status == "paid" && current.PaidAt is null) current.PaidAt = DateTimeOffset.UtcNow;
            }
            if (req.AmountOre is { } amount)
            {
                if (amount < 0) return Results.BadRequest(new { error = "Amount must be zero or more." });
                current.AmountOre = amount;
            }
            var updated = await current.Update<Payment>();
            return Results.Ok(ToDto(updated.Models.First()));
        });

        // Payment methods
        api.MapGet("/payment-methods", async (Client sb) =>
            Results.Ok((await sb.From<PaymentMethod>().Order("sort_order", Ordering.Ascending).Get()).Models
                .Select(m => new PaymentMethodDto(m.Code, m.Label, m.Timing, m.Active, m.SortOrder))));

        api.MapPatch("/payment-methods/{code}", async (string code, UpdatePaymentMethodRequest req, Client sb) =>
        {
            var res = await sb.From<PaymentMethod>().Filter("code", Operator.Equals, code).Set(m => m.Active, req.Active).Update();
            var m = res.Models.FirstOrDefault();
            return m is null ? Results.NotFound() : Results.Ok(new PaymentMethodDto(m.Code, m.Label, m.Timing, m.Active, m.SortOrder));
        });

        // Pricing tiers: PUT replaces the whole price list
        api.MapGet("/pricing/tiers", async (Client sb) =>
            Results.Ok((await sb.From<PricingTier>().Order("min_knives", Ordering.Ascending).Get()).Models
                .Select(t => new PricingTierDto(t.MinKnives, t.PricePerKnifeOre))));

        api.MapPut("/pricing/tiers", async (List<PricingTierDto> tiers, Client sb) =>
        {
            if (tiers.Count == 0 || !tiers.Any(t => t.MinKnives == 1)) return Results.BadRequest(new { error = "A price for 1 knife is required." });
            if (tiers.Any(t => t.MinKnives < 1 || t.MinKnives > 20 || t.PricePerKnifeOre < 0)) return Results.BadRequest(new { error = "Knife counts must be 1-20 and prices zero or more." });
            if (tiers.Select(t => t.MinKnives).Distinct().Count() != tiers.Count) return Results.BadRequest(new { error = "Duplicate knife counts." });
            await sb.From<PricingTier>().Upsert(tiers.Select(t => new PricingTier { MinKnives = t.MinKnives, PricePerKnifeOre = t.PricePerKnifeOre }).ToList());
            var keep = tiers.Select(t => (object)t.MinKnives.ToString()).ToList();
            await sb.From<PricingTier>().Not("min_knives", Operator.In, keep).Delete();
            return Results.Ok(tiers.OrderBy(t => t.MinKnives));
        });

        // Campaigns: at most one active at a time
        api.MapGet("/pricing/campaigns", async (Client sb) =>
            Results.Ok((await sb.From<PricingCampaign>().Order("created_at", Ordering.Descending).Get()).Models.Select(ToDto)));

        api.MapPost("/pricing/campaigns", async (CampaignRequest req, Client sb) =>
        {
            if (ValidateCampaign(req) is { } err) return Results.BadRequest(new { error = err });
            if (req.Active) await DeactivateOtherCampaigns(sb, null);
            var res = await sb.From<PricingCampaign>().Insert(new PricingCampaign
            {
                Id = Guid.NewGuid(), Name = req.Name.Trim(), FirstKnifePriceOre = req.FirstKnifePriceOre,
                StartsOn = req.StartsOn, EndsOn = req.EndsOn, Active = req.Active
            });
            return Results.Ok(ToDto(res.Models.First()));
        });

        api.MapPut("/pricing/campaigns/{id:guid}", async (Guid id, CampaignRequest req, Client sb) =>
        {
            if (ValidateCampaign(req) is { } err) return Results.BadRequest(new { error = err });
            if (req.Active) await DeactivateOtherCampaigns(sb, id);
            var res = await sb.From<PricingCampaign>().Filter("id", Operator.Equals, id.ToString())
                .Set(c => c.Name, req.Name.Trim()).Set(c => c.FirstKnifePriceOre, req.FirstKnifePriceOre)
                .Set(c => c.StartsOn!, req.StartsOn).Set(c => c.EndsOn!, req.EndsOn).Set(c => c.Active, req.Active).Update();
            var c = res.Models.FirstOrDefault();
            return c is null ? Results.NotFound() : Results.Ok(ToDto(c));
        });

        api.MapDelete("/pricing/campaigns/{id:guid}", async (Guid id, Client sb) =>
        {
            await sb.From<PricingCampaign>().Filter("id", Operator.Equals, id.ToString()).Delete();
            return Results.NoContent();
        });

        // Pickup hours: weekday 1 = Monday ... 7 = Sunday; deleting a weekday closes it
        api.MapGet("/pickup-hours", async (Client sb) =>
            Results.Ok((await sb.From<PickupHour>().Order("weekday", Ordering.Ascending).Get()).Models.Select(ToDto)));

        api.MapPut("/pickup-hours/{weekday:int}", async (int weekday, PickupHourDto req, Client sb) =>
        {
            if (weekday is < 1 or > 7) return Results.BadRequest(new { error = "Weekday must be 1-7." });
            if (!TimeOnly.TryParse(req.OpensAt, out var opens) || !TimeOnly.TryParse(req.ClosesAt, out var closes) || closes <= opens)
                return Results.BadRequest(new { error = "Closing time must be after opening time." });
            if (req.SlotMinutes is < 5 or > 240) return Results.BadRequest(new { error = "Slot length must be 5-240 minutes." });
            var res = await sb.From<PickupHour>().Upsert(new PickupHour { Weekday = (short)weekday, OpensAt = opens, ClosesAt = closes, SlotMinutes = req.SlotMinutes });
            return Results.Ok(ToDto(res.Models.First()));
        });

        api.MapDelete("/pickup-hours/{weekday:int}", async (int weekday, Client sb) =>
        {
            await sb.From<PickupHour>().Filter("weekday", Operator.Equals, weekday.ToString()).Delete();
            return Results.NoContent();
        });

        // Blocked dates
        api.MapGet("/blocked-dates", async (Client sb) =>
            Results.Ok((await sb.From<BlockedDate>().Order("blocked_date", Ordering.Ascending).Get()).Models
                .Select(b => new BlockedDateDto(b.Date, b.Reason))));

        api.MapPost("/blocked-dates", async (BlockedDateDto req, Client sb) =>
        {
            var reason = string.IsNullOrWhiteSpace(req.Reason) ? null : req.Reason.Trim()[..Math.Min(req.Reason.Trim().Length, 200)];
            var res = await sb.From<BlockedDate>().Upsert(new BlockedDate { Date = req.Date, Reason = reason });
            var b = res.Models.First();
            return Results.Ok(new BlockedDateDto(b.Date, b.Reason));
        });

        api.MapDelete("/blocked-dates/{date}", async (DateOnly date, Client sb) =>
        {
            await sb.From<BlockedDate>().Filter("blocked_date", Operator.Equals, date.ToString("yyyy-MM-dd")).Delete();
            return Results.NoContent();
        });
    }
}

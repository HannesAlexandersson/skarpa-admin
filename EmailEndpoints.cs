using System.Globalization;
using System.Net;
using System.Text.Json;
using Resend;
using SkarpaAdminApi.Models;
using Supabase;
using static Supabase.Postgrest.Constants;

namespace SkarpaAdminApi;

public static class EmailEndpoints
{
    public static void MapEmailEndpoints(this WebApplication app)
    {
        // 1. Manual Booking Confirmation (Admin UI button)
        app.MapPost("/api/bookings/{id:guid}/send-confirmation", async (
            Guid id,
            Client supabase,
            IResend resend,
            IConfiguration config) =>
        {
            var booking = await GetBookingWithCustomerAsync(supabase, id);
            if (booking is null)
                return Results.NotFound(new { error = "Booking not found." });

            var email = booking.Customer?.Email?.Trim();
            if (string.IsNullOrWhiteSpace(email))
                return Results.BadRequest(new { error = "Kunden saknar e-postadress." });

            try
            {
                await SendConfirmationEmailAsync(resend, config, booking, email);
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { error = $"Resend fel: {ex.Message}" });
            }

            var sentAt = DateTime.UtcNow;
            await supabase
                .From<Booking>()
                .Set(b => b.ConfirmationSentAt!, sentAt)
                .Where(b => b.Id == id)
                .Update();

            return Results.Ok(new { sentAt });
        }).RequireAuthorization("AdminOnly");

        // 2. Manual Payment Receipt (Admin UI button)
        app.MapPost("/api/bookings/{id:guid}/send-receipt", async (
            Guid id,
            Client supabase,
            IResend resend,
            IConfiguration config) =>
        {
            var booking = await GetBookingWithCustomerAsync(supabase, id);
            if (booking is null)
                return Results.NotFound(new { error = "Booking not found." });

            var email = booking.Customer?.Email?.Trim();
            if (string.IsNullOrWhiteSpace(email))
                return Results.BadRequest(new { error = "Kunden saknar e-postadress." });

            var paymentsResponse = await supabase
                .From<Payment>()
                .Filter("booking_id", Operator.Equals, id.ToString())
                .Order("created_at", Ordering.Ascending)
                .Get();
            var payment = paymentsResponse.Models.LastOrDefault();

            if (payment is null || payment.AmountOre is null || payment.AmountOre <= 0)
                return Results.BadRequest(new { error = "Ingen registrerad betalning med belopp hittades för bokningen." });

            try
            {
                await SendReceiptEmailAsync(resend, config, booking, payment, email);
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { error = $"Resend fel: {ex.Message}" });
            }

            var sentAt = DateTime.UtcNow;
            await supabase
                .From<Booking>()
                .Set(b => b.ReceiptSentAt!, sentAt)
                .Where(b => b.Id == id)
                .Update();

            return Results.Ok(new { sentAt });
        }).RequireAuthorization("AdminOnly");

        // 3. Automatic Booking Confirmation Webhook (for Supabase INSERT webhook)
        app.MapPost("/api/webhooks/booking-confirmation", async (
            HttpRequest request,
            Client supabase,
            IResend resend,
            IConfiguration config) =>
        {
            var expectedSecret = config["WebhookSecret"];
            var providedSecret = request.Headers["X-Webhook-Secret"].ToString();

            if (string.IsNullOrWhiteSpace(expectedSecret) ||
                !string.Equals(expectedSecret, providedSecret, StringComparison.Ordinal))
            {
                return Results.Unauthorized();
            }

            using var doc = await JsonDocument.ParseAsync(request.Body);
            if (!doc.RootElement.TryGetProperty("record", out var recordEl) ||
                !recordEl.TryGetProperty("id", out var idEl) ||
                !Guid.TryParse(idEl.GetString(), out var bookingId))
            {
                return Results.BadRequest(new { error = "Invalid Supabase webhook payload." });
            }

            var booking = await GetBookingWithCustomerAsync(supabase, bookingId);
            if (booking is null)
                return Results.NotFound(new { error = "Booking not found." });

            if (booking.ConfirmationSentAt is not null)
                return Results.Ok(new { skipped = true, reason = "Already sent" });

            var email = booking.Customer?.Email?.Trim();
            if (string.IsNullOrWhiteSpace(email))
                return Results.Ok(new { skipped = true, reason = "No customer email" });

            await SendConfirmationEmailAsync(resend, config, booking, email);

            var sentAt = DateTime.UtcNow;
            await supabase
                .From<Booking>()
                .Set(b => b.ConfirmationSentAt!, sentAt)
                .Where(b => b.Id == bookingId)
                .Update();

            return Results.Ok(new { sentAt });
        });
    }

    private static async Task<Booking?> GetBookingWithCustomerAsync(Client supabase, Guid id)
    {
        var response = await supabase
            .From<Booking>()
            .Filter("id", Operator.Equals, id.ToString())
            .Get();
        return response.Models.FirstOrDefault();
    }

    private static async Task SendConfirmationEmailAsync(
        IResend resend,
        IConfiguration config,
        Booking booking,
        string recipientEmail)
    {
        var fromEmail = config["Resend:FromEmail"] ?? "Skärpa <onboarding@resend.dev>";
        var name = WebUtility.HtmlEncode(booking.Customer?.Name ?? "Kund");
        var address = WebUtility.HtmlEncode(
            !string.IsNullOrWhiteSpace(booking.PickupAddress)
                ? booking.PickupAddress
                : booking.Customer?.Address ?? "Ej angiven");
        var dateText = WebUtility.HtmlEncode(booking.PickupDate.ToString("yyyy-MM-dd"));
        var timeText = WebUtility.HtmlEncode(booking.PickupTime.ToString("HH:mm"));
        var shortRef = booking.Id.ToString()[..8].ToUpperInvariant();

        var html = $"""
            <div style="font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif; color: #18181b; max-width: 520px; margin: 0 auto; line-height: 1.6;">
              <h2 style="margin-bottom: 8px;">Bokningsbekräftelse – Skärpa</h2>
              <p>Hej {name},</p>
              <p>Tack för din bokning! Vi har tagit emot din beställning av knivslipning med upphämtning.</p>
              <div style="background: #f4f4f5; border-radius: 8px; padding: 16px; margin: 20px 0;">
                <p style="margin: 4px 0;"><strong>Bokningsreferens:</strong> #{shortRef}</p>
                <p style="margin: 4px 0;"><strong>Datum:</strong> {dateText}</p>
                <p style="margin: 4px 0;"><strong>Tid:</strong> {timeText}</p>
                <p style="margin: 4px 0;"><strong>Upphämtningsadress:</strong> {address}</p>
                <p style="margin: 4px 0;"><strong>Antal knivar:</strong> {booking.KnifeCount} st</p>
              </div>
              <p>Har du några frågor eller behöver ändra tid inför upphämtningen är det bara att svara på detta mejl.</p>
              <p style="margin-top: 24px;">Vänliga hälsningar,<br/><strong>Skärpa</strong></p>
            </div>
            """;

        var message = new EmailMessage
        {
            From = fromEmail,
            Subject = $"Bokningsbekräftelse – Skärpa (#{shortRef})",
            HtmlBody = html
        };
        message.To.Add(recipientEmail);

        await resend.EmailSendAsync(message);
    }

    private static async Task SendReceiptEmailAsync(
        IResend resend,
        IConfiguration config,
        Booking booking,
        Payment payment,
        string recipientEmail)
    {
        var fromEmail = config["Resend:FromEmail"] ?? "Skärpa <onboarding@resend.dev>";
        var stockholmTz = TimeZoneInfo.FindSystemTimeZoneById("Europe/Stockholm");
        var localDate = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, stockholmTz).ToString("yyyy-MM-dd");

        var name = WebUtility.HtmlEncode(booking.Customer?.Name ?? "Kund");
        var shortRef = booking.Id.ToString()[..8].ToUpperInvariant();
        var sekAmount = ((payment.AmountOre ?? 0) / 100m).ToString("0.00", new CultureInfo("sv-SE"));
        var methodLabel = WebUtility.HtmlEncode((payment.MethodCode ?? "Övrigt").ToUpperInvariant() switch
        {
            "SWISH" => "Swish",
            "CARD" or "KORT" => "Kort",
            "INVOICE" or "FAKTURA" => "Faktura",
            var other => other
        });

        var html = $"""
            <div style="font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif; color: #18181b; max-width: 520px; margin: 0 auto; line-height: 1.6;">
              <h2 style="margin-bottom: 8px;">Kvitto – Skärpa</h2>
              <p>Hej {name},</p>
              <p>Tack för att du anlitade Skärpa! Här kommer ditt kvitto för utförd knivslipning.</p>
              <div style="background: #f4f4f5; border-radius: 8px; padding: 16px; margin: 20px 0;">
                <p style="margin: 4px 0;"><strong>Kvittonummer:</strong> #{shortRef}</p>
                <p style="margin: 4px 0;"><strong>Datum:</strong> {localDate}</p>
                <p style="margin: 4px 0;"><strong>Tjänst:</strong> Knivslipning ({booking.KnifeCount} st knivar)</p>
                <p style="margin: 4px 0;"><strong>Betalsätt:</strong> {methodLabel}</p>
                <hr style="border: none; border-top: 1px solid #d4d4d8; margin: 12px 0;" />
                <p style="margin: 4px 0; font-size: 16px;"><strong>Totalt belopp:</strong> {sekAmount} kr</p>
              </div>
              <p style="margin-top: 24px;">Vänliga hälsningar,<br/><strong>Skärpa</strong></p>
            </div>
            """;

        var message = new EmailMessage
        {
            From = fromEmail,
            Subject = $"Kvitto – Skärpa (#{shortRef})",
            HtmlBody = html
        };
        message.To.Add(recipientEmail);

        await resend.EmailSendAsync(message);
    }
}
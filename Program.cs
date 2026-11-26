using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using SkarpaAdminApi;
using SkarpaAdminApi.Models;
using Supabase;
using static Supabase.Postgrest.Constants;

var builder = WebApplication.CreateBuilder(args);

var url = builder.Configuration["Supabase:Url"]
    ?? throw new InvalidOperationException("Supabase:Url is missing.");
var key = builder.Configuration["Supabase:SecretKey"]
    ?? throw new InvalidOperationException("Supabase:SecretKey is missing.");
var adminEmail = builder.Configuration["AdminEmail"]
    ?? throw new InvalidOperationException("AdminEmail is missing.");

// 1. Supabase client (service_role for DB & Storage queries)
builder.Services.AddScoped(_ => new Client(url, key, new SupabaseOptions
{
    AutoRefreshToken = false,
    AutoConnectRealtime = false
}));

// 2. CORS (allow local dev + your Vercel frontend)
var allowedOrigins = builder.Configuration["AllowedOrigins"]?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
    ?? ["http://localhost:3000"];

builder.Services.AddCors(options =>
{
    options.AddPolicy("AdminFrontend", policy =>
        policy.WithOrigins(allowedOrigins)
              .AllowAnyHeader()
              .AllowAnyMethod());
});

// 3. Supabase Auth JWT Verification
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.Authority = $"{url}/auth/v1";
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = $"{url}/auth/v1",
            ValidateAudience = true,
            ValidAudience = "authenticated",
            ValidateLifetime = true
        };
    });

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("AdminOnly", policy =>
        policy.RequireAuthenticatedUser()
              .RequireAssertion(ctx =>
              {
                  var email = ctx.User.FindFirstValue(ClaimTypes.Email) ?? ctx.User.FindFirstValue("email");
                  return string.Equals(email, adminEmail, StringComparison.OrdinalIgnoreCase);
              }));
});

var app = builder.Build();

app.UseCors("AdminFrontend");
app.UseAuthentication();
app.UseAuthorization();

// 4. Protected Endpoints
app.MapGet("/api/bookings", async (DateOnly? date, Client supabase) =>
{
    var stockholmTz = TimeZoneInfo.FindSystemTimeZoneById("Europe/Stockholm");
    var localNow = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, stockholmTz);
    var targetDate = date ?? DateOnly.FromDateTime(localNow);

    var response = await supabase
        .From<Booking>()
        .Filter("pickup_date", Operator.Equals, targetDate.ToString("yyyy-MM-dd"))
        .Order("pickup_time", Ordering.Ascending)
        .Get();

    var bookingIds = response.Models.Select(b => (object)b.Id.ToString()).ToList();
    var payments = bookingIds.Count == 0 ? new List<Payment>() : (await supabase
        .From<Payment>()
        .Filter("booking_id", Operator.In, bookingIds)
        .Order("created_at", Ordering.Ascending)
        .Get()).Models;

    var results = response.Models.Select(b =>
    {
        var payment = payments.LastOrDefault(p => p.BookingId == b.Id);
        return new PickupBookingDto(
            Id: b.Id,
            PickupDate: b.PickupDate,
            PickupTime: b.PickupTime,
            PickupAddress: !string.IsNullOrWhiteSpace(b.PickupAddress)
                ? b.PickupAddress
                : b.Customer?.Address ?? "No address",
            CustomerId: b.CustomerId,
            CustomerName: b.Customer?.Name ?? "Unknown",
            CustomerPhone: b.Customer?.Phone ?? "No phone",
            CustomerEmail: b.Customer?.Email,
            KnifeCount: b.KnifeCount,
            PaymentId: payment?.Id,
            PaymentMethod: payment?.MethodCode,
            PaymentStatus: payment?.Status,
            AmountOre: payment?.AmountOre,
            WithdrawalConsentAt: b.WithdrawalConsentAt,
            WithdrawalConsentText: b.WithdrawalConsentText,
            WithdrawalConsentVersion: b.WithdrawalConsentVersion
        );
    });

    return Results.Ok(results);
}).RequireAuthorization("AdminOnly");

app.MapGet("/api/bookings/{id:guid}", async (Guid id, Client supabase) =>
{
    var booking = await supabase
        .From<Booking>()
        .Filter("id", Operator.Equals, id.ToString())
        .Single();

    if (booking is null)
        return Results.NotFound(new { error = "Booking not found" });

    var paymentsResponse = await supabase
        .From<Payment>()
        .Filter("booking_id", Operator.Equals, id.ToString())
        .Order("created_at", Ordering.Ascending)
        .Get();
    var payment = paymentsResponse.Models.LastOrDefault();

    var photosResponse = await supabase
        .From<BookingPhoto>()
        .Filter("booking_id", Operator.Equals, id.ToString())
        .Order("created_at", Ordering.Descending)
        .Get();

    var photoDtos = new List<BookingPhotoDto>();
    foreach (var p in photosResponse.Models)
    {
        var signedUrl = string.Empty;
        try
        {
            signedUrl = await supabase.Storage
                .From("booking-photos")
                .CreateSignedUrl(p.StoragePath, 3600);
        }
        catch
        {
            // Keep empty URL if storage object was removed externally
        }

        photoDtos.Add(new BookingPhotoDto(
            Id: p.Id,
            BookingId: p.BookingId,
            Url: signedUrl,
            Note: p.Note,
            CreatedAt: p.CreatedAt
        ));
    }

    var detail = new BookingDetailDto(
        Id: booking.Id,
        PickupDate: booking.PickupDate,
        PickupTime: booking.PickupTime,
        PickupAddress: !string.IsNullOrWhiteSpace(booking.PickupAddress)
            ? booking.PickupAddress
            : booking.Customer?.Address ?? "No address",
        CustomerId: booking.CustomerId,
        CustomerName: booking.Customer?.Name ?? "Unknown",
        CustomerPhone: booking.Customer?.Phone ?? "No phone",
        CustomerEmail: booking.Customer?.Email,
        KnifeCount: booking.KnifeCount,
        PaymentId: payment?.Id,
        PaymentMethod: payment?.MethodCode,
        PaymentStatus: payment?.Status,
        AmountOre: payment?.AmountOre,
        WithdrawalConsentAt: booking.WithdrawalConsentAt,
        WithdrawalConsentText: booking.WithdrawalConsentText,
        WithdrawalConsentVersion: booking.WithdrawalConsentVersion,
        Photos: photoDtos
    );

    return Results.Ok(detail);
}).RequireAuthorization("AdminOnly");

app.MapPost("/api/bookings/{id:guid}/photos", async (
    Guid id,
    HttpRequest request,
    Client supabase) =>
{
    if (!request.HasFormContentType)
        return Results.BadRequest(new { error = "Expected multipart form data." });

    var form = await request.ReadFormAsync();
    var file = form.Files.GetFile("file");
    var note = form["note"].FirstOrDefault();

    if (file is null || file.Length == 0)
        return Results.BadRequest(new { error = "No image file provided." });

    var booking = await supabase
        .From<Booking>()
        .Filter("id", Operator.Equals, id.ToString())
        .Single();

    if (booking is null)
        return Results.NotFound(new { error = "Booking not found." });

    using var ms = new MemoryStream();
    await file.CopyToAsync(ms);
    var bytes = ms.ToArray();

    var storagePath = $"{id}/{Guid.NewGuid():N}.jpg";

    await supabase.Storage
        .From("booking-photos")
        .Upload(bytes, storagePath, new Supabase.Storage.FileOptions
        {
            ContentType = string.IsNullOrWhiteSpace(file.ContentType) ? "image/jpeg" : file.ContentType,
            Upsert = false
        });

    var photo = new BookingPhoto
    {
        BookingId = id,
        StoragePath = storagePath,
        Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim()
    };

    var insertRes = await supabase.From<BookingPhoto>().Insert(photo);
    var created = insertRes.Models.First();

    var signedUrl = await supabase.Storage
        .From("booking-photos")
        .CreateSignedUrl(created.StoragePath, 3600);

    var dto = new BookingPhotoDto(
        Id: created.Id,
        BookingId: created.BookingId,
        Url: signedUrl,
        Note: created.Note,
        CreatedAt: created.CreatedAt
    );

    return Results.Ok(dto);
})
.RequireAuthorization("AdminOnly")
.DisableAntiforgery();

app.MapDelete("/api/bookings/{id:guid}/photos/{photoId:guid}", async (
    Guid id,
    Guid photoId,
    Client supabase) =>
{
    var photo = await supabase
        .From<BookingPhoto>()
        .Filter("id", Operator.Equals, photoId.ToString())
        .Filter("booking_id", Operator.Equals, id.ToString())
        .Single();

    if (photo is null)
        return Results.NotFound(new { error = "Photo not found." });

    try
    {
        await supabase.Storage
            .From("booking-photos")
            .Remove(new List<string> { photo.StoragePath });
    }
    catch
    {
        // Continue deleting DB record if file was already deleted
    }

    await supabase
        .From<BookingPhoto>()
        .Filter("id", Operator.Equals, photoId.ToString())
        .Delete();

    return Results.NoContent();
}).RequireAuthorization("AdminOnly");

app.MapGet("/api/customers", async (Client supabase) =>
{
    var response = await supabase
        .From<Customer>()
        .Order("name", Ordering.Ascending)
        .Get();

    var results = response.Models.Select(c => new CustomerDto(
        Id: c.Id,
        Name: c.Name ?? "Unknown",
        Address: c.Address ?? "",
        Phone: c.Phone ?? "",
        Email: c.Email,
        CreatedAt: c.CreatedAt
    ));

    return Results.Ok(results);
}).RequireAuthorization("AdminOnly");

app.MapAdminEndpoints();

app.Run();
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

// 1. Supabase client (service_role for DB queries)
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

    var results = response.Models.Select(b => {
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
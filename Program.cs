using SkarpaAdminApi.Models;
using Supabase;
using static Supabase.Postgrest.Constants;

var builder = WebApplication.CreateBuilder(args);

// 1. Register Supabase Client
var url = builder.Configuration["Supabase:Url"]
    ?? throw new InvalidOperationException("Supabase:Url is missing.");
var key = builder.Configuration["Supabase:SecretKey"]
    ?? throw new InvalidOperationException("Supabase:SecretKey is missing.");

builder.Services.AddScoped(_ => new Client(url, key, new SupabaseOptions
{
    AutoRefreshToken = false,
    AutoConnectRealtime = false
}));

// 2. CORS for local dev and your Vercel admin frontend
builder.Services.AddCors(options =>
{
    options.AddPolicy("AdminFrontend", policy =>
        policy.WithOrigins("http://localhost:3000", "http://localhost:5173", "https://your-admin-app.vercel.app")
              .AllowAnyHeader()
              .AllowAnyMethod());
});

var app = builder.Build();
app.UseCors("AdminFrontend");

// 3. API Key Middleware
app.Use(async (context, next) =>
{
    var expectedKey = app.Configuration["AdminApiKey"];
    if (string.IsNullOrEmpty(expectedKey) ||
        !context.Request.Headers.TryGetValue("X-Api-Key", out var providedKey) ||
        providedKey != expectedKey)
    {
        context.Response.StatusCode = 401;
        await context.Response.WriteAsync("Unauthorized");
        return;
    }

    await next();
});

// 4. Endpoints

// GET /api/bookings (defaults to today in Sweden) or /api/bookings?date=2026-11-18
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

    var results = response.Models.Select(b => new PickupBookingDto(
        Id: b.Id,
        PickupDate: b.PickupDate,
        PickupTime: b.PickupTime,
        PickupAddress: !string.IsNullOrWhiteSpace(b.PickupAddress)
            ? b.PickupAddress
            : b.Customer?.Address ?? "No address",
        CustomerId: b.CustomerId,
        CustomerName: b.Customer?.Name ?? "Unknown",
        CustomerPhone: b.Customer?.Phone ?? "No phone",
        CustomerEmail: b.Customer?.Email
    ));

    return Results.Ok(results);
});

// GET /api/customers
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
});

app.Run();
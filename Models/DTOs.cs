using System;

namespace SkarpaAdminApi.Models;

public record PickupBookingDto(
    Guid Id,
    DateOnly PickupDate,
    TimeOnly PickupTime,
    string PickupAddress,
    Guid CustomerId,
    string CustomerName,
    string CustomerPhone,
    string? CustomerEmail
);

public record CustomerDto(
    Guid Id,
    string Name,
    string Address,
    string Phone,
    string? Email,
    DateTimeOffset CreatedAt
);

public record BookingPhotoDto(
    Guid Id,
    Guid BookingId,
    string Url,
    string? Note,
    DateTimeOffset CreatedAt
);

public record BookingDetailDto(
    Guid Id,
    DateOnly PickupDate,
    TimeOnly PickupTime,
    string PickupAddress,
    Guid CustomerId,
    string CustomerName,
    string CustomerPhone,
    string? CustomerEmail,
    List<BookingPhotoDto> Photos
);
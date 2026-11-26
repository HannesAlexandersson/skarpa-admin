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
    string? CustomerEmail,
    int KnifeCount,
    Guid? PaymentId,
    string? PaymentMethod,
    string? PaymentStatus,
    int? AmountOre,
    DateTimeOffset? WithdrawalConsentAt,
    string? WithdrawalConsentText,
    string? WithdrawalConsentVersion
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
    int KnifeCount,
    Guid? PaymentId,
    string? PaymentMethod,
    string? PaymentStatus,
    int? AmountOre,
    DateTimeOffset? WithdrawalConsentAt,
    string? WithdrawalConsentText,
    string? WithdrawalConsentVersion,
    List<BookingPhotoDto> Photos,
    DateTime? ConfirmationSentAt,
    DateTime? ReceiptSentAt
);

public record CustomerDto(
    Guid Id,
    string Name,
    string Address,
    string Phone,
    string? Email,
    DateTimeOffset CreatedAt
);

public record PaymentDto(Guid Id, Guid BookingId, string MethodCode, string Status, int? AmountOre, string Currency,
    DateTimeOffset? InvoicedAt, DateTimeOffset? PaidAt, DateTimeOffset CreatedAt);
public record UpdatePaymentRequest(string? Status, int? AmountOre);

public record PaymentMethodDto(string Code, string Label, string Timing, bool Active, short SortOrder);
public record UpdatePaymentMethodRequest(bool Active);

public record PricingTierDto(short MinKnives, int PricePerKnifeOre);
public record CampaignDto(Guid Id, string Name, int FirstKnifePriceOre, DateOnly? StartsOn, DateOnly? EndsOn, bool Active);
public record CampaignRequest(string Name, int FirstKnifePriceOre, DateOnly? StartsOn, DateOnly? EndsOn, bool Active);

public record PickupHourDto(short Weekday, string OpensAt, string ClosesAt, int SlotMinutes);
public record BlockedDateDto(DateOnly Date, string? Reason);
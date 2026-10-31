using BookingService.Entities;

namespace BookingService.Dto.Response;

public record BookingStatusHistoryResponse(
    long Id,
    long BookingId,
    BookingStatus? StatusFrom,
    BookingStatus StatusTo,
    DateTimeOffset ChangedAt);
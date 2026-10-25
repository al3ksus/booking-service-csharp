using BookingService.Dto.Response;
using BookingService.Entities;

namespace BookingService.Mappers;

public class BookingStatusHistoryMapper
{
    public BookingStatusHistoryResponse ToResponse(BookingStatusHistory history)
        => new(
            history.Id,
            history.BookingId,
            history.StatusFrom,
            history.StatusTo,
            history.ChangedAt
        );

    public List<BookingStatusHistoryResponse> ToResponseList(List<BookingStatusHistory> histories)
        => histories.Select(ToResponse).ToList();
}
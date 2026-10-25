using BookingService.Dto.Request;
using BookingService.Dto.Response;
using BookingService.Entities;
using BookingService.Mappers;
using BookingService.Services;
using Microsoft.AspNetCore.Mvc;

namespace BookingService.Controllers;

/// <summary>
/// REST контроллер для работы с бронированиями
/// </summary>
[ApiController]
[Route("api/booking")]
public class BookingController : ControllerBase
{
    private readonly Services.BookingService _bookingService;
    private readonly BookingMapper _mapper;

    public BookingController(Services.BookingService bookingService, BookingMapper mapper)
    {
        _bookingService = bookingService;
        _mapper = mapper;
    }

    /// <summary>Создать новое бронирование</summary>
    [HttpPost]
    [ProducesResponseType<long>(StatusCodes.Status200OK)]
    public async Task<long> Create([FromBody] CreateBookingRequest request)
        => await _bookingService.CreateBooking(request.UserId, request.ResourceId, request.BookedFrom, request.BookedTo);

    /// <summary>Получить бронирование по ID</summary>
    [HttpGet("{id:long}")]
    [ProducesResponseType<BookingResponse>(StatusCodes.Status200OK)]
    public async Task<BookingResponse> GetById([FromRoute] long id)
    {
        var booking = await _bookingService.GetById(id);
        return _mapper.ToResponse(booking);
    }

    /// <summary>Получить список бронирований с фильтрацией и пагинацией</summary>
    [HttpPost("by-filter")]
    [ProducesResponseType<List<BookingResponse>>(StatusCodes.Status200OK)]
    public async Task<List<BookingResponse>> GetByFilter([FromBody] GetBookingsByFilterRequest request)
    {
        var bookings = await _bookingService.GetByFilter(
            request.UserId,
            request.ResourceId,
            request.Status,
            request.PageNumber,
            request.PageSize
        );

        return _mapper.ToResponseList(bookings);
    }

    /// <summary>Получить статус бронирования по ID</summary>
    [HttpGet("{id:long}/status")]
    [ProducesResponseType<BookingStatus>(StatusCodes.Status200OK)]
    public async Task<BookingStatus?> GetStatus([FromRoute] long id)
        => await _bookingService.GetStatusById(id);

    /// <summary>Отменить бронирование</summary>
    [HttpPost("{id:long}/cancel")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task Cancel([FromRoute] long id)
        => await _bookingService.CancelBooking(id);

    /// <summary>
    /// Получить статистику по бронированиям
    /// </summary>
    [HttpGet("statistics")]
    [ProducesResponseType<StatisticsResponse>(StatusCodes.Status200OK)]
    public async Task<StatisticsResponse> GetStatistics()
        => await _bookingService.GetStatistics();

    [HttpGet("{bookingId:long}/history")]
    public async Task<List<BookingStatusHistory>> GetBookingHistory(long bookingId)
    {
        return await _bookingService.GetBookingHistory(bookingId);
    }

}

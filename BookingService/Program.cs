using BookingService.Configuration;
using BookingService.Exceptions;
using BookingService.Infrastructure.BackgroundJobs;
using BookingService.Infrastructure.Data;
using BookingService.Infrastructure.Data.Transactions;
using BookingService.Infrastructure.Messaging;
using BookingService.Infrastructure.Messaging.Contracts;
using BookingService.Infrastructure.Notifications;
using BookingService.Mappers;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Http.Resilience;
using Polly;
using Rebus.Config;
using Rebus.Routing.TypeBased;
using Rebus.ServiceProvider;

var builder = WebApplication.CreateBuilder(args);

// ---- Configuration ----
var rabbitMqSettings = builder.Configuration
    .GetSection("RabbitMq")
    .Get<RabbitMqSettings>()!;

// ---- Controllers & OpenAPI ----
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        // Сериализуем enum как строки (AwaitConfirmation вместо 1)
        options.JsonSerializerOptions.Converters.Add(
            new System.Text.Json.Serialization.JsonStringEnumConverter());
    });

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new() { Title = "Booking Service", Version = "v1" });
});

// ---- Database ----
builder.Services.AddDbContext<BookingDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.AddScoped<BookingRepository>();
builder.Services.AddScoped<ProcessedEventRepository>();
builder.Services.AddScoped<OutboxMessageRepository>();
builder.Services.AddScoped<BookingTransactionManager>();

// ---- Business Services ----
builder.Services.AddScoped<BookingService.Services.BookingService>();
builder.Services.AddScoped<BookingMapper>();
builder.Services.AddScoped<BookingStatusHistoryMapper>();
builder.Services.AddSingleton<ICurrentDateTimeProvider, CurrentDateTimeProvider>();
builder.Services.AddHostedService<StuckCancellationsJob>();
builder.Services.AddHostedService<OutboxProcessorJob>();

// ---- Messaging (Rebus + RabbitMQ) ----
builder.Services.AddSingleton(rabbitMqSettings);
builder.Services.AddScoped<BookingEventPublisher>();

// ---- Http ----
builder.Services
    .AddHttpClient<INotificationService, NotificationService>(client =>
    {
        client.BaseAddress = new Uri(
            builder.Configuration["NotificationService:BaseUrl"]!);
    })
    .AddResilienceHandler("notification-retry", pipeline =>
    {
        pipeline.AddRetry(new HttpRetryStrategyOptions
        {
            MaxRetryAttempts = builder.Configuration
                .GetValue<int>("NotificationService:Retry:MaxAttempts"),

            Delay = TimeSpan.FromSeconds(builder.Configuration
                .GetValue<double>("NotificationService:Retry:DelaySeconds")),

            BackoffType = DelayBackoffType.Constant,

            ShouldHandle = new PredicateBuilder<HttpResponseMessage>()
                .Handle<HttpRequestException>()
                .HandleResult(response =>
                    (int)response.StatusCode >= 500)
        });
    });

builder.Services.AddRebus(
    configure => configure
        .Transport(t => t
            .UseRabbitMq(rabbitMqSettings.ConnectionString, rabbitMqSettings.InputQueue)
            .ExchangeNames(rabbitMqSettings.DirectExchange, rabbitMqSettings.TopicExchange))
        .Routing(r => r.TypeBased()
            .Map<CreateBookingJobRequest>(rabbitMqSettings.InputQueue)
            .Map<CancelBookingJobByRequestIdRequest>(rabbitMqSettings.InputQueue)
            .Map<BookingJobConfirmed>(rabbitMqSettings.InputQueue)
            .Map<BookingJobDenied>(rabbitMqSettings.InputQueue)),
    onCreated: async bus =>
    {
        await bus.Subscribe<BookingJobConfirmed>();
        await bus.Subscribe<BookingJobDenied>();
    });

builder.Services.AddRebusHandler<BookingEventsHandler>();
builder.Services.AddRebusHandler<CancelBookingErrorsHandler>();

// ---- App ----
var app = builder.Build();

// Применить миграции БД при старте
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<BookingDbContext>();
    await db.Database.MigrateAsync();
}

// Глобальная обработка ошибок (RFC 7807 Problem Details)
app.UseExceptionHandler(exceptionApp =>
{
    exceptionApp.Run(async context =>
    {
        var exceptionFeature = context.Features.Get<IExceptionHandlerFeature>();
        var exception = exceptionFeature?.Error;

        if (exception is BusinessException businessEx)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            context.Response.ContentType = "application/problem+json";

            var problem = new ProblemDetails
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "Business Error",
                Detail = businessEx.Message
            };

            await context.Response.WriteAsJsonAsync(problem);
        }
        else
        {
            var logger = context.RequestServices.GetRequiredService<ILogger<Program>>();
            logger.LogError(exception, "Unexpected error");

            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            context.Response.ContentType = "application/problem+json";

            var problem = new ProblemDetails
            {
                Status = StatusCodes.Status500InternalServerError,
                Title = "Internal Server Error",
                Detail = "An unexpected error occurred"
            };

            await context.Response.WriteAsJsonAsync(problem);
        }
    });
});

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c => c.SwaggerEndpoint("/swagger/v1/swagger.json", "Booking Service v1"));
}

app.UseRouting();
app.MapControllers();

app.Run();

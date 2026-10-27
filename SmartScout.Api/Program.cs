using Microsoft.AspNetCore.RateLimiting;
using SmartScout.Api.Middleware;
using SmartScout.Application;
using SmartScout.Infrastructure;
using System;
using System.Threading.RateLimiting;

// Enable legacy timestamp behavior for PostgreSQL Npgsql to prevent ArgumentException with Unspecified DateTime
AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddOpenApi();
builder.Services.AddSwaggerGen();

// Global Exception Handling & ProblemDetails
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddProblemDetails();

// CORS Policy
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowFrontend", policy =>
    {
        policy.WithOrigins("http://localhost:3000", "http://localhost:5173", "https://localhost:3000")
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials();
    });
});

// Built-in .NET Rate Limiting
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    // Global limiter per IP (100 req/min)
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(httpContext =>
    {
        string clientIp = httpContext.Connection.RemoteIpAddress?.ToString() ?? "anonymous";
        return RateLimitPartition.GetFixedWindowLimiter(clientIp, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 100,
            Window = TimeSpan.FromMinutes(1),
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            QueueLimit = 5
        });
    });

    // Strict policy for heavy sync operations
    options.AddFixedWindowLimiter("SyncPolicy", opt =>
    {
        opt.PermitLimit = 5;
        opt.Window = TimeSpan.FromMinutes(5);
        opt.QueueLimit = 0;
    });
});

// Add Health Checks
builder.Services.AddHealthChecks();

// Wire up Application and Infrastructure
builder.Services.AddApplicationServices();
builder.Services.AddInfrastructureServices(builder.Configuration);

// Add Background Services
builder.Services.AddHostedService<SmartScout.Api.BackgroundServices.SeasonStatSyncBackgroundService>();

var app = builder.Build();

// Global exception handling (first in pipeline)
app.UseExceptionHandler();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseSwagger();
    app.UseSwaggerUI();
}
else
{
    app.UseHttpsRedirection();
}

app.UseDefaultFiles();
app.UseStaticFiles();

app.UseCors("AllowFrontend");
app.UseRateLimiter();

app.UseAuthorization();
app.MapControllers();
app.MapHealthChecks("/health");

app.Run();

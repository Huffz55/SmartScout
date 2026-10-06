using SmartScout.Application;
using SmartScout.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddOpenApi();
builder.Services.AddSwaggerGen();

// Wire up Application and Infrastructure
builder.Services.AddApplicationServices();
builder.Services.AddInfrastructureServices(builder.Configuration);

// Add Background Services
// builder.Services.AddHostedService<SmartScout.Api.BackgroundServices.PlayerSyncBackgroundService>();
builder.Services.AddHostedService<SmartScout.Api.BackgroundServices.SeasonStatSyncBackgroundService>();

var app = builder.Build();

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

app.UseAuthorization();
app.MapControllers();

app.Run();

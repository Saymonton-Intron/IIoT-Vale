using IIoTVale.Backend.API.Services;
using IIoTVale.Backend.API.Workers;
using IIoTVale.Backend.API.Wrappers;
using Serilog;

void ConfigureSerilog(ConfigurationManager configurationManager, ConfigureHostBuilder host)
{
    var absoluteLogPath = Path.Combine(AppContext.BaseDirectory, "logs", "log-.json");
    configurationManager["Serilog:WriteTo:0:Args:path"] = absoluteLogPath;
    // Ensure the directory exists
    Directory.CreateDirectory(Path.GetDirectoryName(absoluteLogPath) ?? AppContext.BaseDirectory);

    // Configure Host to use serilog and use appsettings.json configurations
    host.UseSerilog((context, services, lc) => lc
        .ReadFrom.Configuration(context.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext());
}

var builder = WebApplication.CreateBuilder(args);

ConfigureSerilog(builder.Configuration, builder.Host);
// Add services to the container.

builder.Services.AddHostedService<MqttListenerWorker>();
builder.Services.AddHostedService<DbProcessorWorker>();
builder.Services.AddHostedService<UiProcessorWorker>();
builder.Services.AddControllers();
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

builder.Services.AddSingleton<DbChannel>();
builder.Services.AddSingleton<UiChannel>();

builder.Services.AddSingleton<DatabaseService>();

builder.Services.AddSingleton<WebSocketHandler>();

int result = 0;
try
{
    var app = builder.Build();

    Log.Information("Starting web host");

    app.UseSerilogRequestLogging();

    // Configure the HTTP request pipeline.
    if (app.Environment.IsDevelopment())
    {
        app.MapOpenApi();
    }

    app.UseHttpsRedirection();

    app.UseAuthorization();

    app.MapControllers();

    app.UseCors(policy => policy // Alterar depois
        .WithOrigins("http://192.168.7.6:3000/")
        .AllowAnyMethod()
        .AllowAnyHeader()
        .AllowCredentials());

    app.UseWebSockets();

    app.Map("/ws", async (HttpContext context, WebSocketHandler manager) =>
    {
        if (context.WebSockets.IsWebSocketRequest)
        {
            using var webSocket = await context.WebSockets.AcceptWebSocketAsync();
            await manager.AddConnection(webSocket);
        }
        else
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
        }
    });

    await app.RunAsync();

    result = 0;
}
catch (Exception ex)
{
    // Ensure startup exceptions are logged
    Log.Fatal(ex, "Host terminated unexpectedly");
    result = 1;
}
finally
{
    Log.CloseAndFlush();
}

return result;
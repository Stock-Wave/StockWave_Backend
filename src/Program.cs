using Amazon.Lambda.AspNetCoreServer.Hosting;
using StockWaveApi.Data;
using StockWaveApi.Models;
using StockWaveApi.Services;

var builder = WebApplication.CreateBuilder(args);

// Habilita que esta misma app corra tanto en Lambda (detectado automáticamente
// por variables de entorno de AWS) como localmente con `dotnet run`.
builder.Services.AddAWSLambdaHosting(LambdaEventSource.RestApi);

builder.Services.AddControllers();

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

// Inyección de dependencias: cada capa recibe la anterior por constructor
builder.Services.AddSingleton<DbConnectionFactory>();
builder.Services.AddScoped<ProductRepository>();
builder.Services.AddScoped<OrderRepository>();
builder.Services.AddScoped<ProductService>();
builder.Services.AddScoped<OrderService>();

var app = builder.Build();

app.UseCors();

// Manejo centralizado de excepciones no controladas: nunca deja pasar un 500
// crudo sin el formato consistente de error que exige la API.
app.Use(async (context, next) =>
{
    try
    {
        await next();
    }
    catch (Exception ex)
    {
        context.Response.StatusCode = 500;
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsJsonAsync(new ErrorResponse
        {
            Error = "Error interno inesperado.",
            Details = ex.Message,
        });
    }
});

app.MapControllers();

app.Run();
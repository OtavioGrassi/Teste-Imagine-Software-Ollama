var builder = WebApplication.CreateBuilder(args);

// Adicione esta linha para registrar o serviço de Controllers
builder.Services.AddControllers(); 

// Se você não for usar o OpenAPI, pode remover as linhas que usam 'OpenApi' e 'mapOpenApi'.
// Por enquanto, vou deixá-las, mas Adicione esta linha abaixo dos outros serviços:
builder.Services.AddOpenApi();


var app = builder.Build();

// Configure o pipeline HTTP.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

// Remova ou Comente o código do WeatherForecast se não for usar.
/*
var summaries = new[]
{
    "Freezing", "Bracing", "Chilly", "Cool", "Mild", "Warm", "Balmy", "Hot", "Sweltering", "Scorching"
};

app.MapGet("/weatherforecast", () =>
{
    var forecast = Enumerable.Range(1, 5).Select(index =>
        new WeatherForecast
        (
            DateOnly.FromDateTime(DateTime.Now.AddDays(index)),
            Random.Shared.Next(-20, 55),
            summaries[Random.Shared.Next(summaries.Length)]
        ))
        .ToArray();
    return forecast;
})
.WithName("GetWeatherForecast");
*/

// Adicione esta linha para que o aplicativo saiba como rotear os Controllers
app.MapControllers(); 

app.Run();

// Mantenha o Record WeatherForecast se você não o removeu acima.
record WeatherForecast(DateOnly Date, int TemperatureC, string? Summary)
{
    public int TemperatureF => 32 + (int)(TemperatureC / 0.5556);
}
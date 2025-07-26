using LeaderboardWebApi.Controllers;
using LeaderboardWebApi.Services;
using LeaderboardWebApi.Models;
using Microsoft.OpenApi.Models;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddSingleton<ILeaderboardService, LeaderboardService>();
builder.Services.AddControllers();
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Leaderboard API",
        Version = "v1",
        Description = "HTTP-based leaderboard service"
    });
});

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c=>{  c.SwaggerEndpoint("/swagger/v1/swagger.json", "Leaderboard API v1");
        c.RoutePrefix = "swagger";});
}

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.Run();

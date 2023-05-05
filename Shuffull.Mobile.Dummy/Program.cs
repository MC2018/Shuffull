using Microsoft.EntityFrameworkCore;
using Shuffull.Shared;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<ShuffullContext>(options =>
{
    options.UseSqlite("temp_db.db3");
});

var app = builder.Build();

app.MapGet("/", () => "Hello World!");

app.Run();

using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Shuffull.Api.Tools.Authorization;
using Shuffull.Core.Authentication;
using Shuffull.Core.Behaviors;
using Shuffull.Core.Persistence;
using Shuffull.Core.Persistence.Repositories;

var builder = WebApplication.CreateBuilder(args);

// Thin JSON API: controllers only (no MVC views — those stay in Shuffull.Site for now).
builder.Services.AddControllers();

builder.Services.AddScoped<JwtHelper>();
// Lets Shuffull.Core's user slices issue tokens without referencing API/JWT types.
builder.Services.AddScoped<IAuthTokenGenerator, JwtAuthTokenGenerator>();

// The shared ShuffullContext lives in Shuffull.Core; the EF migrations remain in Shuffull.Site,
// which is the host that applies them. This API never runs migrations, so it does not need to know
// where they live.
builder.Services.AddDbContext<ShuffullContext>(options =>
{
    options.UseSqlServer(builder.Configuration.GetConnectionString("Shuffull"));
});

// Expose the concrete ShuffullContext as the base DbContext so Shuffull.Core's generic
// UnitOfWork/Repository (which depend only on DbContext) resolve the request-scoped context.
builder.Services.AddScoped<DbContext>(sp => sp.GetRequiredService<ShuffullContext>());
builder.Services.AddScoped<IUnitOfWork, UnitOfWork>();

// CQRS pipeline (mirrors the Sociallite backend): MediatR handlers + FluentValidation validators are
// discovered from Shuffull.Core, where every feature slice lives. The ValidationBehavior runs the
// validators before each handler and short-circuits to Result.Error on failure.
var coreAssembly = typeof(ValidationBehavior<,>).Assembly;
builder.Services.AddValidatorsFromAssemblies(new[] { coreAssembly });
builder.Services.AddMediatR(cfg =>
{
    cfg.RegisterServicesFromAssemblies(coreAssembly);
    cfg.AddOpenBehavior(typeof(ValidationBehavior<,>));
});

var app = builder.Build();

// Resolves the bearer token (if any) to HttpContext.Items["User"] for the [Authorize] filter.
app.UseMiddleware<JwtMiddleware>();

app.UseHttpsRedirection();
app.UseAuthorization();
app.MapControllers();

app.Run();

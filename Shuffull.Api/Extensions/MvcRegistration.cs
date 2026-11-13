using Shuffull.Api.Tools.Authorization;

namespace Shuffull.Api.Extensions;

/// <summary>
/// The API's MVC setup, in one place so the contract tests serialize with exactly what the API ships
/// (Shuffull.Core.Tests ApiContractGoldenFilesTest). Configure JSON here, never in Program.cs: a serializer change
/// is a wire change for every installed app and the running funnel, and the golden files should catch it.
/// The API tests start from here too, so <see cref="CurrentUserRequiresSignInConvention"/> runs in them as in prod.
/// </summary>
public static class MvcRegistration
{
    public static IMvcBuilder AddShuffullControllers(this IServiceCollection services) =>
        services.AddControllers(options => options.Conventions.Add(new CurrentUserRequiresSignInConvention()));
}

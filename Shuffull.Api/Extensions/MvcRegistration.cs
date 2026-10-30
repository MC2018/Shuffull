namespace Shuffull.Api.Extensions;

/// <summary>
/// The API's MVC setup, in one place so the contract tests serialize with exactly what the API ships
/// (Shuffull.Core.Tests ApiContractGoldenFilesTest). Configure JSON here, never in Program.cs: a serializer change
/// is a wire change for every installed app and the running funnel, and the golden files should catch it.
/// </summary>
public static class MvcRegistration
{
    public static IMvcBuilder AddShuffullControllers(this IServiceCollection services) => services.AddControllers();
}

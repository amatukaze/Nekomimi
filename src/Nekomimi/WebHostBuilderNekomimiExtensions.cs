using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace Sakuno.Nekomimi;

public static class WebHostBuilderNekomimiExtensions
{
    public static IWebHostBuilder UseNekomimi(this IWebHostBuilder builder) =>
        builder.UseKestrel(options =>
        {
            options.ConfigureEndpointDefaults(options =>
            {
                var middleware = ActivatorUtilities.GetServiceOrCreateInstance<PreprocessMiddleware>(options.ApplicationServices);

                options.Use(next => context => middleware.HandleAsync(context, next));
            });
        });
    public static IWebHostBuilder UseNekomimi(this IWebHostBuilder builder, Action<NekomimiOptions> options) =>
        builder.UseNekomimi().ConfigureNekomimi(options);
    public static IWebHostBuilder UseNekomimi(this IWebHostBuilder builder, Action<WebHostBuilderContext, NekomimiOptions> configureOptions) =>
        builder.UseNekomimi().ConfigureNekomimi(configureOptions);

    public static IWebHostBuilder ConfigureNekomimi(this IWebHostBuilder builder, Action<NekomimiOptions> options) =>
        builder.ConfigureServices(services => services.Configure(options));
    public static IWebHostBuilder ConfigureNekomimi(this IWebHostBuilder builder, Action<WebHostBuilderContext, NekomimiOptions> configureOptions) =>
        builder.ConfigureServices((context, services) => services.Configure<NekomimiOptions>(options => configureOptions(context, options)));
}

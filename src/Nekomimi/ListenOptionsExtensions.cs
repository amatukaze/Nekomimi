using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.AspNetCore.Server.Kestrel.Https;
using Microsoft.Extensions.DependencyInjection;

namespace Sakuno.Nekomimi;

public static class ListenOptionsExtensions
{
    public static void UseNekomimi(this ListenOptions options, Action<HttpsConnectionAdapterOptions>? httpsConfigure = null)
    {
        options.UsePreprocessMiddleware();

        if (httpsConfigure is not null)
            options.UseHttps(httpsConfigure);

        options.UseTunnelForwardMiddleware();
    }

    private static void UsePreprocessMiddleware(this ListenOptions options)
    {
        var middleware = ActivatorUtilities.GetServiceOrCreateInstance<PreprocessMiddleware>(options.ApplicationServices);

        options.Use(next => context => middleware.HandleAsync(context, next));
    }
    private static void UseTunnelForwardMiddleware(this ListenOptions options)
    {
        var middleware = ActivatorUtilities.GetServiceOrCreateInstance<TunnelForwardMiddleware>(options.ApplicationServices);

        options.Use(next => context => middleware.HandleAsync(context, next));
    }
}

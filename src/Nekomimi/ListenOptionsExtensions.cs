using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;

namespace Sakuno.Nekomimi;

public static class ListenOptionsExtensions
{
    public static void UseNekomimiPreprocessMiddleware(this ListenOptions options)
    {
        var middleware = ActivatorUtilities.GetServiceOrCreateInstance<PreprocessMiddleware>(options.ApplicationServices);

        options.Use(next => context => middleware.HandleAsync(context, next));
    }
    public static void UseNekomimiTunnelForwardMiddleware(this ListenOptions options)
    {
        var middleware = ActivatorUtilities.GetServiceOrCreateInstance<TunnelForwardMiddleware>(options.ApplicationServices);

        options.Use(next => context => middleware.HandleAsync(context, next));
    }
}

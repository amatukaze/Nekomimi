using Microsoft.AspNetCore.Connections.Features;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.AspNetCore.Server.Kestrel.Https;
using Microsoft.Extensions.DependencyInjection;

namespace Sakuno.Nekomimi;

public static class ListenOptionsExtensions
{
    public static void UseNekomimi(this ListenOptions options, Action<HttpsConnectionAdapterOptions>? httpsConfigure = null)
    {
        options.UseProxyHandshakeMiddleware();

        if (httpsConfigure is not null)
        {
            options.UseHttps(httpsConfigure);
            options.Use(next => async context =>
            {
                if (context.Features.Get<ITlsHandshakeFeature>() is not null && context.Features.Get<INekomimiContextFeature>() is { } nekomimiFeature)
                    nekomimiFeature.IsTunnel = false;

                await next(context);
            });
        }

        options.UseTunnelRelayMiddleware();
    }

    private static void UseProxyHandshakeMiddleware(this ListenOptions options)
    {
        var middleware = ActivatorUtilities.GetServiceOrCreateInstance<ProxyHandshakeMiddleware>(options.ApplicationServices);

        options.Use(next => context => middleware.HandleAsync(context, next));
    }
    private static void UseTunnelRelayMiddleware(this ListenOptions options)
    {
        var middleware = ActivatorUtilities.GetServiceOrCreateInstance<TunnelRelayMiddleware>(options.ApplicationServices);

        options.Use(next => context => middleware.HandleAsync(context, next));
    }
}

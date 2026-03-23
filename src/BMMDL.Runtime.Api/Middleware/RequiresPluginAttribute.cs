using BMMDL.Runtime.Plugins;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace BMMDL.Runtime.Api.Middleware;

/// <summary>
/// Marks a controller or action as requiring a specific plugin to be enabled.
/// When the plugin is disabled, all requests return 404 with a descriptive message.
/// Multiple attributes can be stacked — ALL referenced plugins must be enabled.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
public sealed class RequiresPluginAttribute : Attribute, IAsyncActionFilter
{
    public string PluginName { get; }

    public RequiresPluginAttribute(string pluginName)
    {
        PluginName = pluginName;
    }

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var pluginManager = context.HttpContext.RequestServices.GetRequiredService<IPluginManager>();

        if (!await pluginManager.IsPluginEnabledAsync(PluginName, context.HttpContext.RequestAborted))
        {
            context.Result = new NotFoundObjectResult(new
            {
                error = "PluginDisabled",
                message = $"The '{PluginName}' plugin is currently disabled.",
                plugin = PluginName,
            });
            return;
        }

        await next();
    }
}

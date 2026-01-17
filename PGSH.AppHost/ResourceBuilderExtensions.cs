using Microsoft.Extensions.Diagnostics.HealthChecks;
using System.Diagnostics;

namespace PGSH.AppHost
{
    internal static class ResourceBuilderExtensions
    {
        internal static IResourceBuilder<T> WithSwaggerUI<T>(this IResourceBuilder<T> builder)
        where T : IResourceWithEndpoints
        {
            return builder.WithOpenApiDocs("swagger-ui-docs", "Swagger API Documentation", "swagger");
        }
        internal static IResourceBuilder<T> WithScalarUI<T>(this IResourceBuilder<T> builder)
        where T : IResourceWithEndpoints
        {
            return builder.WithOpenApiDocs("scalar-docs", "Scaler API Documentation", "scalar/v1");
        }
        private static IResourceBuilder<T> WithOpenApiDocs<T>(this IResourceBuilder<T> builder,
            string name,
            string displayName,
            string openApiUIPath)
            where T : IResourceWithEndpoints
        {
            return builder.WithCommand(
                name,
                displayName,
                executeCommand: async _ =>
                {
                    try
                    {
                        var endpoint = builder.GetEndpoint("https");
                        var url = $"{endpoint.Url}/{openApiUIPath}";
                        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
                        return new ExecuteCommandResult { Success = true };
                    }
                    catch (Exception ex)
                    {
                        return new ExecuteCommandResult { Success = false, ErrorMessage = ex.ToString() };
                    }
                },
                updateState: context => context.ResourceSnapshot.HealthStatus == HealthStatus.Healthy
                             ? ResourceCommandState.Enabled : ResourceCommandState.Disabled
                ,
                iconName: "Document",
                iconVariant: IconVariant.Filled
                );
        }
    }
}

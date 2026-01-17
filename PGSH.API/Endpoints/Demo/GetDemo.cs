
namespace PGSH.API.Endpoints.Demo
{
    public sealed class GetDemo : IEndpoint
    {
        public record DemoData(string name);
        public void MapEndpoint(IEndpointRouteBuilder app)
        {
            app.MapGet("demo", () => Results.Ok(new DemoData("Ezzoubeir"))).WithTags("User");
        }
    }
}

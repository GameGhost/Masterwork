using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;

namespace Masterwork.App.Web.Api;

/// <summary>The content API, <c>/api/content/{path}</c>: a redirect to wherever <see cref="ContentRedirect"/> says the content is.</summary>
public static class ContentFunction
{
    [Function("content")]
    public static HttpResponseData Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", "head", Route = "content/{*path}")] HttpRequestData request,
        string? path)
    {
        var target = ContentRedirect.Target(path);
        var response = request.CreateResponse(target is null ? HttpStatusCode.NotFound : HttpStatusCode.Found);
        if (target is not null)
        {
            response.Headers.Add("Location", target);
        }

        // The redirect itself is never cached, so moving the content takes effect at once.
        response.Headers.Add("Cache-Control", "no-cache");
        return response;
    }
}

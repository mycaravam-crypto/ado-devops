namespace AdoCli.Tests;

using System.Net;
using System.Text;

/// <summary>Fake Azure DevOps Server: answers every request with a canned response and records what was sent.</summary>
sealed class StubHandler(HttpStatusCode status, string body = "{}") : HttpMessageHandler
{
    public List<(HttpMethod Method, string Url, string? Body, string? Auth, string? ContentType)> Requests { get; } = [];

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var content = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);
        Requests.Add((request.Method, request.RequestUri!.ToString(), content, request.Headers.Authorization?.ToString(), request.Content?.Headers.ContentType?.MediaType));
        return new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    }
}

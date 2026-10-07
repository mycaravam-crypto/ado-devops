namespace AdoCli.Tests;

using System.Net;
using System.Text;

/// <summary>Fake Azure DevOps Server: answers every request with a canned response and records what was sent.</summary>
sealed class StubHandler(HttpStatusCode status, string body = "{}") : HttpMessageHandler
{
    readonly Func<int, HttpResponseMessage>? _respond;

    /// <summary>Answers the n-th request (from 0) with <paramref name="respond"/>(n), e.g. one page of a list after another.</summary>
    public StubHandler(Func<int, HttpResponseMessage> respond) : this(HttpStatusCode.OK) => _respond = respond;

    /// <summary>A 200 JSON response, with an x-ms-continuationtoken header when <paramref name="continuation"/> is set.</summary>
    public static HttpResponseMessage Json(string body, string? continuation = null)
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        if (continuation is not null)
            response.Headers.Add("x-ms-continuationtoken", continuation);
        return response;
    }

    public List<(HttpMethod Method, string Url, string? Body, string? Auth)> Requests { get; } = [];

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var content = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);
        Requests.Add((request.Method, request.RequestUri!.ToString(), content, request.Headers.Authorization?.ToString()));
        if (_respond is not null)
            return _respond(Requests.Count - 1);
        return new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    }
}

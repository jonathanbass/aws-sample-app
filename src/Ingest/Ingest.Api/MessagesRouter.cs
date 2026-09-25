using System.Net;
using System.Text.Json;
using Amazon.Lambda.APIGatewayEvents;

namespace Ingest.Api;

/// <summary>
/// One Lambda serves every route on the messages resource. API Gateway sets
/// the method and the path on the request context; this dispatches on them.
/// </summary>
public sealed class MessagesRouter
{
    private readonly SubmitMessageFunction _submit;
    private readonly ListMessagesService _list;

    public MessagesRouter(SubmitMessageFunction submit, ListMessagesService list)
    {
        _submit = submit;
        _list = list;
    }

    public async Task<APIGatewayHttpApiV2ProxyResponse> HandleAsync(
        APIGatewayHttpApiV2ProxyRequest request)
    {
        string method = request.RequestContext.Http.Method;

        if (method == "POST")
        {
            return await _submit.HandleAsync(request);
        }

        IReadOnlyList<MessageResponse> messages = await _list.ListAsync(CancellationToken.None);

        return Ok(messages);
    }

    private static APIGatewayHttpApiV2ProxyResponse Ok(IReadOnlyList<MessageResponse> messages) =>
        new()
        {
            StatusCode = (int)HttpStatusCode.OK,
            Headers = new Dictionary<string, string> { ["Content-Type"] = "application/json" },

            // The same camelCase options the POST response uses. The SPA parses
            // this with Zod and a PascalCase body would fail every field.
            Body = JsonSerializer.Serialize(messages, SubmitMessageFunction.JsonOptions),
        };
}

using System.Net;
using System.Text.Json;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Amazon.Lambda.APIGatewayEvents;

namespace Ingest.Api;

public sealed class SubmitMessageFunction
{
    /// <summary>
    /// camelCase on the wire. Defaults would emit PascalCase, which silently
    /// mismatches both the SPA and any non-.NET consumer - a casing mismatch
    /// does not throw, it binds every field to its default.
    /// </summary>
    internal static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IAmazonDynamoDB _dynamoDb;
    private readonly string _tableName;
    private readonly TimeProvider _timeProvider;

    public SubmitMessageFunction(
        IAmazonDynamoDB dynamoDb,
        string tableName,
        TimeProvider timeProvider)
    {
        _dynamoDb = dynamoDb;
        _tableName = tableName;
        _timeProvider = timeProvider;
    }

    public async Task<APIGatewayHttpApiV2ProxyResponse> HandleAsync(
        APIGatewayHttpApiV2ProxyRequest request)
    {
        if (!TryParseBody(request.Body, out SubmitMessageRequest? submission))
        {
            return BadRequest("Request body must be a JSON object with a \"text\" property.");
        }

        string? text = submission?.Text;

        if (string.IsNullOrWhiteSpace(text))
        {
            return BadRequest("text is required and cannot be empty.");
        }

        DateTimeOffset submittedAt = _timeProvider.GetUtcNow();

        // Version 7 GUIDs are time-ordered, which keeps DynamoDB partition
        // keys roughly sequential instead of scattering them.
        Guid messageId = Guid.CreateVersion7(submittedAt);

        MessageItems items = MessageItems.Create(messageId, text, submittedAt);

        await WriteAtomicallyAsync(items);

        return Accepted(messageId);
    }

    /// <summary>
    /// A malformed body is a caller error, not a server fault. Letting the
    /// JsonException escape would surface at the API Gateway as a 500.
    /// </summary>
    private static bool TryParseBody(string? body, out SubmitMessageRequest? submission)
    {
        submission = null;

        try
        {
            submission = JsonSerializer.Deserialize<SubmitMessageRequest>(
                body ?? string.Empty, JsonOptions);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>
    /// The unit of work. The domain item and the outbox item are written in a
    /// single transaction, so an event can never exist without its message and
    /// a message can never be saved without its event being queued for relay.
    /// </summary>
    private Task WriteAtomicallyAsync(MessageItems items) =>
        _dynamoDb.TransactWriteItemsAsync(new TransactWriteItemsRequest
        {
            TransactItems =
            [
                new TransactWriteItem { Put = new Put { TableName = _tableName, Item = items.DomainItem } },
                new TransactWriteItem { Put = new Put { TableName = _tableName, Item = items.OutboxItem } },
            ],
        });

    private static APIGatewayHttpApiV2ProxyResponse BadRequest(string message) => new()
    {
        StatusCode = (int)HttpStatusCode.BadRequest,
        Headers = new Dictionary<string, string> { ["Content-Type"] = "application/json" },
        Body = JsonSerializer.Serialize(new ErrorResponse(message), JsonOptions),
    };

    private static APIGatewayHttpApiV2ProxyResponse Accepted(Guid messageId) => new()
    {
        StatusCode = (int)HttpStatusCode.Accepted,
        Headers = new Dictionary<string, string> { ["Content-Type"] = "application/json" },
        Body = JsonSerializer.Serialize(new SubmitMessageResponse(messageId), JsonOptions),
    };
}

public sealed record SubmitMessageRequest(string? Text);

public sealed record SubmitMessageResponse(Guid MessageId);

public sealed record ErrorResponse(string Message);

using Ingest.Storage;
using System.Net;
using System.Text.Json;
using Amazon.DynamoDBv2.Model;
using Amazon.Lambda.APIGatewayEvents;
using AwesomeAssertions;
using Microsoft.Extensions.Time.Testing;

namespace Ingest.Api.Tests;

/// <summary>
/// Handler contract tests: construct a real APIGatewayHttpApiV2ProxyRequest,
/// invoke the real entry point, assert the real response. This is the
/// production path - there is no ASP.NET pipeline in front of it (design D13).
/// </summary>
public class SubmitMessageFunctionTests : IClassFixture<DynamoDbFixture>
{
    private static readonly DateTimeOffset SubmittedAt =
        new(2026, 9, 17, 14, 30, 0, TimeSpan.Zero);

    private readonly DynamoDbFixture _dynamoDb;
    private readonly SubmitMessageFunction _function;

    public SubmitMessageFunctionTests(DynamoDbFixture dynamoDb)
    {
        _dynamoDb = dynamoDb;

        FakeTimeProvider timeProvider = new();
        timeProvider.SetUtcNow(SubmittedAt);

        _function = new SubmitMessageFunction(
            dynamoDb.Client,
            DynamoDbFixture.TableName,
            timeProvider);
    }

    private static APIGatewayHttpApiV2ProxyRequest RequestWithBody(string? body) =>
        new() { Body = body };

    [Fact]
    public async Task Valid_submission_is_accepted_and_returns_the_message_id()
    {
        APIGatewayHttpApiV2ProxyResponse response =
            await _function.HandleAsync(RequestWithBody("""{"text":"hello world"}"""));

        response.StatusCode.Should().Be((int)HttpStatusCode.Accepted);

        string messageId = JsonDocument.Parse(response.Body)
            .RootElement.GetProperty("messageId").GetString()!;

        messageId.Should().NotBeNullOrWhiteSpace();
        Guid.TryParse(messageId, out _).Should().BeTrue();
    }

    [Theory]
    [InlineData("text is empty", """{"text":""}""")]
    [InlineData("text is whitespace", """{"text":"   "}""")]
    [InlineData("text is null", """{"text":null}""")]
    [InlineData("text is absent", "{}")]
    public async Task Submission_without_usable_text_is_rejected(string scenario, string body)
    {
        APIGatewayHttpApiV2ProxyResponse response =
            await _function.HandleAsync(RequestWithBody(body));

        response.StatusCode.Should().Be((int)HttpStatusCode.BadRequest, scenario);
    }

    [Theory]
    [InlineData("body is not json", "this is not json")]
    [InlineData("body is empty", "")]
    [InlineData("body is absent", null)]
    [InlineData("body is a json array", "[1,2,3]")]
    public async Task Unparseable_body_is_rejected_rather_than_thrown(string scenario, string? body)
    {
        // A throw here becomes a 500 at the API Gateway. Malformed input from a
        // caller is a 400 - it is not a server fault.
        APIGatewayHttpApiV2ProxyResponse response =
            await _function.HandleAsync(RequestWithBody(body));

        response.StatusCode.Should().Be((int)HttpStatusCode.BadRequest, scenario);
    }

    [Fact]
    public async Task Valid_submission_persists_the_domain_item_and_the_outbox_item_together()
    {
        APIGatewayHttpApiV2ProxyResponse response =
            await _function.HandleAsync(RequestWithBody("""{"text":"persisted text"}"""));

        string messageId = JsonDocument.Parse(response.Body)
            .RootElement.GetProperty("messageId").GetString()!;

        QueryResponse stored = await _dynamoDb.Client.QueryAsync(new QueryRequest
        {
            TableName = DynamoDbFixture.TableName,
            KeyConditionExpression = "pk = :pk",
            ExpressionAttributeValues = new Dictionary<string, AttributeValue>
            {
                [":pk"] = new($"MESSAGE#{messageId}"),
            },
        });

        // Both items, or neither - that is the whole point of the transaction.
        stored.Items.Should().HaveCount(2);
        stored.Items.Select(item => item[MessageItems.SortKey].S)
            .Should().BeEquivalentTo([MessageItems.DomainSortKey, MessageItems.OutboxSortKey]);
    }
}

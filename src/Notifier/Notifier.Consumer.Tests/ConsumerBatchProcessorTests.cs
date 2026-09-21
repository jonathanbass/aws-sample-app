using System.Text;
using System.Text.Json;
using Amazon.ApiGatewayManagementApi;
using Amazon.ApiGatewayManagementApi.Model;
using Amazon.Lambda.SQSEvents;
using AwesomeAssertions;
using Contracts;
using Microsoft.Extensions.Time.Testing;
using Notifier.Consumer;
using Notifier.Storage;
using Notifier.TestSupport;
using NSubstitute;

namespace Notifier.Consumer.Tests;

public class ConsumerBatchProcessorTests : IClassFixture<ConnectionsTableFixture>, IAsyncLifetime
{
    private static readonly DateTimeOffset SubmittedAt =
        new(2026, 9, 21, 9, 0, 0, TimeSpan.Zero);

    private readonly ConnectionRegistry _registry;
    private readonly IAmazonApiGatewayManagementApi _socketApi =
        Substitute.For<IAmazonApiGatewayManagementApi>();

    private readonly ConsumerBatchProcessor _processor;

    public ConsumerBatchProcessorTests(ConnectionsTableFixture table)
    {
        FakeTimeProvider timeProvider = new();
        timeProvider.SetUtcNow(SubmittedAt);

        _registry = new ConnectionRegistry(
            table.Client,
            ConnectionsTableFixture.TableName,
            timeProvider);

        _table = table;
        _processor = new ConsumerBatchProcessor(new BroadcastService(_socketApi, _registry));
    }

    private static SQSEvent.SQSMessage Message(string messageId, string body) =>
        new() { MessageId = messageId, Body = body };

    private List<string> PostedBodies() =>
        _socketApi.ReceivedCalls()
            .Where(call => call.GetMethodInfo().Name
                == nameof(IAmazonApiGatewayManagementApi.PostToConnectionAsync))
            .Select(call => (PostToConnectionRequest)call.GetArguments()[0]!)
            .Select(request => Encoding.UTF8.GetString(request.Data.ToArray()))
            .ToList();

    [Fact]
    public async Task ProcessAsync_broadcasts_the_event_in_the_message_body()
    {
        await _registry.AddAsync("batch-socket", CancellationToken.None);

        TextSubmitted submitted = new(Guid.NewGuid(), "from the queue", SubmittedAt);

        SQSEvent batch = new()
        {
            Records = [Message("sqs-1", JsonSerializer.Serialize(submitted, EventJson.Options))],
        };

        await _processor.ProcessAsync(batch, CancellationToken.None);

        PostedBodies().Should().ContainSingle()
            .Which.Should().Contain("from the queue");
    }

    [Fact]
    public async Task ProcessAsync_reports_a_bad_message_and_still_delivers_the_good_one()
    {
        await _registry.AddAsync("mixed-socket", CancellationToken.None);

        TextSubmitted good = new(Guid.NewGuid(), "good message", SubmittedAt);

        SQSEvent batch = new()
        {
            Records =
            [
                Message("sqs-bad", "this is not json"),
                Message("sqs-good", JsonSerializer.Serialize(good, EventJson.Options)),
            ],
        };

        // A throw here makes SQS retry the WHOLE batch, so the good message is
        // delivered again on every retry and the browser shows it three times
        // before the batch dead-letters. Reporting only the bad id keeps the
        // good message delivered once and sends just the poison one to the
        // dead-letter queue after three receives.
        SQSBatchResponse response = await _processor.ProcessAsync(batch, CancellationToken.None);

        response.BatchItemFailures.Should().ContainSingle()
            .Which.ItemIdentifier.Should().Be("sqs-bad");

        PostedBodies().Should().ContainSingle()
            .Which.Should().Contain("good message");
    }

    private readonly ConnectionsTableFixture _table;

    // Rows survive between tests in a class fixture. Clear before each test,
    // or a broadcast reaches the previous test's connections.
    public Task InitializeAsync() => _table.ClearAsync();

    public Task DisposeAsync() => Task.CompletedTask;
}

using System.Text;
using System.Text.Json;
using Amazon.ApiGatewayManagementApi;
using Amazon.ApiGatewayManagementApi.Model;
using AwesomeAssertions;
using Contracts;
using Microsoft.Extensions.Time.Testing;
using Notifier.Consumer;
using Notifier.Storage;
using Notifier.TestSupport;
using NSubstitute;

namespace Notifier.Consumer.Tests;

public class BroadcastServiceTests : IClassFixture<ConnectionsTableFixture>, IAsyncLifetime
{
    private static readonly DateTimeOffset SubmittedAt =
        new(2026, 9, 21, 9, 0, 0, TimeSpan.Zero);

    private readonly ConnectionRegistry _registry;
    private readonly IAmazonApiGatewayManagementApi _socketApi =
        Substitute.For<IAmazonApiGatewayManagementApi>();

    private readonly BroadcastService _service;

    public BroadcastServiceTests(ConnectionsTableFixture table)
    {
        FakeTimeProvider timeProvider = new();
        timeProvider.SetUtcNow(SubmittedAt);

        // The registry is OURS, so the tests use the real one against DynamoDB
        // Local. Only the API Gateway client is substituted: it is a system
        // boundary and there is no local emulator for it.
        _registry = new ConnectionRegistry(
            table.Client,
            ConnectionsTableFixture.TableName,
            timeProvider);

        _table = table;
        _service = new BroadcastService(_socketApi, _registry);
    }

    private List<PostToConnectionRequest> PostedRequests() =>
        _socketApi.ReceivedCalls()
            .Where(call => call.GetMethodInfo().Name
                == nameof(IAmazonApiGatewayManagementApi.PostToConnectionAsync))
            .Select(call => (PostToConnectionRequest)call.GetArguments()[0]!)
            .ToList();

    [Fact]
    public async Task BroadcastAsync_pushes_the_event_to_an_open_connection()
    {
        await _registry.AddAsync("open-socket", CancellationToken.None);

        TextSubmitted submitted = new(Guid.NewGuid(), "hello browser", SubmittedAt);

        await _service.BroadcastAsync(submitted, CancellationToken.None);

        PostToConnectionRequest posted = PostedRequests()
            .Single(request => request.ConnectionId == "open-socket");

        string body = Encoding.UTF8.GetString(posted.Data.ToArray());

        // The SPA reads this payload, so it must be the camelCase contract and
        // it must carry the id (design D15), not the text alone.
        TextSubmitted? received = JsonSerializer.Deserialize<TextSubmitted>(body, EventJson.Options);

        received!.MessageId.Should().Be(submitted.MessageId);
        received.Text.Should().Be("hello browser");
    }

    [Fact]
    public async Task BroadcastAsync_keeps_going_when_one_connection_is_gone()
    {
        await _registry.AddAsync("dead-socket", CancellationToken.None);
        await _registry.AddAsync("live-socket", CancellationToken.None);

        _socketApi
            .PostToConnectionAsync(
                Arg.Is<PostToConnectionRequest>(request => request.ConnectionId == "dead-socket"),
                Arg.Any<CancellationToken>())
            .Returns<Task<PostToConnectionResponse>>(_ => throw new GoneException("socket closed"));

        // A browser that closed without a clean $disconnect leaves a dead row.
        // One dead connection must not stop the live ones receiving the event.
        await _service.BroadcastAsync(
            new TextSubmitted(Guid.NewGuid(), "survives", SubmittedAt),
            CancellationToken.None);

        PostedRequests().Select(request => request.ConnectionId)
            .Should().Contain("live-socket");
    }

    [Fact]
    public async Task BroadcastAsync_deletes_a_connection_that_is_gone()
    {
        await _registry.AddAsync("stale-socket", CancellationToken.None);

        _socketApi
            .PostToConnectionAsync(
                Arg.Is<PostToConnectionRequest>(request => request.ConnectionId == "stale-socket"),
                Arg.Any<CancellationToken>())
            .Returns<Task<PostToConnectionResponse>>(_ => throw new GoneException("socket closed"));

        await _service.BroadcastAsync(
            new TextSubmitted(Guid.NewGuid(), "cleanup", SubmittedAt),
            CancellationToken.None);

        // Without the delete, every later broadcast retries this dead socket
        // until the two hour TTL removes it.
        IReadOnlyList<string> remaining = await _registry.ListAsync(CancellationToken.None);

        remaining.Should().NotContain("stale-socket");
    }

    private readonly ConnectionsTableFixture _table;

    // Rows survive between tests in a class fixture. Clear before each test,
    // or a broadcast reaches the previous test's connections.
    public Task InitializeAsync() => _table.ClearAsync();

    public Task DisposeAsync() => Task.CompletedTask;
}

using Amazon.DynamoDBv2.Model;
using AwesomeAssertions;
using Ingest.Api;
using Ingest.Storage;

namespace Ingest.Api.Tests;

public class ListMessagesServiceTests : IClassFixture<DynamoDbFixture>, IAsyncLifetime
{
    private static readonly DateTimeOffset Midday =
        new(2026, 9, 24, 12, 0, 0, TimeSpan.Zero);

    private readonly DynamoDbFixture _table;
    private readonly ListMessagesService _service;

    public ListMessagesServiceTests(DynamoDbFixture table)
    {
        _table = table;
        _service = new ListMessagesService(table.Client, DynamoDbFixture.TableName);
    }

    // The table is shared by every test in the class. A scan would otherwise
    // return rows written by an earlier test and the counts would be wrong.
    public Task InitializeAsync() => _table.ClearAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    private Task StoreAsync(string text, DateTimeOffset submittedAt) =>
        _table.Client.TransactWriteItemsAsync(new TransactWriteItemsRequest
        {
            TransactItems = MessageItems.Create(Guid.CreateVersion7(submittedAt), text, submittedAt)
                .Both
                .Select(item => new TransactWriteItem
                {
                    Put = new Put { TableName = DynamoDbFixture.TableName, Item = item },
                })
                .ToList(),
        });

    [Fact]
    public async Task ListAsync_returns_a_stored_message()
    {
        await StoreAsync("stored message", Midday);

        IReadOnlyList<MessageResponse> messages = await _service.ListAsync(CancellationToken.None);

        messages.Should().ContainSingle()
            .Which.Text.Should().Be("stored message");
    }

    [Fact]
    public async Task ListAsync_returns_each_message_once()
    {
        await StoreAsync("first", Midday);
        await StoreAsync("second", Midday.AddMinutes(1));

        IReadOnlyList<MessageResponse> messages = await _service.ListAsync(CancellationToken.None);

        // One TransactWriteItems call writes a MESSAGE row AND an OUTBOX row
        // for every message. Without the sort key filter the scan returns both
        // and every message appears twice.
        messages.Should().HaveCount(2);
    }

    [Fact]
    public async Task ListAsync_returns_the_oldest_message_first()
    {
        await StoreAsync("newest", Midday.AddMinutes(5));
        await StoreAsync("oldest", Midday);

        IReadOnlyList<MessageResponse> messages = await _service.ListAsync(CancellationToken.None);

        // A scan returns rows in no useful order. The SPA appends new messages
        // to the end of the list, so history must arrive oldest first or the
        // two halves of the list disagree.
        messages.Select(message => message.Text).Should().Equal("oldest", "newest");
    }
}

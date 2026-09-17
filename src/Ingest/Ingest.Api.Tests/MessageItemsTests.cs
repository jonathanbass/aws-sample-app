using AwesomeAssertions;
using Ingest.Api;

namespace Ingest.Api.Tests;

public class MessageItemsTests
{
    private static readonly DateTimeOffset SubmittedAt =
        new(2026, 9, 17, 14, 30, 0, TimeSpan.Zero);

    private static readonly Guid MessageId =
        Guid.Parse("0199c0de-1111-4222-8333-444455556666");

    [Fact]
    public void Create_writes_the_domain_item_and_the_outbox_item_under_one_partition_key()
    {
        MessageItems items = MessageItems.Create(MessageId, "hello world", SubmittedAt);

        // AttributeValue has no value equality - compare the string payload.
        items.DomainItem[MessageItems.PartitionKey].S
            .Should().Be(items.OutboxItem[MessageItems.PartitionKey].S)
            .And.Contain(MessageId.ToString());
    }

    [Fact]
    public void Create_gives_the_two_items_different_sort_keys()
    {
        MessageItems items = MessageItems.Create(MessageId, "hello world", SubmittedAt);

        // Same partition key with the same sort key would make TransactWriteItems
        // fail: a transaction cannot write the same key twice.
        items.DomainItem[MessageItems.SortKey].S
            .Should().NotBe(items.OutboxItem[MessageItems.SortKey].S);
    }

    [Fact]
    public void Create_puts_the_text_and_submitted_time_on_the_outbox_item()
    {
        MessageItems items = MessageItems.Create(MessageId, "hello world", SubmittedAt);

        // The relay Lambda builds the integration event from the outbox item
        // alone - it never reads the domain item - so everything the event
        // needs must be here.
        items.OutboxItem[MessageItems.TextAttribute].S.Should().Be("hello world");
        items.OutboxItem[MessageItems.MessageIdAttribute].S.Should().Be(MessageId.ToString());
        items.OutboxItem[MessageItems.SubmittedAtAttribute].S
            .Should().Be("2026-09-17T14:30:00.0000000+00:00");
    }

    [Fact]
    public void Create_stores_the_message_itself_on_the_domain_item()
    {
        MessageItems items = MessageItems.Create(MessageId, "hello world", SubmittedAt);

        // The domain item is the persisted message. Without a payload the
        // "domain write and event write in one transaction" demo is vacuous.
        items.DomainItem[MessageItems.TextAttribute].S.Should().Be("hello world");
        items.DomainItem[MessageItems.SubmittedAtAttribute].S
            .Should().Be("2026-09-17T14:30:00.0000000+00:00");
    }
}

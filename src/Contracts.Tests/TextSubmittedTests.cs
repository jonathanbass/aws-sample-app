using System.Text.Json;
using AwesomeAssertions;
using Contracts;

namespace Contracts.Tests;

public class TextSubmittedTests
{
    private static readonly DateTimeOffset SubmittedAt =
        new(2026, 9, 17, 14, 30, 0, TimeSpan.Zero);

    private static readonly Guid MessageId =
        Guid.Parse("0199c0de-1111-4222-8333-444455556666");

    [Fact]
    public void TextSubmitted_round_trips_through_the_shared_serializer_options()
    {
        TextSubmitted original = new(MessageId, "hello world", SubmittedAt);

        string json = JsonSerializer.Serialize(original, EventJson.Options);
        TextSubmitted? restored = JsonSerializer.Deserialize<TextSubmitted>(json, EventJson.Options);

        // Assert every field on its own. A casing mismatch does not throw - it
        // binds each field to its default. An assertion that the object is
        // not null would pass against a completely broken contract.
        restored.Should().NotBeNull();
        restored!.MessageId.Should().Be(MessageId);
        restored.Text.Should().Be("hello world");
        restored.SubmittedAt.Should().Be(SubmittedAt);
    }

    [Fact]
    public void TextSubmitted_writes_camel_case_keys_on_the_wire()
    {
        TextSubmitted original = new(MessageId, "hello world", SubmittedAt);

        string json = JsonSerializer.Serialize(original, EventJson.Options);

        // The SPA reads this payload. It is not .NET, so the key casing is a
        // contract in its own right. The round-trip test above passes even if
        // both ends agree on PascalCase, so it cannot catch this.
        json.Should().Contain("\"messageId\"").And.NotContain("\"MessageId\"");
        json.Should().Contain("\"text\"").And.NotContain("\"Text\"");
        json.Should().Contain("\"submittedAt\"").And.NotContain("\"SubmittedAt\"");
    }
}

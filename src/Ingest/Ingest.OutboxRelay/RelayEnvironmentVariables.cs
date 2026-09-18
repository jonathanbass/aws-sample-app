namespace Ingest.OutboxRelay;

/// <summary>
/// Names must match the environment variables set on the Lambda in
/// infra/main/messaging.tf. A mismatch fails at startup, not at invoke time.
/// </summary>
public static class RelayEnvironmentVariables
{
    public const string OutboxQueueUrl = "OUTBOX_QUEUE_URL";
}

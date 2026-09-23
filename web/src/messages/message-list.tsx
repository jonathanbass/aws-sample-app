import type { Message } from "./message"

type MessageListProps = {
  messages: Message[]
}

export function MessageList({ messages }: MessageListProps) {
  if (messages.length === 0) {
    return (
      <p className="text-muted-foreground text-sm">
        No messages yet. Send one to see it come back.
      </p>
    )
  }

  return (
    <ul className="flex flex-col gap-2">
      {messages.map((message) => (
        /*
          Keyed by messageId, never by array index (design D15). An index key
          makes React reuse the wrong row when a message is later removed, so
          the text of one row appears against another row's identity.
        */
        <li
          key={message.messageId}
          className="bg-card border-border rounded-md border px-3 py-2 text-sm"
        >
          {message.text}
        </li>
      ))}
    </ul>
  )
}

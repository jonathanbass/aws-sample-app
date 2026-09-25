import { useState } from "react"
import { config } from "@/config"
import { MessageList } from "@/messages/message-list"
import { SubmitForm } from "@/messages/submit-form"
import { submitMessage } from "@/messages/submit-message"
import { useMessages } from "@/messages/use-messages"

export function App() {
  const { messages, historyFailed } = useMessages(config.apiUrl, config.webSocketUrl)
  const [error, setError] = useState<string | null>(null)

  const handleSubmit = async (text: string) => {
    setError(null)

    try {
      await submitMessage(config.apiUrl, text)
    } catch {
      // The message never reaches the list on a failure, so without this the
      // page looks like it simply lost the message.
      setError("The message could not be sent. Try again.")
    }
  }

  return (
    <main className="mx-auto flex min-h-dvh max-w-xl flex-col gap-6 px-4 py-10">
      <header className="flex flex-col gap-1">
        <h1 className="text-2xl font-semibold tracking-tight">Messages</h1>
        <p className="text-muted-foreground text-sm">
          Send a message. It travels through a queue and returns over a WebSocket.
        </p>
      </header>

      <SubmitForm onSubmit={handleSubmit} />

      {error !== null && (
        <p role="alert" className="text-destructive text-sm">
          {error}
        </p>
      )}

      {historyFailed && (
        <p role="status" className="text-muted-foreground text-sm">
          Earlier messages could not be loaded. New messages will still appear.
        </p>
      )}

      <MessageList messages={messages} />
    </main>
  )
}

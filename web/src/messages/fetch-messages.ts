import { z } from "zod"
import { messageSchema, type Message } from "./message"

const messageListSchema = z.array(messageSchema)

/**
 * Reads every stored message, oldest first.
 *
 * The SPA fills its list from two sources: this on load, and the WebSocket
 * afterwards. Without this the page is empty on every refresh, even though
 * the messages are stored permanently.
 */
export async function fetchMessages(apiUrl: string): Promise<Message[]> {
  const response = await fetch(`${apiUrl}/messages`)

  // fetch only rejects on a network fault. Without this check a 500 body
  // reaches Zod, and the page reports a shape error for what is really a
  // server failure.
  if (!response.ok) {
    throw new Error(`Failed to load the message history: ${response.status}`)
  }

  // An API response is untrusted input, so it is parsed, never cast. A cast
  // would let a changed backend shape through and fail later, in a render.
  return messageListSchema.parse(await response.json())
}

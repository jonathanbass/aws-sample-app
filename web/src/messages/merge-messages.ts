import type { Message } from "./message"

/**
 * Joins the stored history to the messages the socket has pushed.
 *
 * A message submitted while the history request is in flight is stored before
 * the response is built AND pushed over the socket, so it arrives by both
 * routes. Deduplicating by id is why the id is on every payload (design D15).
 *
 * History comes first because the backend returns it oldest-first and the
 * socket appends as messages arrive. Order is preserved, not recomputed.
 */
export function mergeMessages(history: Message[], live: Message[]): Message[] {
  const seen = new Set<string>()

  return [...history, ...live].filter((message) => {
    if (seen.has(message.messageId)) {
      return false
    }

    seen.add(message.messageId)

    return true
  })
}

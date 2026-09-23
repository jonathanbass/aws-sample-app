import { z } from "zod"

/*
  A WebSocket frame is untrusted input, so it is parsed, never cast. Casting it
  with `as Message` would let a malformed frame through and fail later, in a
  render, where the cause is much harder to see.
*/
const messageSchema = z.object({
  messageId: z.string(),
  text: z.string(),
  submittedAt: z.string(),
})

export type Message = z.infer<typeof messageSchema>

export function parseMessage(frame: string): Message | null {
  // JSON.parse throws on a frame that is not JSON, before Zod ever sees it.
  // safeParse alone is not enough.
  let raw: unknown

  try {
    raw = JSON.parse(frame)
  } catch {
    return null
  }

  const parsed = messageSchema.safeParse(raw)

  return parsed.success ? parsed.data : null
}

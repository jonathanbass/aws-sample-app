import { describe, expect, it } from "vitest"
import type { Message } from "./message"
import { mergeMessages } from "./merge-messages"

function messageWith(text: string, messageId: string): Message {
  return { messageId, text, submittedAt: "2026-09-24T12:00:00+00:00" }
}

describe("mergeMessages", () => {
  it("keeps one copy of a message that arrived by both routes", () => {
    const history = [messageWith("first", "id-one"), messageWith("second", "id-two")]

    // A message submitted while the history request is in flight is stored
    // before the response is built AND pushed over the socket. Without the
    // merge it appears twice, which is why the id is on every payload (D15).
    const live = [messageWith("second", "id-two"), messageWith("third", "id-three")]

    expect(mergeMessages(history, live).map((message) => message.text)).toEqual([
      "first",
      "second",
      "third",
    ])
  })
})

import { describe, expect, it } from "vitest"
import { parseMessage } from "./message"

describe("parseMessage", () => {
  const validFrame = JSON.stringify({
    messageId: "0199c0de-1111-4222-8333-444455556666",
    text: "hello browser",
    submittedAt: "2026-09-21T09:00:00+00:00",
  })

  it("reads a message the backend sent", () => {
    const parsed = parseMessage(validFrame)

    expect(parsed).toEqual({
      messageId: "0199c0de-1111-4222-8333-444455556666",
      text: "hello browser",
      submittedAt: "2026-09-21T09:00:00+00:00",
    })
  })

  // A bad frame must never reach the list. A throw here happens inside the
  // socket's onmessage handler, where nothing catches it.
  it.each([
    { scenario: "not json at all", frame: "this is not json" },
    { scenario: "an empty frame", frame: "" },
    { scenario: "a json array", frame: "[1,2,3]" },
    { scenario: "an object missing text", frame: '{"messageId":"a","submittedAt":"b"}' },
    { scenario: "text of the wrong type", frame: '{"messageId":"a","text":7,"submittedAt":"b"}' },
  ])("returns null for $scenario", ({ frame }) => {
    expect(parseMessage(frame)).toBeNull()
  })
})

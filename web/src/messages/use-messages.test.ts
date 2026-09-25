import { act, renderHook, waitFor } from "@testing-library/react"
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest"
import { FakeWebSocket } from "./fake-web-socket"
import { useMessages } from "./use-messages"

const apiUrl = "https://api.example.test"
const socketUrl = "wss://example.test/live"

function messageBody(text: string, messageId: string) {
  return { messageId, text, submittedAt: "2026-09-24T12:00:00+00:00" }
}

describe("useMessages", () => {
  beforeEach(() => {
    FakeWebSocket.reset()
    vi.stubGlobal("WebSocket", FakeWebSocket)
  })

  afterEach(() => {
    vi.unstubAllGlobals()
    vi.restoreAllMocks()
  })

  it("shows the stored history before anything arrives on the socket", async () => {
    vi.spyOn(globalThis, "fetch").mockResolvedValue(
      new Response(JSON.stringify([messageBody("stored earlier", "id-one")]), { status: 200 }),
    )

    const { result } = renderHook(() => useMessages(apiUrl, socketUrl))

    // Without the history fetch the list is empty on every refresh, even
    // though every message is stored permanently.
    await waitFor(() => {
      expect(result.current.messages.map((message) => message.text)).toEqual(["stored earlier"])
    })
  })

  it("reports that the history could not be loaded", async () => {
    vi.spyOn(globalThis, "fetch").mockResolvedValue(new Response("", { status: 500 }))

    const { result } = renderHook(() => useMessages(apiUrl, socketUrl))

    // The socket still works, so the page stays useful. Without a flag the
    // page shows an empty list and implies there is no history, rather than
    // saying it could not be read.
    await waitFor(() => {
      expect(result.current.historyFailed).toBe(true)
    })
  })

  it("appends a socket message after the history", async () => {
    vi.spyOn(globalThis, "fetch").mockResolvedValue(
      new Response(JSON.stringify([messageBody("stored earlier", "id-one")]), { status: 200 }),
    )

    const { result } = renderHook(() => useMessages(apiUrl, socketUrl))

    await waitFor(() => {
      expect(result.current.messages).toHaveLength(1)
    })

    act(() => {
      FakeWebSocket.latest.receive(JSON.stringify(messageBody("just arrived", "id-two")))
    })

    // Proves the hook joins the two sources. Either one alone would show a
    // single message and look correct.
    await waitFor(() => {
      expect(result.current.messages.map((message) => message.text)).toEqual([
        "stored earlier",
        "just arrived",
      ])
    })
  })
})

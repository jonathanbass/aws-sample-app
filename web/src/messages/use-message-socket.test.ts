import { act, renderHook } from "@testing-library/react"
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest"
import { FakeWebSocket } from "./fake-web-socket"
import { useMessageSocket } from "./use-message-socket"

const socketUrl = "wss://example.test/live"

function frameFor(text: string, messageId = crypto.randomUUID()): string {
  return JSON.stringify({
    messageId,
    text,
    submittedAt: "2026-09-21T09:00:00+00:00",
  })
}

describe("useMessageSocket", () => {
  beforeEach(() => {
    FakeWebSocket.reset()
    vi.stubGlobal("WebSocket", FakeWebSocket)
  })

  afterEach(() => {
    vi.unstubAllGlobals()
  })

  it("adds a received message to the list", () => {
    const { result } = renderHook(() => useMessageSocket(socketUrl))

    act(() => {
      FakeWebSocket.latest.receive(frameFor("hello browser"))
    })

    expect(result.current.map((message) => message.text)).toEqual(["hello browser"])
  })

  it("closes the socket when the component unmounts", () => {
    const { unmount } = renderHook(() => useMessageSocket(socketUrl))
    const socket = FakeWebSocket.latest

    unmount()

    // Without this, every remount leaks an open connection. The backend then
    // keeps a dead connection id and broadcasts to it until the TTL expires.
    expect(socket.close).toHaveBeenCalled()
  })

  it("opens a new socket after the connection drops", () => {
    vi.useFakeTimers()

    renderHook(() => useMessageSocket(socketUrl))
    expect(FakeWebSocket.instances).toHaveLength(1)

    // API Gateway closes an idle WebSocket after ten minutes, and closes every
    // connection after two hours. Without a reconnect the page stops receiving
    // and gives the user no sign that anything is wrong.
    act(() => {
      FakeWebSocket.latest.dropConnection()
    })

    act(() => {
      vi.advanceTimersByTime(5_000)
    })

    expect(FakeWebSocket.instances.length).toBeGreaterThan(1)

    vi.useRealTimers()
  })

  it("does not reconnect after the component unmounts", () => {
    vi.useFakeTimers()

    const { unmount } = renderHook(() => useMessageSocket(socketUrl))
    const socket = FakeWebSocket.latest

    unmount()

    // close() fires onclose, which is the same path a real drop takes. Without
    // a guard, every unmount schedules a reconnect and the page reopens a
    // socket that nothing will ever close.
    act(() => {
      socket.dropConnection()
      vi.advanceTimersByTime(10_000)
    })

    expect(FakeWebSocket.instances).toHaveLength(1)

    vi.useRealTimers()
  })
})

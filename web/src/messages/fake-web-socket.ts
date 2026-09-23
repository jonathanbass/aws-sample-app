import { vi } from "vitest"

/*
  A stand-in for the browser WebSocket. jsdom has no WebSocket, and a real one
  would make the tests depend on a deployed API.

  It records every instance, so a test can assert that the hook closed the old
  socket and opened a new one.
*/
export class FakeWebSocket {
  static instances: FakeWebSocket[] = []

  static reset(): void {
    FakeWebSocket.instances = []
  }

  static get latest(): FakeWebSocket {
    const latest = FakeWebSocket.instances.at(-1)

    if (latest === undefined) {
      throw new Error("No socket was opened.")
    }

    return latest
  }

  onmessage: ((event: { data: string }) => void) | null = null
  onclose: (() => void) | null = null
  onerror: (() => void) | null = null

  readonly close = vi.fn()

  // Declared as a field rather than a constructor parameter property. The
  // tsconfig sets erasableSyntaxOnly, which forbids parameter properties
  // because they emit code rather than being erased.
  readonly url: string

  constructor(url: string) {
    this.url = url

    FakeWebSocket.instances.push(this)
  }

  /** Simulates a frame arriving from the server. */
  receive(data: string): void {
    this.onmessage?.({ data })
  }

  /** Simulates the server or the network dropping the connection. */
  dropConnection(): void {
    this.onclose?.()
  }
}

import { useEffect, useState } from "react"
import { parseMessage, type Message } from "./message"

/** How long to wait before a reconnect attempt. */
const reconnectDelayMilliseconds = 2_000

/**
 * Holds one WebSocket open and collects the messages the backend pushes.
 *
 * The socket reopens after a drop. API Gateway closes an idle connection after
 * ten minutes and every connection after two hours, so a page left open will
 * be dropped. Without the reopen it stops receiving and shows the user nothing
 * to explain why.
 */
export function useMessageSocket(url: string): Message[] {
  const [messages, setMessages] = useState<Message[]>([])

  useEffect(() => {
    let socket: WebSocket | null = null
    let reconnectTimer: ReturnType<typeof setTimeout> | null = null

    // Set when the effect is torn down. It stops a pending reconnect from
    // opening a socket that nothing will ever close.
    let stopped = false

    const connect = () => {
      socket = new WebSocket(url)

      socket.onmessage = (event: MessageEvent<string>) => {
        const message = parseMessage(event.data)

        if (message === null) {
          return
        }

        setMessages((current) => [...current, message])
      }

      socket.onclose = () => {
        if (stopped) {
          return
        }

        reconnectTimer = setTimeout(connect, reconnectDelayMilliseconds)
      }
    }

    connect()

    return () => {
      stopped = true

      if (reconnectTimer !== null) {
        clearTimeout(reconnectTimer)
      }

      // Closing fires onclose, which is why `stopped` is checked there. Without
      // it, every unmount would schedule a reconnect.
      socket?.close()
    }
  }, [url])

  return messages
}

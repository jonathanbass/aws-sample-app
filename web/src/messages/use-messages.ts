import { useEffect, useState } from "react"
import { fetchMessages } from "./fetch-messages"
import { mergeMessages } from "./merge-messages"
import type { Message } from "./message"
import { useMessageSocket } from "./use-message-socket"

export type MessagesState = {
  messages: Message[]
  historyFailed: boolean
}

/**
 * The list the page shows: the stored history, then whatever the socket pushes.
 *
 * The two sources are merged rather than concatenated. A message submitted
 * while the history request is in flight arrives by both routes.
 */
export function useMessages(apiUrl: string, webSocketUrl: string): MessagesState {
  const [history, setHistory] = useState<Message[]>([])
  const [historyFailed, setHistoryFailed] = useState(false)

  const live = useMessageSocket(webSocketUrl)

  useEffect(() => {
    // Set when the effect is torn down, so a response that arrives after
    // unmount does not set state on a component that has gone.
    let stopped = false

    fetchMessages(apiUrl)
      .then((stored) => {
        if (!stopped) {
          setHistory(stored)
        }
      })
      .catch(() => {
        if (!stopped) {
          // The socket still works, so the page stays useful. The flag lets
          // the page say the history is missing rather than show an empty
          // list and imply there is none.
          setHistoryFailed(true)
        }
      })

    return () => {
      stopped = true
    }
  }, [apiUrl])

  return { messages: mergeMessages(history, live), historyFailed }
}

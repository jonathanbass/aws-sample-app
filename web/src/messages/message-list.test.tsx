import { render, screen } from "@testing-library/react"
import { describe, expect, it } from "vitest"
import type { Message } from "./message"
import { MessageList } from "./message-list"

function messageWith(text: string, messageId: string): Message {
  return { messageId, text, submittedAt: "2026-09-21T09:00:00+00:00" }
}

describe("MessageList", () => {
  it("shows every message in the order they arrived", () => {
    const messages = [
      messageWith("first", "id-one"),
      messageWith("second", "id-two"),
    ]

    render(<MessageList messages={messages} />)

    // The queue is FIFO with one message group, so arrival order is
    // submission order. The list must not reorder them.
    expect(screen.getAllByRole("listitem").map((item) => item.textContent)).toEqual([
      "first",
      "second",
    ])
  })

  it("tells the user what to do when there are no messages", () => {
    render(<MessageList messages={[]} />)

    // An empty page gives no sign whether the app works or is broken.
    expect(screen.getByText(/no messages yet/i)).toBeInTheDocument()
    expect(screen.queryAllByRole("listitem")).toHaveLength(0)
  })
})

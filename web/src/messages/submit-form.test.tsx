import { render, screen } from "@testing-library/react"
import userEvent from "@testing-library/user-event"
import { describe, expect, it, vi } from "vitest"
import { SubmitForm } from "./submit-form"

describe("SubmitForm", () => {
  it("sends the typed text", async () => {
    const submit = vi.fn().mockResolvedValue(undefined)

    render(<SubmitForm onSubmit={submit} />)

    await userEvent.type(screen.getByRole("textbox", { name: /message/i }), "hello backend")
    await userEvent.click(screen.getByRole("button", { name: /send/i }))

    expect(submit).toHaveBeenCalledWith("hello backend")
  })

  it("clears the box after a send", async () => {
    render(<SubmitForm onSubmit={vi.fn().mockResolvedValue(undefined)} />)

    const box = screen.getByRole("textbox", { name: /message/i })

    await userEvent.type(box, "first message")
    await userEvent.click(screen.getByRole("button", { name: /send/i }))

    // A box that keeps its text invites the user to press Send twice and
    // submit the same message again.
    expect(box).toHaveValue("")
  })

  it.each([
    { scenario: "the box is empty", typed: "" },
    { scenario: "the box holds only spaces", typed: "   " },
  ])("does not send when $scenario", async ({ typed }) => {
    const submit = vi.fn().mockResolvedValue(undefined)

    render(<SubmitForm onSubmit={submit} />)

    if (typed !== "") {
      await userEvent.type(screen.getByRole("textbox", { name: /message/i }), typed)
    }

    await userEvent.click(screen.getByRole("button", { name: /send/i }))

    // The backend rejects empty text with a 400. Sending it anyway wastes a
    // round trip and shows the user an error they could not have avoided.
    expect(submit).not.toHaveBeenCalled()
  })
})

import { afterEach, describe, expect, it, vi } from "vitest"
import { submitMessage } from "./submit-message"

const apiUrl = "https://api.example.test"

describe("submitMessage", () => {
  afterEach(() => {
    vi.restoreAllMocks()
  })

  it("posts the text to the messages endpoint", async () => {
    const fetchSpy = vi
      .spyOn(globalThis, "fetch")
      .mockResolvedValue(new Response(null, { status: 202 }))

    await submitMessage(apiUrl, "hello backend")

    const [url, init] = fetchSpy.mock.calls[0]

    expect(url).toBe("https://api.example.test/messages")
    expect(init?.method).toBe("POST")
    expect(init?.headers).toMatchObject({ "content-type": "application/json" })
    expect(JSON.parse(String(init?.body))).toEqual({ text: "hello backend" })
  })

  it.each([
    { status: 400, scenario: "the text was rejected" },
    { status: 500, scenario: "the server failed" },
  ])("throws when $scenario", async ({ status }) => {
    vi.spyOn(globalThis, "fetch").mockResolvedValue(new Response(null, { status }))

    // fetch only rejects on a network fault. A 400 or a 500 resolves
    // normally, so without this check a failed send looks successful and the
    // user waits for a message that will never arrive.
    await expect(submitMessage(apiUrl, "hello backend")).rejects.toThrow()
  })
})

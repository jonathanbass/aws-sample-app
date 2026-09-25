import { afterEach, describe, expect, it, vi } from "vitest"
import { fetchMessages } from "./fetch-messages"

const apiUrl = "https://api.example.test"

function respondWith(body: unknown): void {
  vi.spyOn(globalThis, "fetch").mockResolvedValue(
    new Response(JSON.stringify(body), {
      status: 200,
      headers: { "content-type": "application/json" },
    }),
  )
}

describe("fetchMessages", () => {
  afterEach(() => {
    vi.restoreAllMocks()
  })

  it("reads the stored messages", async () => {
    respondWith([
      {
        messageId: "0199c0de-1111-4222-8333-444455556666",
        text: "stored earlier",
        submittedAt: "2026-09-24T12:00:00+00:00",
      },
    ])

    const messages = await fetchMessages(apiUrl)

    expect(messages).toEqual([
      {
        messageId: "0199c0de-1111-4222-8333-444455556666",
        text: "stored earlier",
        submittedAt: "2026-09-24T12:00:00+00:00",
      },
    ])
  })

  it.each([
    { status: 404, scenario: "the route is missing" },
    { status: 500, scenario: "the server failed" },
  ])("throws when $scenario", async ({ status }) => {
    vi.spyOn(globalThis, "fetch").mockResolvedValue(new Response("", { status }))

    // fetch only rejects on a network fault. Without a status check, a 500
    // body reaches Zod and the page reports a shape error for what is really
    // a server failure.
    await expect(fetchMessages(apiUrl)).rejects.toThrow(/history/i)
  })
})

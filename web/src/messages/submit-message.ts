/**
 * Posts one message to the ingest API.
 *
 * It returns nothing useful on purpose. The message reaches the page through
 * the WebSocket, not through this response, so the caller has nothing to read
 * from it. The API replies 202 Accepted for the same reason.
 */
export async function submitMessage(apiUrl: string, text: string): Promise<void> {
  const response = await fetch(`${apiUrl}/messages`, {
    method: "POST",
    headers: { "content-type": "application/json" },
    body: JSON.stringify({ text }),
  })

  // fetch only rejects on a network fault. A 400 or a 500 resolves normally,
  // so without this check a failed send looks successful and the user waits
  // for a message that will never arrive.
  if (!response.ok) {
    throw new Error(`Failed to submit the message: ${response.status}`)
  }
}

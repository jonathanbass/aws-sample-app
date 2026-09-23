import { useState, type FormEvent } from "react"
import { Button } from "@/components/ui/button"
import { Input } from "@/components/ui/input"

type SubmitFormProps = {
  onSubmit: (text: string) => Promise<void>
}

export function SubmitForm({ onSubmit }: SubmitFormProps) {
  const [text, setText] = useState("")

  const handleSubmit = async (event: FormEvent) => {
    event.preventDefault()

    // The backend rejects empty text with a 400. Stopping here saves a round
    // trip and an error the user could not have avoided.
    if (text.trim() === "") {
      return
    }

    await onSubmit(text)

    setText("")
  }

  return (
    <form onSubmit={handleSubmit} className="flex gap-2">
      <label htmlFor="message" className="sr-only">
        Message
      </label>

      <Input
        id="message"
        name="message"
        value={text}
        onChange={(event) => setText(event.target.value)}
        placeholder="Type a message"
        autoComplete="off"
      />

      <Button type="submit">Send</Button>
    </form>
  )
}

/*
  Endpoints come from the environment, never from a literal in a component.
  Amplify sets these at build time from the Terraform outputs, so the SPA does
  not need to know which account or region it was deployed to.

  Vite only exposes variables prefixed with VITE_ to the browser.
*/

function required(name: string, value: string | undefined): string {
  if (value === undefined || value === "") {
    // Fail at startup with the name of the missing variable. A blank endpoint
    // would otherwise surface as a confusing fetch or WebSocket error later.
    throw new Error(`${name} is not configured.`)
  }

  return value
}

export const config = {
  apiUrl: required("VITE_API_URL", import.meta.env.VITE_API_URL),
  webSocketUrl: required("VITE_WS_URL", import.meta.env.VITE_WS_URL),
}

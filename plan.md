# Simple AWS App Plan

### Preamble
Sample application to prove event driven architecture using AWS PaaS.
This is a personal project and is not for Bennetts. So there should be no Bennetts naming at all in the software.
This ia an overall architecture with technology and PaaS deployment preferences. It will need to be confirmed in concept and then broken into a series of small problems, each of which can be deployed in isolation of each other. We can discuss preferences over how many git repos these should live in. The factors will be based on them being able to deploy independantly and easily.

### Front-End
- This should be a react SPA
- use shadCN components for layout
- Tailwind theme ( colour palette should be mostly white with neutral buttons being a medium, grey-blue, hint of purple hue)
- deployed to AWS Amplify
- Should take a piece of text that gets sent to the first service over REST after a button is clicked
- the app will use the npm SignalR package to receive back the push response from the second service and add the text to a list

### Back-End
- 2 services independantly deployable - Linux Lambdas
- One should send an event (using UOW in Wolverine and Outbox pattern)
- The second service should handle that event over SQS in AWS
- Upon handling the message the second service should send over websockets/HTTP streaming the main text from the event
- .NET Wolverine
- SignalR for push to browser

### CI/CD
- GitHub Actions
- IAC with Terraform
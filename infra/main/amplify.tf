# ---------------------------------------------------------------------------
# Phase 6 - the SPA
#
# Terraform owns the Amplify app, its environment and its rewrite rules. It
# does NOT own the GitHub connection: the provider still wires the deprecated
# OAuth path, and `access_token` accepts only classic ghp_ tokens, not the
# fine-grained ones GitHub now issues. See the design document, section 5.
#
# Connect the repository ONCE, by hand, in the Amplify console. Amplify then
# builds on every push. The lifecycle block below stops Terraform undoing it.
# ---------------------------------------------------------------------------

resource "aws_amplify_app" "web" {
  name     = "${var.project_name}-web"
  platform = "WEB"

  environment_variables = {
    # Skip the build when nothing under appRoot changed. Without it every
    # backend-only push rebuilds the SPA and spends billed build minutes on an
    # identical artifact.
    AMPLIFY_DIFF_DEPLOY = "true"

    # REQUIRED for a monorepo, and it must equal `appRoot` in amplify.yml.
    # AWS sets this automatically only when the app is created through the
    # console. An app created by Terraform must set it here.
    AMPLIFY_MONOREPO_APP_ROOT = "web"

    # The SPA reads these at build time. Vite only exposes variables that
    # start with VITE_ to the browser.
    VITE_API_URL = aws_apigatewayv2_stage.ingest.invoke_url
    VITE_WS_URL  = aws_apigatewayv2_stage.notifier.invoke_url
  }

  # A single page application serves one index.html for every path. Without
  # this rule a refresh on any route returns 404, because no file matches.
  # The negative lookahead keeps real asset requests from being rewritten.
  custom_rule {
    source = "</^[^.]+$|\\.(?!(css|gif|ico|jpg|js|png|txt|svg|woff|woff2|ttf|map|json|webp)$)([^.]+$)/>"
    target = "/index.html"
    status = "200"
  }

  lifecycle {
    # The repository is connected by hand in the console, so Terraform must
    # not try to manage or clear it on the next apply.
    ignore_changes = [repository, oauth_token, access_token]
  }
}

resource "aws_amplify_branch" "main" {
  app_id      = aws_amplify_app.web.id
  branch_name = "main"

  enable_auto_build = true

  # The app-level variables above do not reach a branch that sets its own, so
  # they are repeated here. A missing VITE_ variable fails at startup with the
  # name of the variable, by design in web/src/config.ts.
  environment_variables = {
    AMPLIFY_DIFF_DEPLOY       = "true"
    AMPLIFY_MONOREPO_APP_ROOT = "web"
    VITE_API_URL              = aws_apigatewayv2_stage.ingest.invoke_url
    VITE_WS_URL               = aws_apigatewayv2_stage.notifier.invoke_url
  }
}

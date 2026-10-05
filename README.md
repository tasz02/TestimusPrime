# TestimusPrime

Draft C# framework for AI-assisted backend API test generation, execution, analysis, and reporting.

## What this draft covers

- Analyzes a connected ASP.NET API repository by scanning controller attributes and minimal API mappings
- Generates draft positive and negative test cases directly from detected endpoints
- Categorizes generated tests into `Smoke` and `Regression`
- Runs smoke tests for commit-triggered runs and regression tests for PR-triggered runs
- Logs request/response pairs, execution timings, and pass/fail outcomes
- Analyzes test results into suite summaries, failure lists, and status-code distributions
- Serves a lightweight dashboard with expandable run details

## Solution layout

- `/src/TestimusPrime.Core` - domain models, repository analysis, test generation, execution, reporting
- `/src/TestimusPrime.Runner` - CLI orchestrator, packaged as the installable `TestimusPrime.Tool` (`testimusprime`) with its native static dashboard publisher and bundled assets
- `/src/TestimusPrime.Dashboard` - minimal dashboard/API over persisted run artifacts
- `/tests/TestimusPrime.Tests` - unit tests for analysis, generation, and result summarization

## Running the draft

Requires the **.NET 10 SDK** (the tool targets `net10.0`).

### Execute smoke tests for a commit flow

```bash
dotnet run --project src/TestimusPrime.Runner -- \
  --repo=/absolute/path/to/api-repo \
  --api-base-url=https://localhost:5001 \
  --trigger=Commit
```

### Execute regression tests for a pull request flow

```bash
dotnet run --project src/TestimusPrime.Runner -- \
  --repo=/absolute/path/to/api-repo \
  --api-base-url=https://localhost:5001 \
  --trigger=PullRequest
```

Optional arguments:

- `--output=/absolute/path/to/output-directory`
- `--timeout-seconds=60`

Generated run artifacts are stored as JSON and can be opened in the dashboard.

### Generate a versioned testcase suite without executing it

```bash
dotnet run --project src/TestimusPrime.Runner -- \
  --mode=Generate \
  --repo=/absolute/path/to/api-repo \
  --output=/absolute/path/to/generated-testcases \
  --version-tag=testcases-v20260927150000
```

### Execute a previously generated testcase suite

```bash
dotnet run --project src/TestimusPrime.Runner -- \
  --mode=ExecuteGenerated \
  --test-suite=/absolute/path/to/generated-testcases/testcases-v20260927150000.json \
  --api-base-url=https://localhost:5001 \
  --trigger=PullRequest \
  --output=/absolute/path/to/output-directory
```

## Installable tool and JSON configuration

`TestimusPrime.Tool` version `0.1.0` exposes the `testimusprime` command. **The package is not automatically published** by this repository. A maintainer must pack it and publish the resulting package to NuGet before consumers can install it or use the reusable workflow:

```bash
dotnet pack src/TestimusPrime.Runner/TestimusPrime.Runner.csproj \
  --configuration Release --output artifacts/packages
# Run manually with your own NuGet publishing credentials:
dotnet nuget push artifacts/packages/TestimusPrime.Tool.0.1.0.nupkg \
  --source https://api.nuget.org/v3/index.json --api-key "$NUGET_API_KEY"
```

No publishing credentials are provided. Keep credentials out of source control. After publication:

```bash
dotnet tool install --global TestimusPrime.Tool --version 0.1.0
testimusprime --config=.github/testimusprime.json
```

Example `.github/testimusprime.json` in a consumer API repository:

```json
{
  "repo": "..",
  "api-base-url": "http://127.0.0.1:5050",
  "output": "../artifacts/testimusprime",
  "mode": "Run",
  "trigger": "Commit",
  "timeout-seconds": 30,
  "test-suite": "../artifacts/generated/testcases-v1.json",
  "version-tag": "testcases-v1",
  "history-limit": 20,
  "dashboard-subdirectory": "api-tests"
}
```

Without `--config`, the tool automatically loads `testimusprime.json` from the current working directory when present. Configuration-relative paths (`repo`, `output`, and `test-suite`) resolve from the **configuration file's folder**, not the current working directory. CLI arguments override configuration values; relative CLI paths also resolve from the configuration folder when a configuration file is loaded. Without configuration, relative CLI paths resolve from the current working directory except for the legacy `--output` behavior: it resolves from `--repo`, or from the saved suite's directory in `ExecuteGenerated` mode. Use absolute CLI paths when running automation. `test-suite` is needed only for `ExecuteGenerated`, and `version-tag` names the suite created by `Generate`. `trigger=Commit` selects smoke tests; `trigger=PullRequest` selects regression tests.

CLI options:

| Option | Purpose |
| --- | --- |
| `--config=path` | Load JSON defaults |
| `--repo=path` | API source repository to analyze |
| `--api-base-url=url` | Running API's base URL |
| `--output=path` | Report or generated-suite output directory |
| `--mode=Run\|Generate\|ExecuteGenerated` | Generate and run, generate only, or run a saved suite |
| `--trigger=Commit\|PullRequest` | Select the smoke or regression subset |
| `--timeout-seconds=30` | Per-request HTTP timeout |
| `--test-suite=path` | Previously generated suite JSON |
| `--version-tag=tag` | Generated suite version identifier |
| `--history-limit=20` | Maximum retained dashboard run reports |
| `--dashboard-subdirectory=api-tests` | Dashboard location within a shared static site |

An execution with failed tests saves its report before returning exit code `1`. To publish locally, use the tool's bundled native C# builder and assets—no checkout of this project's scripts or dashboard source is required:

```bash
testimusprime --mode=PublishDashboard --config=.github/testimusprime.json \
  --reports-directory=/absolute/path/to/reports \
  --existing-site=/absolute/path/to/current-site \
  --output-site=/absolute/path/to/fresh-site
```

`--reports-directory`, `--existing-site`, and `--output-site` are publisher-specific options. Use a fresh output directory separate from the existing site and reports. The publisher merges retained report history, applies `history-limit` and `dashboard-subdirectory`, and preserves unrelated content from the existing site. For a first publication, point `existing-site` at an empty directory.

## Reusable GitHub Actions workflow

`.github/workflows/analyze-api.yml` runs against the **consumer checkout**, not this repository. It installs a pinned, already-published tool package (`tool-version` defaults to `0.1.0`) with .NET 10. Pin the reusable workflow to a reviewed commit SHA or release tag; replace the placeholder below.

Consumer `.github/workflows/api-tests.yml`:

```yaml
name: API tests
on:
  push:
    branches: [main]
  pull_request:
  workflow_dispatch:

permissions:
  contents: read

jobs:
  analyze:
    # These grants are necessary only when dashboard publishing is enabled.
    permissions:
      contents: write
      pages: write
      id-token: write
    uses: tasz02/TestimusPrime/.github/workflows/analyze-api.yml@<reviewed-commit-sha>
    with:
      config-path: .github/testimusprime.json
      tool-version: "0.1.0"
      trigger: ${{ github.event_name == 'pull_request' && 'PullRequest' || 'Commit' }}
      publish-dashboard: true
      api-start-command: dotnet run --project src/MyApi -- --urls http://127.0.0.1:5050
      api-ready-url: http://127.0.0.1:5050/health
```

Set **Settings → Pages → Build and deployment → Source → GitHub Actions** before enabling publication. For artifact-only use, leave `publish-dashboard` unset (default `false`) and grant only `contents: read` in the caller job.

Inputs:

| Input | Default / behavior |
| --- | --- |
| `config-path` | Required, file inside the caller checkout, relative to its root |
| `tool-version` | Exact published package version, `0.1.0`; floating ranges are rejected |
| `api-base-url` | Empty; honors configuration unless explicitly overridden |
| `trigger` | Empty; honors configuration, or override with `Commit` / `PullRequest` |
| `mode` | Empty; honors configuration, or override with `Run` / `Generate` / `ExecuteGenerated` |
| `publish-dashboard` | `false`; explicit publication/exposure opt-in |
| `api-start-command` | Empty; optional trusted caller command executed using Bash |
| `api-ready-url` | Required with a startup command; polled until ready or timeout |

For a localhost API, provide its startup command and readiness endpoint as above. The workflow stops that command's process group even when analysis fails. Keep the server in the foreground; do not daemonize it or escape its process group. Startup logs are never included in report artifacts. Alternatively, omit both startup inputs and set `api-base-url` (or the configuration key) to an API reachable from the GitHub-hosted runner. `localhost` without a startup command does not refer to your workstation. Startup commands must be trusted literals, not interpolated PR titles, branch names, or other untrusted event text.

The workflow forces a fresh, dedicated absolute output directory for each invocation, ignoring configured `output`. It stages execution-report JSON and generated-suite JSON separately, uploading them as uniquely named `testimusprime-reports-…` and `testimusprime-suites-…` artifacts whenever each exists, even after execution fails. API startup logs are never uploaded. Download a suite artifact to reuse its JSON with `ExecuteGenerated`. Generation-only runs retain their suite artifact but have no execution reports and **do not publish a dashboard**. Failed tests still fail the analysis job, while saved reports are uploaded and, if opted in on a trusted event, published by a separate job.

The analysis job has read-only repository permissions. Only the separate publisher receives `contents: write`, `pages: write`, and `id-token: write`. Publication is allowed exclusively for `push`, `workflow_dispatch`, and `schedule`, never PR events (including `pull_request_target`). Treat commits/configuration on these publication paths and the package/workflow versions as trusted; restrict repository write access and protect publication branches and the `github-pages` environment as appropriate. Callers must grant the publisher's permissions; reusable workflows cannot elevate the caller's token.

Publication reads the same caller commit's configuration, checks out the consumer's existing `gh-pages` history when present, and persists the refreshed **complete site** back to that branch before deploying it. A failed history probe aborts rather than overwriting unknown content. The `dashboard-pages-publish` concurrency group serializes the entire publisher job; use that same group for other workflows updating this site's history. Keep **all unrelated site content in the shared `gh-pages` source**, preferably set `dashboard-subdirectory` to a dedicated folder, and coordinate external deployments. Content present only in an earlier Pages deployment, but absent from `gh-pages`, cannot be preserved. External writers that ignore this concurrency/source convention can still race or overwrite deployments. The workflow summary links the dynamically assigned site root; append the configured dashboard subdirectory to reach its dashboard.

### Publication and artifact privacy

**Authentication and automatic redaction are not implemented.** Reports, generated suites, and dashboards can contain request/response bodies, URLs, headers, test data, and source paths. GitHub Pages may expose reports publicly, and workflow artifacts expose their contents to anyone with artifact access even when Pages publication is disabled. Use sanitized, non-sensitive test data and non-production APIs; remove credentials, personal information, and secrets **before generating/executing/uploading artifacts**, not merely before deploying Pages. Do not enable publication unless that exposure is acceptable.

## Repository-local GitHub Actions

You can launch the built-in flows from the Actions tab with manual workflows:

- `Generate testcases` - analyzes a repository-relative folder, saves a versioned testcase suite artifact, and can push a matching git tag
- `Run generated testcases` - resolves a previously generated testcase artifact from its version tag and executes it against a target API
- `Run smoke suite` - starts the sample API fixture and runs the runner with `--trigger=Commit`
- `Run regression suite` - starts the sample API fixture and runs the runner with `--trigger=PullRequest`

The generate workflow uploads the versioned testcase JSON as an artifact named `generated-testcases-<version-tag>`, using the version tag as the canonical identifier for the generated suite. Generated suite version tags always end with `-<workflow-run-id>`; when you supply `test_suite_version_tag`, treat it as a custom prefix and omit any existing run-ID suffix. The run-generated workflow can then resolve the matching artifact from `test_suite_version_tag` alone. It also lets you choose `Commit` or `PullRequest` from a fixed dropdown to control whether the smoke or regression subset runs.

Each execution workflow uploads its run reports as artifacts, rebuilds the static dashboard, and deploys the refreshed site to GitHub Pages. The workflow also keeps the 20 most recent run reports in the `gh-pages` branch so the published site can merge new results into the retained dashboard history. Enable Pages for the repository by choosing **GitHub Actions** as the source.

## Dashboard

```bash
dotnet run --project src/TestimusPrime.Dashboard -- \
  --ReportDirectory=/absolute/path/to/output-directory
```

Endpoints:

- `GET /api/summary`
- `GET /api/runs`
- `GET /api/runs/{runId}`
- `GET /` - HTML dashboard with expandable request/response detail tables

The same dashboard UI is also stored in `src/TestimusPrime.Dashboard/wwwroot/index.html` so GitHub Pages can serve it as a static site. When the live API endpoints are unavailable, the page automatically falls back to published JSON under `data/summary.json` and `data/runs.json`.

## Current draft limitations

- Endpoint discovery is currently heuristic and optimized for ASP.NET controller attributes and minimal APIs
- Request bodies are scaffolded with placeholder and malformed JSON instead of schema-aware payload generation
- Auth, dependency mocking, and semantic code understanding are not implemented yet
- Suite assignment is rules-based and intended as a starting point for later AI enrichment

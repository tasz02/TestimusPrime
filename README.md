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
- `/src/TestimusPrime.Runner` - CLI orchestrator for generating and executing suites
- `/src/TestimusPrime.Dashboard` - minimal dashboard/API over persisted run artifacts
- `/tests/TestimusPrime.Tests` - unit tests for analysis, generation, and result summarization

## Running the draft

### Execute smoke tests for a commit flow

```bash
dotnet run --project /home/runner/work/TestimusPrime/TestimusPrime/src/TestimusPrime.Runner -- \
  --repo=/absolute/path/to/api-repo \
  --api-base-url=https://localhost:5001 \
  --trigger=Commit
```

### Execute regression tests for a pull request flow

```bash
dotnet run --project /home/runner/work/TestimusPrime/TestimusPrime/src/TestimusPrime.Runner -- \
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
dotnet run --project /home/runner/work/TestimusPrime/TestimusPrime/src/TestimusPrime.Runner -- \
  --mode=Generate \
  --repo=/absolute/path/to/api-repo \
  --output=/absolute/path/to/generated-testcases \
  --version-tag=testcases-v20260927150000
```

### Execute a previously generated testcase suite

```bash
dotnet run --project /home/runner/work/TestimusPrime/TestimusPrime/src/TestimusPrime.Runner -- \
  --mode=ExecuteGenerated \
  --test-suite=/absolute/path/to/generated-testcases/testcases-v20260927150000.json \
  --api-base-url=https://localhost:5001 \
  --trigger=PullRequest \
  --output=/absolute/path/to/output-directory
```

## GitHub Actions

You can launch the built-in flows from the Actions tab with manual workflows:

- `Generate testcases` - analyzes a repository-relative folder, saves a versioned testcase suite artifact, and can push a matching git tag
- `Run generated testcases` - resolves a previously generated testcase artifact from its version tag and executes it against a target API
- `Run smoke suite` - starts the sample API fixture and runs the runner with `--trigger=Commit`
- `Run regression suite` - starts the sample API fixture and runs the runner with `--trigger=PullRequest`

The generate workflow uploads the versioned testcase JSON as an artifact named `generated-testcases-<version-tag>`, using the version tag as the canonical identifier for the generated suite. Generated suite version tags always end with `-<workflow-run-id>`; when you supply `test_suite_version_tag`, treat it as a custom prefix and omit any existing run-ID suffix. The run-generated workflow can then resolve the matching artifact from `test_suite_version_tag` alone. It also lets you choose `Commit` or `PullRequest` from a fixed dropdown to control whether the smoke or regression subset runs.

Each execution workflow uploads its run reports as artifacts, rebuilds the static dashboard, and deploys the refreshed site to GitHub Pages. The workflow also keeps the 20 most recent run reports in the `gh-pages` branch so the published site can merge new results into the retained dashboard history. Enable Pages for the repository by choosing **GitHub Actions** as the source.

## Dashboard

```bash
dotnet run --project /home/runner/work/TestimusPrime/TestimusPrime/src/TestimusPrime.Dashboard -- \
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

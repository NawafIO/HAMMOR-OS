# ADR-007: Claude through the user's own Claude Code and Claude account

## Status
Accepted (owner's mission, 2026-10-07) and implemented on branch `claude/living-home-auth`. **UNVERIFIED ON WINDOWS**: not compiled or run on Windows. The command lines and output shapes were checked against an installed Claude Code 2.1.292. See [Verification](#verification).

## Date
2026-10-07

## Context
HAMMOR reached Claude only through the Anthropic API, so an API key was required before the assistant could answer. The owner wants the primary experience to be their own Claude account and subscription, through the official `claude` CLI (Claude Code), with the API key kept as an option.

Two kinds of constraint shape this.

**Anthropic's terms.** The Claude Code [legal and compliance](https://code.claude.com/docs/en/legal-and-compliance) page says, quoted:

> Anthropic does not permit third-party developers to offer Claude.ai login into their own applications, or to route requests through Free, Pro, or Max plan credentials on behalf of their users. Moreover, developers may not collect, store, or intermediate Claude.ai credentials or session tokens — sign-in to a Claude account must complete through Anthropic's own flow.

It then adds an exception:

> Nor does it prevent an end user from signing in to the unmodified Claude Code binary with their own Claude subscription.

For products that run Claude Code it also sets conditions:

- "The Claude Code binary must not be modified".
- Customers "may not remove, disable, or restrict any authentication method built into it".
- Each end user authenticates with their own credentials.

The same page notes that "Advertised usage limits for Pro and Max plans assume ordinary, individual usage of Claude Code and the Agent SDK." The [Agent SDK overview](https://code.claude.com/docs/en/agent-sdk/overview) documents running the CLI as a subprocess with `-p` and `--output-format json`. It repeats that, unless previously approved, third-party developers may not offer claude.ai login or rate limits for their products.

**HAMMOR's own rules.** It must not:

- run arbitrary shell or process commands;
- let the model act outside the permission engine, grants and confirmations (ADR-002 to ADR-006);
- ever handle a secret it does not need.

## Decision
Add a text-only provider, `claude-code`, that drives the user's installed, unmodified Claude Code in documented print mode. HAMMOR never handles the Claude credential. The API-key provider (`claude`) stays, now optional.

### Commands
These are the only command lines HAMMOR runs. They are fixed lists in `ClaudeCodeArguments`, passed through `ProcessStartInfo.ArgumentList`:

| Purpose | Command |
|---|---|
| Version | `claude --version` |
| Sign-in state | `claude auth status --json`: only `loggedIn` and `authMethod` are read |
| Sign in | `claude auth login --claudeai`, in Claude Code's own console window; HAMMOR reads nothing from it |
| Sign out | `claude auth logout` |
| One turn | `claude -p --output-format json --tools "" --no-session-persistence --safe-mode --strict-mcp-config --system-prompt=<text>`, prompt and history on standard input |

- **No tools.** `--tools ""` disables every built-in tool and `--strict-mcp-config` loads no MCP server, so Claude Code cannot read, write or run anything.
- **No customizations.** `--safe-mode` turns off hooks, plugins, skills and CLAUDE.md; Claude Code's documentation says sign-in still works normally.
- **No transcripts.** `--no-session-persistence` keeps HAMMOR's conversations out of Claude Code's saved sessions.
- **One argument.** The system prompt is a single `--system-prompt=` argument, so its text can never become another option.
- **Not `--bare`.** It is not used, because it disables sign-in with a Claude account.
- **Never `--dangerously-skip-permissions`.** HAMMOR never passes it.

### Credentials
HAMMOR never reads, stores, displays or logs a Claude credential or the `~/.claude` folder:
- The environment Claude Code inherits is unchanged, so no sign-in method is disabled. If the user's environment sets `ANTHROPIC_API_KEY`, Claude Code may use it; Settings shows the method Claude Code reports.
- Error text from Claude Code is redacted (`SecretRedactor`) and length-bounded before it is shown, logged or audited.
- Prompts and answers are never logged.

### Process
- **Executable.** Only a real executable is started: `claude.exe` on Windows. HAMMOR looks first at the native installer's location (`%USERPROFILE%\.local\bin`), then on fully qualified PATH entries, which includes WinGet's link.
- **No shell.** The npm launcher `claude.cmd` runs through `cmd.exe`, which re-parses arguments, so HAMMOR reports it and never starts it. No `cmd.exe`, no PowerShell.
- **Working directory.** A HAMMOR-owned empty folder, `%LOCALAPPDATA%\HAMMOR\claude-code`.
- **Limits.** Stdin and stdout are redirected:
  - 5-minute timeout per turn and 30 seconds per account command;
  - cancellation;
  - output bounded to 4 million characters;
  - on timeout, cancellation or overflow the whole process tree is killed.
- **Output.** Answers come only from the JSON result; HAMMOR never parses terminal text.

### States
`ClaudeCodeState` covers:
- not installed;
- a script launcher only;
- not signed in;
- signed in (with method);
- usage limited (Claude Code's own message, for example "You've hit your session limit · resets 3:45pm");
- needs update (Claude Code rejected an option);
- error.

The status is cached for two minutes. Every request's outcome updates it, and the status bar follows. A usage limit stays visible until a request succeeds, and requests stay allowed because only the next one can show that the limit has reset.

### Selection
`AiSettings.PrimaryProvider` chooses the provider. Settings offers "Claude Code — your Claude account (recommended)" and "Anthropic API key (optional)". First run picks `claude-code` when no API key is entered. Core's default is unchanged.

## Consequences
- **No API key needed.** Claude works with only a Claude account, and usage counts against the user's own plan.
- **Text only.** HAMMOR's model-driven tools (filesystem, Git, projects) and their permission flow need the API-key provider. Settings says so.
- **Unattended tasks.** These route through the selected provider. With `claude-code` they run text-only, with no tools on either side, so a grant can never be exercised and nothing outside ADR-003/004 changes. Personal usage expectations ("ordinary, individual usage") apply.
- **Latency.** Each turn starts one Claude Code process (about a second), plus two short ones when the cached status has expired.
- **Fragility.** A Claude Code release that renames a flag shows "needs update" with Claude Code's own error rather than failing silently.
- **Distribution.** Anthropic's terms allow this for an end user signing in to their own unmodified Claude Code. **Distributing HAMMOR to other people** with this provider means "running Claude Code in your products", which needs Anthropic's Commercial Terms and the conditions above. Confirm with Anthropic before shipping it to anyone else.
- **Unchanged:**
  - Core;
  - the permission engine and filesystem policy;
  - path-scoped grants and task-runner restrictions;
  - Git confinement (ADR-005/006);
  - the confirmation flow and audit behaviour.

  The provider is new Infrastructure code, registered as one more `IAiProvider`.

## Verification
Not yet run on Windows. Tests (`dotnet test -c Release`):
- **Core tests** gain 66 cases for the locator, the fixed arguments, output parsing (from real 2.1.292 output shapes), the transcript, the account and the provider. They use a fake Claude Code that starts no process.
- **Manual.** The process runner itself (timeouts, tree kill, the sign-in window) is verified by hand: steps 16 to 19 of the checklist in [docs/LIVING-CORE.md](../LIVING-CORE.md#windows-verification).

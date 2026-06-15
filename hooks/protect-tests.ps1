# PreToolUse guard: blocks Claude Code from editing protected files (the test suite,
# this guard, and the hook config). The developer changes them by editing manually
# outside a Claude session, or by temporarily removing the hook in .claude/settings.json.
#
# Reads the tool-call JSON from stdin, inspects the target file path, and returns a
# "deny" decision when the path is protected. Otherwise allows (exit 0, no output).

$ErrorActionPreference = 'Stop'

try {
    $raw = [Console]::In.ReadToEnd()
    if ([string]::IsNullOrWhiteSpace($raw)) { exit 0 }

    $payload = $raw | ConvertFrom-Json
    $path = $payload.tool_input.file_path
    if ([string]::IsNullOrWhiteSpace($path)) { exit 0 }

    # Normalize to forward slashes for matching.
    $norm = ($path -replace '\\', '/')

    $protected =
        $norm -match '(^|/)tests/' -or
        $norm -match '/hooks/protect-tests\.ps1$' -or
        $norm -match '/\.claude/settings\.json$' -or
        $norm -match '(^|/)\.githooks/'

    if ($protected) {
        $reason = "Bloqueado: '$path' faz parte da suite de testes/governanca protegida. " +
                  "Esses arquivos so podem ser alterados pelo desenvolvedor (nao pelo Claude Code)."
        $decision = @{
            hookSpecificOutput = @{
                hookEventName            = 'PreToolUse'
                permissionDecision       = 'deny'
                permissionDecisionReason = $reason
            }
        }
        $decision | ConvertTo-Json -Compress -Depth 5
        exit 0
    }

    exit 0
}
catch {
    # Fail open: a guard error must not block legitimate edits to normal source files.
    exit 0
}

---
schema_version: 1
open_count: 1
waived_count: 0
fixed_count: 0
total_count: 1
last_updated: 2026-11-16T20:56:26.515Z
---

# Broken Windows Ledger

> Cross-phase defect register. With `workflow.windows_enforce` enabled, `/gsd-ship` blocks while `open_count > 0`.
> Waive with `gsd-tools windows waive <id> "<reason>"` (reason required).
> Mark fixed with `gsd-tools windows fixed <id>`.

| id | phase | kind | file | line | description | status | reason | recorded_at | resolved_at |
|----|-------|------|------|------|-------------|--------|--------|-------------|-------------|
| 1 | 01 | stub | src/FinanceBot/Messages.cs | 26 | No overrun branch (D-02): over 200 zł the second line shows negative remainder; plan 01-02 adds 🔴 branch | open |  | 2026-11-16T20:56:26.515Z |  |

````json
[
  {
    "id": 1,
    "kind": "stub",
    "phase": "01",
    "file": "src/FinanceBot/Messages.cs",
    "line": 26,
    "description": "No overrun branch (D-02): over 200 zł the second line shows negative remainder; plan 01-02 adds 🔴 branch",
    "status": "open",
    "reason": "",
    "recorded_at": "2026-11-16T20:56:26.515Z",
    "resolved_at": null,
    "milestone": null
  }
]
````

# AI Engineering Note

This note is a starter record and should be reviewed by the candidate before submission.

- Tools used: GitHub Copilot in VS Code.
- Prompt pattern: requested a domain-first modular-monolith structure, then implemented and validated one slice at a time.
- Generated or modified: project boilerplate, Domain primitives/value objects, initial aggregate/policy code, focused tests, and architecture notes.
- Verification: Domain unit tests run against the .NET 10 SDK; the complete solution and infrastructure have not yet been verified end to end.
- Defect found during iteration: an initial margin-policy test used repeated snapshots from the same minute. The policy and fixture were corrected to require adjacent closed-minute buckets.
- Rejected suggestion: add a specific rejected suggestion from the actual review session before final submission; none has been recorded yet.
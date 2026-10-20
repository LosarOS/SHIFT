# Security

SHIFT runs user-selected local apps, files, shortcuts and shell commands. It is **not a sandbox** and does not determine whether an arbitrary executable or command is safe. Review imported workspaces. Never store credentials in target/argument fields.

Commands require explicit per-launch consent. URLs accept HTTP/HTTPS only. Imported data is validated and never auto-executed. App/shortcut launch still executes trusted code, and opening documents invokes their registered handler.

Data, backups and exports are unencrypted local files. Local OS account access rules apply. Diagnostic messages can contain local paths; redact logs before sharing.

For a sensitive finding, use GitHub private vulnerability reporting on the eventual repository when enabled. Do not post exploit material or private data in a public issue. No reporting email or repository URL is invented for this initial source tree.

Before public beta: dependency audit, full Windows QA and an explicit signing policy. Development and packaging require NuGet/network access; installed SHIFT has no application-owned cloud or telemetry calls.

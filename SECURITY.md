# Security Policy

## Supported Versions

| Version | Supported          |
| ------- | ------------------ |
| 3.0.x   | :white_check_mark: |
| < 3.0   | :x:                |

## Reporting a Vulnerability

If you discover a security vulnerability, please report it responsibly:

1. **Do NOT open a public issue.**
2. Use GitHub's private vulnerability reporting (Security tab → Report a vulnerability).
3. Include: description, steps to reproduce, and potential impact.

## Scope

Lingarr on Steroids is a self-hosted application. Users are responsible for securing their own network, API keys, and container environment. This project does not handle user authentication beyond the optional Hangfire dashboard credentials.

## Best Practices for Users

- Use strong, unique passwords for `DB_PASSWORD` and `HANGFIRE_PASSWORD`.
- Do not expose the Hangfire dashboard (`/hangfire`) to the public internet.
- Keep your Radarr/Sonarr API keys secret.
- Run the container as a non-root user when possible.

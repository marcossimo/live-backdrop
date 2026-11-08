# Security policy

## Reporting a vulnerability

Please do not open a public issue for security problems. Report them privately through
GitHub: **Security → Report a vulnerability** on this repository. You will get a reply within a
week.

## Scope

Live Backdrop runs locally and makes no network requests. The areas worth a close look are:

- the overlay reading `settings.json` (a file any program running as you can write), including
  the picture path it opens;
- the plugin starting the executable recorded in `overlayPath`;
- the "start with Windows" entry under
  `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`.

## Supported versions

Only the latest release receives fixes.

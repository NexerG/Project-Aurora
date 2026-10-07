# Calendar plan — calendar in Thorium with Google and iCloud

**Status:** designed 2026-10-06, parked — nothing built. 2026-10-07: the engine side is merged into the planner, see [planner-plan](planner-plan.md); C1 is superseded by the planner's PC (landed 2026-10-07), C2 landed 2026-10-07 as the planner's C2, C3–C7 stay parked. Further calendar work needs its own detailed plan (files, types, signatures) before
any code. Part of the Thorium productivity suite (WIP list, Phase A). No decision record yet.

## Settled by the user (2026-10-06)
- Own visual design, not a copy of Google or Apple.
- "Dependencies" means both: attachments on one event (travel before/after, prep, user-defined kinds) and links
  between events (B follows A; moving A moves B). Since 2026-10-07 these live on planner tickets (planner C2).
- ~~Local calendars are vault files, `*.calendar.xml`, opened in a tab like sheets.~~ **Superseded 2026-10-07:** a calendar event is a planner ticket in `*.planner.xml`; the calendar is a planner view ([planner-plan](planner-plan.md)).
- One CalDAV client for Google and iCloud (not Google's REST API).
- Sync is read-only first; two-way is its own later phase.
- Secrets go to WinRT `Windows.Security.Credentials.PasswordVault` (TFM is `net10.0-windows10.0.22621.0`).
- The app can start at login and shows OS notifications from the calendar; the notification path is generic so
  other features can use it.
- Google sign-in is the system browser consent flow (default browser, not Chrome specifically).

## Open (ask before C4)
- Background shape: whole app hides to tray (recommended — idle frames keep CPU near zero, GPU memory stays
  resident) vs a separate small background process for reminders and sync that launches Thorium on toast click
  (lighter, needs a shared cache and IPC).
- Closing the main window: always hide to tray while start-at-login is on, or a separate setting.

## Shape
| Part | Where | Proposed types |
|---|---|---|
| model (superseded 2026-10-07 by `PlannerDocument` / `PlannerTicket`) | engine, `ArctisAurora.Core.UI`, beside `Sheet*` | `CalendarDocument` (calendars: source Local/Google/iCloud, colour, events), `CalendarEvent` (UID, title, start/end + time zone, all-day, location, notes, recurrence, attachments, links), `CalendarAttachment` (kind, side before/after, duration, label), edit records for undo like `SheetCellEdit` |
| attachment kinds | engine data, XML like gradients/palettes | Travel and Prep shipped; user kinds added in XML |
| view (superseded 2026-10-07 by the planner's calendar view, PC) | engine, `ArctisAurora.Core.UI` | `CalendarControl` (Day/Week time grid, Month grid, overlap packing into columns, dashed attachment blocks, now line), `CalendarEditorControl : IFileEditor` (create, drag move, edge resize, edit popup) |
| now line | `CalendarControl` | red line + dot across today; one redraw per minute via `FrameScheduler.RequestFrameAt` so idle frames stay idle |
| hosting (superseded 2026-10-07: "New planner", `BuildPlannerTab`) | Thorium `VaultBrowserControl` | tab + "New calendar" menus, same path as `BuildSheetTab` / `NewSheet` |
| sync, accounts, credentials | Thorium, new `Thorium.Calendar` namespace | CalDAV client, iCalendar mapping, Google OAuth, iCloud account, `PasswordVault` wrapper — keeps networking out of the engine |
| notifications | engine, generic | `Notifications.Show(title, body, onClick)` |

## Integrations
- **CalDAV for both.** WebDAV XML + iCalendar (RFC 5545) text. Google's endpoint needs OAuth 2.0 over HTTPS and the
  "CalDAV API" enabled in the Cloud project; it shares scopes with the Calendar API.
- **No new NuGet.** `HttpClient`, `HttpListener`, `System.Xml`, `TimeZoneInfo` (IANA ids on .NET 10), PKCE via
  `System.Security.Cryptography`. Google's token endpoint answers JSON → parsed with BCL `System.Text.Json`; a wire
  format, not a data file.
- **Recurrence is native**, not `Ical.Net`: common RRULE subset, EXDATE, RECURRENCE-ID overrides; `BYSETPOS` /
  `BYWEEKNO` left out until needed.
- **Attachments over the wire:** Apple's `X-APPLE-TRAVEL-DURATION` mapped both ways; other kinds as `X-AURORA-*`
  properties. Unverified: whether Google's CalDAV keeps unknown properties.
- **Remote changes are polled** (CalDAV ctag/etag) every few minutes. Google push needs a public HTTPS endpoint.

### Google sign-in
- Default browser via `Process.Start` (shell execute) → consent screen → redirect to `127.0.0.1:<port>` loopback
  listener with PKCE → code exchanged for tokens → `PasswordVault`. Embedded web views are blocked by Google anyway.
- Desktop OAuth clients get a client secret that is not confidential; shipping it with PKCE is accepted practice.
- Scope: `calendar.readonly` for read-only; two-way needs `calendar` and one re-consent.
- Setup is the user's: Google Cloud project, Desktop OAuth client, consent screen, CalDAV API enabled.

| Publishing state | Sign-in looks like | Limits |
|---|---|---|
| Testing | no warning, listed test accounts only | consent and refresh tokens expire after 7 days; ≤100 testers |
| Production, unverified | one-time "Google hasn't verified this app" (Advanced → Go to app) | 100 users over the project's lifetime; no weekly expiry |
| Production, verified | clean consent with app name/logo | none that matter |

- Personal use → Production, unverified.
- Distribution → verification: owned domain verified with Google, homepage + privacy policy on it, scope
  justification, demo video of the consent flow; calendar scopes are sensitive, not restricted → no paid security
  assessment; review days to weeks. Verify for the scope the app will end up needing.

### iCloud sign-in
- No OAuth or consent screen for iCloud Calendar; Sign in with Apple only shares name/email.
- Flow: "Connect iCloud" opens the Apple ID app-specific passwords page in the browser; user generates one, pastes it
  with their Apple ID once; stored in `PasswordVault`. Requires 2FA on the Apple ID.

## Background and notifications (C4)
- Start at login: value under `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`, written when the setting is
  turned on; no admin.
- Toasts: WinRT `ToastNotificationManager`; unpackaged apps register an AUMID under
  `HKCU\Software\Classes\AppUserModelId\<id>` (written by the app). Toast payload is XML. No Windows App SDK or
  Community Toolkit package.
- Reminder sources: event reminders (VALARM) and attachments — "leave now" when a travel block starts.
- Close to tray fits `Shutdown.xml`: a `Request` step hides the window and returns false; tray Quit skips it.
- Tray icon is `Shell_NotifyIcon` via `DllImport` (GLFW has no tray).
- Single instance: named mutex; a second launch forwards "show" to the running one and exits.

## Phases
| # | Scope | Verify |
|---|---|---|
| C1 (superseded 2026-10-07 by planner P1/P2/PC) | model, `*.calendar.xml`, Day/Week/Month, now line, create/move/resize with undo, "New calendar" menu | `Calendar` suite (overlap packing, XML round-trip, undo); screenshot of the running app |
| C2 (landed 2026-10-07 as the planner's C2) | attachments (kinds in XML, travel/prep, custom) and event links (B follows A) | tests: attachments and linked events follow a move/resize; screenshot |
| C3 | iCalendar read/write, recurrence, time zones | tests on fixture `.ics`, including a DST switch |
| C4 | tray, start at login, single instance, toasts, reminders incl. "leave now" | manual: login start, toast fires and opens the event |
| C5 | CalDAV client + iCloud account, read-only | live test against the user's account (user signs in) |
| C6 | Google OAuth via browser + Google CalDAV, read-only | same |
| C7 | two-way sync: etags, 412 conflicts, offline edit queue | tests against a fake server; then live |

Handoff (CLAUDE.md §10): C3 fixture `.ics` and mechanical XML/menu wiring can go to `aurora-mechanic`.

## Sources
- https://developers.google.com/calendar/caldav/v2/guide
- https://developers.google.com/calendar/api/auth
- https://support.google.com/cloud/answer/15549945

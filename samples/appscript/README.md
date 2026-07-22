# Xenaia availability Apps Script templates

Parameterized Google Apps Script templates for driving the Xenaia
availability sync surface from a Google Sheet. This is a Meridian Trails
demo: every deployment-specific value (backend URL, API key, tab names)
comes from Script Properties, never from code, so the same two files work
for any Xenaia tenant.

Bind both scripts to the same spreadsheet as one Apps Script project (for
example, Extensions > Apps Script from the sheet). They share a single
Script Properties store and a single global scope, so `AvailabilitySync.gs`
can call the `getConfig_()` helper defined in `AvailabilityPatch.gs`.

## Scripts

### AvailabilityPatch.gs

Reads pending availability changes from the patch tab and pushes them to
`POST {XENAIA_BASE_URL}/api/availability/patch`.

1. Reads every non-empty row in column A of the patch tab, starting at
   row 2 (row 1 is a header).
2. Parses and validates each row locally (see layout below). A row that
   fails validation never reaches the network: its column B cell gets a
   timestamped parse error instead.
3. Valid rows are batched in groups of at most 1000 items (the same cap the
   backend enforces via `Sync:Availability:MaxBatchSize`) and POSTed with
   the `X-Api-Key` header.
4. If a batch request itself fails (non-200 response), every row in that
   batch gets a timestamped failure written to its column B cell. On
   success, column B is left for the Xenaia backend to fill in once the row
   has actually been pushed to the booking system (the request carries each
   row's `patchStatusRange`, which points the backend at that cell).
5. Two entry points, meant for time-driven triggers: `runPatch()` (normal
   run, `force: false`) and `runForcePatch()` (`force: true`, re-pushes rows
   whose values look unchanged).

### AvailabilitySync.gs

`runSheetSync()` POSTs `{ spreadsheetId }` to
`{XENAIA_BASE_URL}/api/availability/sync`, which makes the backend refresh
the get tab from the booking system, and shows the result as a toast:

- 200: a summary (combinations found, successful/failed fetches, rows
  updated, timeslots not found).
- 503: the backend has no spreadsheet provider configured for this tenant.
- any other status: the HTTP code and response body.

## Tab layouts

### Patch tab

| Column | Contents |
|---|---|
| A | One pipe-delimited row per pending change: `product_id\|option_id\|from\|to\|time\|vacancies\|stop_sales` |
| B | Status write-back: a parse error from the script, or the outcome the Xenaia backend writes once the row has been processed. |

Column A fields:

- `product_id`, `option_id`: the catalog's external ids (positive integers).
- `from`, `to`: dates in `yyyy-MM-dd` format. `from` and `to` must be the same
  day: multi-day rows are not supported, so submit one row per day. A row whose
  `from` and `to` differ is rejected (by the script and by the backend).
- `time`: `HH:mm` for a specific timeslot, or empty for a slotless product.
- `vacancies`: an integer to set, or empty to leave the current value unchanged.
- `stop_sales`: `true` or `false` to set, or empty to leave the current value unchanged.

### Get tab

Canonical layout; columns F, G, and H are written by the Xenaia backend, not
by either script.

| Column | Contents |
|---|---|
| A | Time (`HH:mm`), empty for a slotless timeslot. |
| B | Product external id. |
| C | Option external id. |
| D | Participant aliases (informational). |
| E | Combination key: `product_id\|option_id\|from\|to`. |
| F | Vacancies (written back by the backend). |
| G | Last-synced timestamp (written back by the backend). |
| H | Stop-sales flag (written back by the backend). |

## Script Properties

Set these under Project Settings > Script Properties in the Apps Script
editor. All four are required; running any entry point with one missing
throws immediately, listing every missing property, before any request is
made.

| Property | Example | Notes |
|---|---|---|
| `XENAIA_BASE_URL` | `https://YOUR-XENAIA-HOST` | Base URL of the Xenaia API host, no trailing slash needed. |
| `XENAIA_API_KEY` | `YOUR-API-KEY` | Sent as the `X-Api-Key` header on every request. |
| `PATCH_SHEET_NAME` | `Availability Patch` | Name of the patch tab in this spreadsheet. |
| `GET_SHEET_NAME` | `Availability Get` | Name of the get tab in this spreadsheet. |

The spreadsheet id is never a Script Property: both scripts read it at
runtime from `SpreadsheetApp.getActiveSpreadsheet().getId()`, so the
templates stay portable across copies of the sheet.

## Trigger installation

Use the "Xenaia Automations" menu that appears when the spreadsheet is
opened (added by `AvailabilityPatch.gs`'s `onOpen()`):

- **Install triggers**: creates a time-driven trigger running `runPatch`
  every 5 minutes and another running `runForcePatch` every hour. Re-running
  this menu item first removes any existing Xenaia triggers, so it is safe
  to run more than once.
- **Remove triggers**: deletes both triggers.
- **Run patch now** / **Run force patch** / **Run sheet sync**: manual runs
  of the corresponding function, useful for testing before installing
  triggers.

Alternatively, call `installTriggers()` or `removeTriggers()` directly from
the Apps Script editor.

## Notes

- Both scripts require `XENAIA_API_KEY` to be set; requests are rejected by
  the backend's API-key gate otherwise.
- The backend enforces the same 1000-item batch cap the patch script uses
  locally (`Sync:Availability:MaxBatchSize`), so a request assembled by this
  template is never rejected for being oversized.

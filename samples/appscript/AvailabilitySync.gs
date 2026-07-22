/**
 * AvailabilitySync.gs
 *
 * Meridian Trails demo template: triggers the Xenaia backend's inbound
 * availability sync, which reads the get tab, fetches current availability
 * from the booking system, and writes updated vacancies, timestamps, and
 * stop-sales flags back onto the sheet.
 *
 * Get tab layout (read and written by the backend, not by this script):
 *   Column A: time (HH:mm), empty for a slotless timeslot.
 *   Column B: product external id.
 *   Column C: option external id.
 *   Column D: participant aliases (informational).
 *   Column E: combination key, product_id|option_id|from|to.
 *   Column F: vacancies, written back by the backend.
 *   Column G: last-synced timestamp, written back by the backend.
 *   Column H: stop-sales flag, written back by the backend.
 *
 * Required Script Properties: shared with AvailabilityPatch.gs (see its
 * getConfig_ function and README.md).
 */

function runSheetSync() {
  var config = getConfig_();
  var response = UrlFetchApp.fetch(config.baseUrl + '/api/availability/sync', {
    method: 'post',
    contentType: 'application/json',
    headers: { 'X-Api-Key': config.apiKey },
    payload: JSON.stringify({ spreadsheetId: config.spreadsheetId }),
    muteHttpExceptions: true
  });

  var code = response.getResponseCode();
  var ss = SpreadsheetApp.getActiveSpreadsheet();

  if (code === 503) {
    ss.toast('Sheet sync unavailable: the backend has no spreadsheet provider configured.', 'Xenaia sheet sync');
    return;
  }
  if (code !== 200) {
    ss.toast('Sheet sync failed: HTTP ' + code + ': ' + response.getContentText(), 'Xenaia sheet sync');
    return;
  }

  var summary = JSON.parse(response.getContentText());
  ss.toast(
    summary.combinations + ' combinations, ' + summary.successfulFetches + ' succeeded, ' +
    summary.failedFetches + ' failed, ' + summary.rowsUpdated + ' rows updated, ' +
    summary.timeslotsNotFound + ' timeslots not found.',
    'Xenaia sheet sync complete');
}

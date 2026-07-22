/**
 * AvailabilityPatch.gs
 *
 * Meridian Trails demo template: reads pending availability changes from the
 * patch tab, column A, one pipe-delimited row per change:
 *   product_id|option_id|from|to|time|vacancies|stop_sales
 * Parse errors and (once processed) the backend's outcome are written back
 * to column B. Valid rows are batched to the backend's cap and POSTed to
 * the availability patch endpoint. Full tab layout, required Script
 * Properties, and trigger setup: see README.md.
 */

var MAX_BATCH_SIZE = 1000; // Mirrors the backend's Sync:Availability:MaxBatchSize default.

function getConfig_() {
  var props = PropertiesService.getScriptProperties().getProperties();
  var required = ['XENAIA_BASE_URL', 'XENAIA_API_KEY', 'PATCH_SHEET_NAME', 'GET_SHEET_NAME'];
  var missing = required.filter(function (key) { return !props[key]; });
  if (missing.length > 0) throw new Error('Missing required Script Properties: ' + missing.join(', '));
  return {
    baseUrl: props.XENAIA_BASE_URL.replace(/\/+$/, ''),
    apiKey: props.XENAIA_API_KEY,
    patchSheetName: props.PATCH_SHEET_NAME,
    getSheetName: props.GET_SHEET_NAME,
    spreadsheetId: SpreadsheetApp.getActiveSpreadsheet().getId()
  };
}

function onOpen() {
  SpreadsheetApp.getUi()
    .createMenu('Xenaia Automations')
    .addItem('Run patch now', 'runPatch')
    .addItem('Run force patch', 'runForcePatch')
    .addItem('Run sheet sync', 'runSheetSync')
    .addSeparator()
    .addItem('Install triggers', 'installTriggers')
    .addItem('Remove triggers', 'removeTriggers')
    .addToUi();
}

function runPatch() { runPatchInternal_(false); }

function runForcePatch() { runPatchInternal_(true); }

function runPatchInternal_(force) {
  var config = getConfig_();
  var sheet = SpreadsheetApp.getActiveSpreadsheet().getSheetByName(config.patchSheetName);
  if (!sheet) throw new Error('Patch sheet not found: ' + config.patchSheetName);

  var lastRow = sheet.getLastRow();
  var validItems = [];
  var invalidCount = 0;

  for (var row = 2; row <= lastRow; row++) {
    var raw = sheet.getRange(row, 1).getValue();
    if (raw === '' || raw === null) continue;

    var parsed = parsePatchRow_(String(raw), row, config.patchSheetName);
    if (parsed.error) {
      writeStatus_(sheet, row, 'Parse error: ' + parsed.error);
      invalidCount++;
    } else {
      validItems.push(parsed.item);
    }
  }

  var accepted = 0;
  var skipped = 0;
  for (var i = 0; i < validItems.length; i += MAX_BATCH_SIZE) {
    var chunk = validItems.slice(i, i + MAX_BATCH_SIZE);
    var result = postPatchBatch_(config, chunk, force);
    if (result.error) {
      chunk.forEach(function (item) {
        writeStatusForRange_(sheet, item.patchStatusRange, 'Request failed: ' + result.error);
      });
    } else {
      accepted += result.accepted;
      skipped += result.skipped;
    }
  }

  var summary = validItems.length + ' rows sent (' + accepted + ' accepted, ' + skipped +
    ' skipped), ' + invalidCount + ' parse errors.';
  SpreadsheetApp.getActiveSpreadsheet().toast(summary, 'Xenaia availability patch');
}

function parsePatchRow_(raw, row, patchSheetName) {
  var fields = raw.split('|');
  if (fields.length !== 7) return { error: 'expected 7 pipe-delimited fields, found ' + fields.length };

  var productId = fields[0].trim();
  var optionId = fields[1].trim();
  var from = fields[2].trim();
  var to = fields[3].trim();
  var time = fields[4].trim();
  var vacancies = fields[5].trim();
  var stopSales = fields[6].trim();

  if (!/^\d+$/.test(productId)) return { error: 'product_id must be a positive integer' };
  if (!/^\d+$/.test(optionId)) return { error: 'option_id must be a positive integer' };
  if (!/^\d{4}-\d{2}-\d{2}$/.test(from)) return { error: 'from must be yyyy-MM-dd' };
  if (!/^\d{4}-\d{2}-\d{2}$/.test(to)) return { error: 'to must be yyyy-MM-dd' };
  if (from !== to) return { error: 'multi-day patch items are not supported; submit one row per day' };
  if (time !== '' && !/^([01]\d|2[0-3]):[0-5]\d$/.test(time)) return { error: 'time must be HH:mm or empty' };
  if (vacancies !== '' && !/^\d+$/.test(vacancies)) return { error: 'vacancies must be a non-negative integer or empty' };
  if (stopSales !== '' && stopSales !== 'true' && stopSales !== 'false') return { error: 'stop_sales must be true, false, or empty' };

  return {
    item: {
      productExternalId: Number(productId),
      optionExternalId: Number(optionId),
      from: from,
      to: to,
      times: time === '' ? [] : [time],
      vacancies: vacancies === '' ? null : Number(vacancies),
      stopSales: stopSales === '' ? null : (stopSales === 'true'),
      patchStatusRange: quoteTab_(patchSheetName) + '!B' + row + ':B' + row
    }
  };
}

function postPatchBatch_(config, items, force) {
  var response = UrlFetchApp.fetch(config.baseUrl + '/api/availability/patch', {
    method: 'post',
    contentType: 'application/json',
    headers: { 'X-Api-Key': config.apiKey },
    payload: JSON.stringify({ spreadsheetId: config.spreadsheetId, force: !!force, items: items }),
    muteHttpExceptions: true
  });

  var code = response.getResponseCode();
  if (code !== 200) return { error: 'HTTP ' + code + ': ' + response.getContentText() };
  return JSON.parse(response.getContentText());
}

function writeStatus_(sheet, row, message) {
  sheet.getRange(row, 2).setValue(timestamp_() + ' ' + message);
}

function writeStatusForRange_(sheet, patchStatusRange, message) {
  writeStatus_(sheet, Number(patchStatusRange.split('!B')[1].split(':')[0]), message);
}

function timestamp_() { return new Date().toISOString(); }

// Quotes a tab name for A1 notation: Google requires single quotes around a
// tab name that holds anything beyond letters, digits, and underscores (a
// space, punctuation, and so on). An inner single quote is doubled. The server
// echoes patchStatusRange straight to the sheet gateway, so it must be valid
// A1. Kept in sync with A1.QuoteTab on the backend.
function quoteTab_(name) {
  if (/^[A-Za-z0-9_]+$/.test(name)) return name;
  return "'" + String(name).replace(/'/g, "''") + "'";
}

function installTriggers() {
  removeTriggers();
  ScriptApp.newTrigger('runPatch').timeBased().everyMinutes(5).create();
  ScriptApp.newTrigger('runForcePatch').timeBased().everyHours(1).create();
  var msg = 'Triggers installed: runPatch every 5 minutes, runForcePatch hourly.';
  SpreadsheetApp.getActiveSpreadsheet().toast(msg, 'Xenaia availability patch');
}

function removeTriggers() {
  var triggers = ScriptApp.getProjectTriggers();
  for (var i = 0; i < triggers.length; i++) {
    var handler = triggers[i].getHandlerFunction();
    if (handler === 'runPatch' || handler === 'runForcePatch') {
      ScriptApp.deleteTrigger(triggers[i]);
    }
  }
}

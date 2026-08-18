/**
 * RevitAPP License - Apps Script web app with per-email device limits.
 *
 * Before deployment, add the REVITAPP_LICENSE_SHARED_SECRET value in
 * Project Settings > Script Properties. Never put the value in this file.
 *
 * POST { email, secret, machineId } returns
 * { allowed, expiry, email, error }.
 */

const SHARED_SECRET = PropertiesService.getScriptProperties()
  .getProperty('REVITAPP_LICENSE_SHARED_SECRET');
const SHEET_ID = '1E6h-FtzQz_-kTDWVuavVS5V4-OItdfve8ReYjYWragM';
const SHEET_LICENSES = 'Licenses';
const SHEET_DEVICES = 'Devices';
const DEFAULT_MAX_DEVICES = 1;

function doPost(e) {
  try {
    if (!SHARED_SECRET) {
      return json({ allowed: false, error: 'server_not_configured' });
    }

    const body = JSON.parse((e && e.postData && e.postData.contents) || '{}');
    if (body.secret !== SHARED_SECRET) {
      return json({ allowed: false, error: 'unauthorized' });
    }

    const email = String(body.email || '').trim().toLowerCase();
    if (!email) return json({ allowed: false, error: 'no_email' });
    const machineId = String(body.machineId || '').trim();

    const ss = SpreadsheetApp.openById(SHEET_ID);
    const licSheet = ss.getSheetByName(SHEET_LICENSES);
    if (!licSheet) return json({ allowed: false, error: 'sheet_missing' });

    const rows = licSheet.getDataRange().getValues();
    let found = null;
    for (let i = 1; i < rows.length; i++) {
      if (String(rows[i][0]).trim().toLowerCase() === email) {
        found = rows[i];
        break;
      }
    }
    if (!found) return json({ allowed: false, error: 'not_found', email: email });

    const expiryStr = toDateStr(found[1]);
    if (new Date(expiryStr + 'T23:59:59Z') < new Date()) {
      return json({ allowed: false, error: 'expired', expiry: expiryStr, email: email });
    }

    const maxDevices = parseInt(found[3], 10) || DEFAULT_MAX_DEVICES;
    if (machineId) {
      const lock = LockService.getScriptLock();
      lock.waitLock(10000);
      try {
        const devResult = checkDevice(ss, email, machineId, maxDevices);
        if (!devResult.ok) {
          return json({ allowed: false, error: 'device_limit', expiry: expiryStr, email: email });
        }
      } finally {
        lock.releaseLock();
      }
    }

    return json({ allowed: true, expiry: expiryStr, email: email });
  } catch (err) {
    return json({ allowed: false, error: 'exception', message: String(err) });
  }
}

function checkDevice(ss, email, machineId, maxDevices) {
  let sheet = ss.getSheetByName(SHEET_DEVICES);
  if (!sheet) {
    sheet = ss.insertSheet(SHEET_DEVICES);
    sheet.appendRow(['email', 'machineId', 'firstSeen', 'lastSeen']);
  }

  const now = Utilities.formatDate(
    new Date(), Session.getScriptTimeZone(), 'yyyy-MM-dd HH:mm:ss');
  const data = sheet.getDataRange().getValues();

  let countForEmail = 0;
  for (let i = 1; i < data.length; i++) {
    const rowEmail = String(data[i][0]).trim().toLowerCase();
    if (rowEmail !== email) continue;
    countForEmail++;
    if (String(data[i][1]).trim() === machineId) {
      sheet.getRange(i + 1, 4).setValue(now);
      return { ok: true };
    }
  }

  if (countForEmail >= maxDevices) return { ok: false };
  sheet.appendRow([email, machineId, now, now]);
  return { ok: true };
}

function doGet() {
  return json({
    ok: true,
    service: 'RevitAPP License',
    usage: 'POST { email, secret, machineId }'
  });
}

function toDateStr(value) {
  if (value instanceof Date) {
    return Utilities.formatDate(
      value, Session.getScriptTimeZone(), 'yyyy-MM-dd');
  }
  return String(value).trim().substring(0, 10);
}

function json(value) {
  return ContentService.createTextOutput(JSON.stringify(value))
    .setMimeType(ContentService.MimeType.JSON);
}

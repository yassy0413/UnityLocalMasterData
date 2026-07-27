const SCRIPT_PROPERTIES = PropertiesService.getScriptProperties();
const SPREADSHEET_ID = SCRIPT_PROPERTIES.getProperty("SPREADSHEET_ID");
const API_TOKEN = SCRIPT_PROPERTIES.getProperty("API_TOKEN");

function doGet(e) {
  try {
    if (!e || !e.parameter) {
      return textResponse("bad request");
    }

    if (e.parameter.token !== API_TOKEN) {
      return textResponse("unauthorized");
    }

    const gid = e.parameter.gid;

    if (!gid) {
      return textResponse("gid is required");
    }

    const ss = SpreadsheetApp.openById(SPREADSHEET_ID);

    const sheet = ss
      .getSheets()
      .find((s) => String(s.getSheetId()) === String(gid));

    if (!sheet) {
      return textResponse(`sheet not found. gid=${gid}`);
    }

    const values = sheet.getDataRange().getValues();
    const csv = values.map((row) => row.map(toCsvCell).join(",")).join("\n");

    return ContentService.createTextOutput(csv).setMimeType(
      ContentService.MimeType.CSV,
    );
  } catch (error) {
    return textResponse(`error: ${error.message}`);
  }
}

function toCsvCell(value) {
  if (value === null || value === undefined) {
    return "";
  }

  if (value instanceof Date) {
    value = Utilities.formatDate(
      value,
      Session.getScriptTimeZone(),
      "yyyy-MM-dd HH:mm:ss",
    );
  }

  const text = String(value);

  if (
    text.includes(",") ||
    text.includes('"') ||
    text.includes("\n") ||
    text.includes("\r")
  ) {
    return `"${text.replace(/"/g, '""')}"`;
  }

  return text;
}

function textResponse(text) {
  return ContentService.createTextOutput(text).setMimeType(
    ContentService.MimeType.TEXT,
  );
}

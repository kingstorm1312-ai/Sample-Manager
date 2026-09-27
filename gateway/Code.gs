var SPREADSHEET_ID = '1mliaTlEftFXI3mqDYUny134MutVqjFPZBqrNKL424aQ';
var REQUEST_SHEET = 'YEU_CAU_MAU';
var SAMPLE_SHEET = 'QUAN_LY_MAU';
var LOG_SHEET = 'MUTATION_LOG';

function doGet() {
  return json_({ ok: true, service: 'sample-manager-write-gateway' });
}

function doPost(event) {
  var logRow = null;
  var mutation = null;
  var timing = createTiming_();
  try {
    mutation = parseMutation_(event);
    requireSecret_(mutation.gatewaySecret);
    var lock = LockService.getScriptLock();
    var lockStarted = Date.now();
    if (!lock.tryLock(30000)) {
      throw gatewayError_('BUSY', 'Write gateway đang bận.');
    }
    timing.lockWaitMs = Date.now() - lockStarted;
    try {
      var lookupStarted = Date.now();
      var logged = findMutation_(mutation.operationId);
      addTiming_(timing, 'operationLookupMs', lookupStarted);
      if (logged) {
        if (logged.payloadHash !== mutation.payloadHash) {
          throw gatewayError_('OPERATION_MISMATCH', 'OperationId đã được dùng với payload khác.');
        }
        if (logged.status === 'COMPLETE') {
          return json_(completeResponse_(mutation, JSON.parse(logged.resultJson), timing));
        }
        logRow = logged.row;
        updateMutation_(logRow, mutation, 'PROCESSING', { resumed: true }, timing);
      } else {
        logRow = createMutation_(mutation, timing);
      }

      try {
        var result = runMutation_(mutation, timing);
        updateMutation_(logRow, mutation, 'COMPLETE', result, timing);
        return json_(completeResponse_(mutation, result, timing));
      } catch (operationError) {
        updateMutation_(logRow, mutation, 'FAILED', { message: operationError.message }, timing);
        throw operationError;
      }
    } finally {
      lock.releaseLock();
    }
  } catch (error) {
    return json_({
      ok: false,
      code: error.code || 'ERROR',
      message: error.message || String(error),
      operationId: mutation ? mutation.operationId : '',
      timing: timingSnapshot_(timing)
    });
  }
}

function createTiming_() {
  return {
    startedAt: Date.now(),
    lockWaitMs: 0,
    operationLookupMs: 0,
    recordReadMs: 0,
    validationMs: 0,
    commitMs: 0,
    mutationLogMs: 0
  };
}

function addTiming_(timing, field, startedAt) {
  if (timing) timing[field] += Math.max(0, Date.now() - startedAt);
}

function timingSnapshot_(timing) {
  if (!timing) return null;
  return {
    totalMs: Math.max(0, Date.now() - timing.startedAt),
    lockWaitMs: timing.lockWaitMs,
    operationLookupMs: timing.operationLookupMs,
    recordReadMs: timing.recordReadMs,
    validationMs: timing.validationMs,
    commitMs: timing.commitMs,
    mutationLogMs: timing.mutationLogMs
  };
}

function parseMutation_(event) {
  if (!event || !event.postData || !event.postData.contents) {
    throw gatewayError_('INVALID_REQUEST', 'Mutation request rỗng.');
  }
  var value;
  try {
    value = JSON.parse(event.postData.contents);
  } catch (error) {
    throw gatewayError_('INVALID_REQUEST', 'Mutation request không phải JSON.');
  }
  if (!value.operationId || !value.operationType || !value.payloadHash || !value.payload) {
    throw gatewayError_('INVALID_REQUEST', 'Mutation request thiếu field bắt buộc.');
  }
  return value;
}

function requireSecret_(secret) {
  var expected = PropertiesService.getScriptProperties().getProperty('GATEWAY_SECRET');
  if (!expected || secret !== expected) {
    throw gatewayError_('UNAUTHORIZED', 'Write gateway không được xác thực.');
  }
}

function runMutation_(mutation, timing) {
  switch (mutation.operationType) {
    case 'CREATE_REQUEST': return createRequest_(mutation, timing);
    case 'UPDATE_REQUEST': return updateRequest_(mutation, timing);
    case 'UPDATE_SAMPLE': return updateSample_(mutation, timing);
    case 'CLAIM_SAMPLE': return claimSample_(mutation, timing);
    case 'RELEASE_CLAIM': return releaseClaim_(mutation, timing);
    case 'DELETE_REQUEST': return deleteRequest_(mutation, timing);
    case 'DELETE_SAMPLE': return deleteSample_(mutation, timing);
    default: throw gatewayError_('INVALID_OPERATION', 'OperationType không được hỗ trợ.');
  }
}

function createRequest_(mutation, timing) {
  var payload = mutation.payload;
  var input = payload.request || {};
  var count = asPositiveInt_(payload.sampleCount);
  var requestSheet = sheet_(REQUEST_SHEET);
  var sampleSheet = sheet_(SAMPLE_SHEET);
  var requestRows = table_(requestSheet, timing);
  var sampleRows = table_(sampleSheet, timing);
  var requestId = asString_(input.YeuCau_ID);
  if (!requestId) requestId = 'YCM-' + mutation.operationId.replace(/-/g, '').substring(0, 24);
  var existingRequest = findTableRow_(requestRows, 'YeuCau_ID', requestId);
  var createdRequest = false;
  var createdSampleIds = [];
  var requestRecord;
  try {
    if (existingRequest) {
      assertRequestMatches_(existingRequest.record, input);
      requestRecord = existingRequest.record;
    } else {
      requestRecord = requestInput_(input, requestId);
      requestRecord.RowVersion = 1;
      appendRecord_(requestSheet, requestRows.headers, requestRecord, timing);
      createdRequest = true;
      requestRecord = findTableRow_(table_(requestSheet, timing), 'YeuCau_ID', requestId).record;
    }

    var version = normalizeVersion_(requestRecord.PhienBan);
    var contract = normalizeContract_(requestRecord.SoHopDong);
    var existingSamples = samplesForRequest_(sampleRows, requestId);
    var toAppend = [];
    for (var index = 1; index <= count; index++) {
      var expectedSampleId = 'RS-' + contract + '-' + version + '-' + index;
      var sameNumber = findSampleNumber_(existingSamples, index);
      if (sameNumber) {
        if (sameNumber.Sample_ID !== expectedSampleId) {
          throw gatewayError_('BUSINESS_RULE', 'STT mẫu đã tồn tại với Sample_ID khác.');
        }
        continue;
      }
      var duplicate = findTableRow_(sampleRows, 'Sample_ID', expectedSampleId);
      if (duplicate && duplicate.record.YeuCau_ID !== requestId) {
        throw gatewayError_('DUPLICATE_ID', 'Sample_ID đã tồn tại ở yêu cầu khác.');
      }
      var sample = {
        Mau_ID: 'MAU-' + Utilities.getUuid().replace(/-/g, '').toUpperCase(),
        YeuCau_ID: requestId,
        Sample_ID: expectedSampleId,
        PhienBan: version,
        STTMau: String(index),
        NgayDuyet: '',
        NgayHetHan: '',
        NguoiDuyet: '',
        NoiLuu: '',
        NgayGiaoMau: '',
        GhiChu: '',
        TrangThaiMay: 'Chờ may',
        RowVersion: 1,
        ClaimOwner: '',
        ClaimedAt: ''
      };
      toAppend.push(sample);
      createdSampleIds.push(sample.Mau_ID);
    }
    if (toAppend.length > 0) appendRecords_(sampleSheet, sampleRows.headers, toAppend, timing);

    requestRecord = findTableRow_(table_(requestSheet, timing), 'YeuCau_ID', requestId).record;
    sampleRows = table_(sampleSheet, timing);
    var finalSamples = samplesForRequest_(sampleRows, requestId);
    if (finalSamples.length !== count) {
      throw gatewayError_('INCOMPLETE_COMMIT', 'Không đủ mẫu sau commit.');
    }
    return { request: requestRecord, samples: finalSamples };
  } catch (error) {
    if (createdSampleIds.length > 0) {
      deleteRowsByValues_(sampleSheet, 'Mau_ID', createdSampleIds, timing);
    }
    if (createdRequest) {
      deleteRowsByValues_(requestSheet, 'YeuCau_ID', [requestId], timing);
    }
    throw error;
  }
}

function updateRequest_(mutation, timing) {
  var payload = mutation.payload;
  var current = findRequired_(sheet_(REQUEST_SHEET), 'YeuCau_ID', payload.recordId, timing);
  assertVersion_(current.record, payload.expectedVersion);
  var fields = payload.changedFields || {};
  var allowed = {
    SoHopDong: true, MaVatTu: true, TenTui: true, NoiYeuCau: true,
    SoLuongMau: true, PhienBan: true, Deadline: true, TrangThai: true, GhiChu: true
  };
  rejectUnknownFields_(fields, allowed);
  var sampleRows = samplesForRequest_(table_(sheet_(SAMPLE_SHEET), timing), payload.recordId);
  if (sampleRows.length > 0
      && (hasField_(fields, 'SoHopDong') || hasField_(fields, 'MaVatTu')
          || hasField_(fields, 'SoLuongMau') || hasField_(fields, 'PhienBan'))) {
    throw gatewayError_('BUSINESS_RULE', 'Yêu cầu đã có mẫu nên dữ liệu định danh bị khóa.');
  }
  if (Object.keys(fields).length === 0) return { request: current.record };
  var sheet = sheet_(REQUEST_SHEET);
  var headers = table_(sheet, timing).headers;
  var next = copy_(current.record);
  applyFields_(next, fields);
  next.RowVersion = version_(current.record.RowVersion) + 1;
  writeRecord_(sheet, headers, current.row, next, timing);
  return { request: next };
}

function updateSample_(mutation, timing) {
  var payload = mutation.payload;
  var sampleSheet = sheet_(SAMPLE_SHEET);
  var current = findRequired_(sampleSheet, 'Mau_ID', payload.recordId, timing);
  var validationStarted = Date.now();
  assertVersion_(current.record, payload.expectedVersion);
  if (current.record.ClaimOwner !== payload.clientId) {
    throw gatewayError_(current.record.ClaimOwner ? 'CLAIMED' : 'NOT_CLAIMED', current.record.ClaimOwner
      ? 'Mẫu này đang được xử lý ở máy khác.'
      : 'Hãy nhận mẫu trước khi lưu.');
  }
  var fields = payload.changedFields || {};
  var allowed = {
    NgayDuyet: true, NgayHetHan: true, NguoiDuyet: true, NoiLuu: true,
    NgayGiaoMau: true, GhiChu: true, TrangThaiMay: true, PhienBan: true, Sample_ID: true
  };
  rejectUnknownFields_(fields, allowed);
  var next = copy_(current.record);
  applyFields_(next, fields);
  var currentVersion = normalizeVersion_(current.record.PhienBan);
  var nextVersion = normalizeVersion_(next.PhienBan);
  if (currentVersion !== nextVersion) {
      if (currentVersion !== normalizeVersion_(requestForSample_(current.record, timing).PhienBan)) {
      throw gatewayError_('BUSINESS_RULE', 'Phiên bản đã được xác nhận; không thể đổi lại.');
    }
    var request = requestForSample_(current.record, timing);
    var expectedId = 'RS-' + normalizeContract_(request.SoHopDong) + '-' + nextVersion + '-' + current.record.STTMau;
    if (next.Sample_ID !== expectedId) throw gatewayError_('BUSINESS_RULE', 'Sample_ID không khớp phiên bản mới.');
    var duplicate = findTableRow_(table_(sampleSheet, timing), 'Sample_ID', expectedId);
    if (duplicate && duplicate.row !== current.row) throw gatewayError_('DUPLICATE_ID', 'Sample_ID mới đã tồn tại.');
  }
  if (next.TrangThaiMay === 'Đã may') {
    next.ClaimOwner = '';
    next.ClaimedAt = '';
  }
  addTiming_(timing, 'validationMs', validationStarted);
  if (Object.keys(fields).length === 0) return { sample: current.record };
  next.RowVersion = version_(current.record.RowVersion) + 1;
  writeRecord_(sampleSheet, current.headers, current.row, next, timing);
  return { sample: next };
}

function claimSample_(mutation, timing) {
  var payload = mutation.payload;
  var sampleSheet = sheet_(SAMPLE_SHEET);
  var current = findRequired_(sampleSheet, 'Mau_ID', payload.recordId, timing);
  var validationStarted = Date.now();
  assertVersion_(current.record, payload.expectedVersion);
  if (normalizeSampleStatus_(current.record.TrangThaiMay) !== 'Chờ may') {
    throw gatewayError_('BUSINESS_RULE', 'Chỉ nhận được mẫu đang Chờ may.');
  }
  if (current.record.ClaimOwner) {
    if (current.record.ClaimOwner === payload.clientId) return { sample: current.record };
    throw gatewayError_('CLAIMED', 'Mẫu này đang được xử lý ở máy khác.');
  }
  var next = copy_(current.record);
  next.ClaimOwner = payload.clientId;
  next.ClaimedAt = new Date().toISOString();
  next.RowVersion = version_(current.record.RowVersion) + 1;
  addTiming_(timing, 'validationMs', validationStarted);
  writeRecord_(sampleSheet, current.headers, current.row, next, timing);
  return { sample: next };
}

function releaseClaim_(mutation, timing) {
  var payload = mutation.payload;
  var sampleSheet = sheet_(SAMPLE_SHEET);
  var current = findRequired_(sampleSheet, 'Mau_ID', payload.recordId, timing);
  assertVersion_(current.record, payload.expectedVersion);
  if (current.record.ClaimOwner !== payload.clientId) {
    throw gatewayError_('CLAIMED', 'Chỉ máy đang nhận mẫu mới được trả lại.');
  }
  var next = copy_(current.record);
  next.ClaimOwner = '';
  next.ClaimedAt = '';
  next.RowVersion = version_(current.record.RowVersion) + 1;
  writeRecord_(sampleSheet, current.headers, current.row, next, timing);
  return { sample: next };
}

function deleteRequest_(mutation, timing) {
  var requestId = mutation.payload.recordId;
  var requestSheet = sheet_(REQUEST_SHEET);
  var sampleSheet = sheet_(SAMPLE_SHEET);
  findRequired_(requestSheet, 'YeuCau_ID', requestId, timing);
  deleteRowsByValues_(sampleSheet, 'YeuCau_ID', [requestId], timing);
  deleteRowsByValues_(requestSheet, 'YeuCau_ID', [requestId], timing);
  return { deletedRequestId: requestId };
}

function deleteSample_(mutation, timing) {
  var sampleId = mutation.payload.recordId;
  findRequired_(sheet_(SAMPLE_SHEET), 'Mau_ID', sampleId, timing);
  deleteRowsByValues_(sheet_(SAMPLE_SHEET), 'Mau_ID', [sampleId], timing);
  return { deletedSampleId: sampleId };
}

function requestInput_(input, requestId) {
  return {
    YeuCau_ID: requestId,
    NgayTaoYeuCau: asString_(input.NgayTaoYeuCau),
    SoHopDong: normalizeContract_(input.SoHopDong),
    MaVatTu: asString_(input.MaVatTu).replace(/\s/g, '').toUpperCase(),
    TenTui: asString_(input.TenTui),
    QA_ID: asString_(input.QA_ID),
    NoiYeuCau: asString_(input.NoiYeuCau),
    SoLuongMau: asString_(input.SoLuongMau),
    PhienBan: normalizeVersion_(input.PhienBan),
    Deadline: asString_(input.Deadline),
    TrangThai: asString_(input.TrangThai) || 'Mới',
    GhiChu: asString_(input.GhiChu),
    RowVersion: 1
  };
}

function assertRequestMatches_(existing, input) {
  var keys = ['SoHopDong', 'MaVatTu', 'TenTui', 'QA_ID', 'NoiYeuCau', 'SoLuongMau', 'PhienBan', 'Deadline', 'TrangThai', 'GhiChu'];
  for (var index = 0; index < keys.length; index++) {
    var key = keys[index];
    var incoming = asString_(input[key]);
    if (key === 'SoHopDong') incoming = normalizeContract_(incoming);
    if (key === 'MaVatTu') incoming = incoming.replace(/\s/g, '').toUpperCase();
    if (key === 'PhienBan') incoming = normalizeVersion_(incoming);
    var current = asString_(existing[key]);
    if (key === 'PhienBan') current = normalizeVersion_(current);
    if (incoming !== current) throw gatewayError_('OPERATION_MISMATCH', 'Yêu cầu đã tồn tại với dữ liệu khác.');
  }
}

function requestForSample_(sample, timing) {
  var row = findRequired_(sheet_(REQUEST_SHEET), 'YeuCau_ID', sample.YeuCau_ID, timing);
  return row.record;
}

function samplesForRequest_(table, requestId) {
  var result = [];
  for (var index = 0; index < table.records.length; index++) {
    if (table.records[index].YeuCau_ID === requestId) result.push(table.records[index]);
  }
  result.sort(function(a, b) { return Number(a.STTMau || 0) - Number(b.STTMau || 0); });
  return result;
}

function findSampleNumber_(samples, number) {
  var expected = String(number);
  for (var index = 0; index < samples.length; index++) {
    if (String(samples[index].STTMau) === expected) return samples[index];
  }
  return null;
}

function table_(sheet, timing) {
  var startedAt = Date.now();
  var lastColumn = Math.max(sheet.getLastColumn(), 1);
  var headers = sheet.getRange(1, 1, 1, lastColumn).getDisplayValues()[0];
  var lastRow = sheet.getLastRow();
  var values = lastRow > 1 ? sheet.getRange(2, 1, lastRow - 1, lastColumn).getDisplayValues() : [];
  var records = [];
  for (var rowIndex = 0; rowIndex < values.length; rowIndex++) {
    var record = record_(headers, values[rowIndex]);
    if (Object.keys(record).length > 0 && record[headers[0]]) {
      records.push(record);
    }
  }
  addTiming_(timing, 'recordReadMs', startedAt);
  return { sheet: sheet, headers: headers, records: records };
}

function record_(headers, row) {
  var record = {};
  for (var index = 0; index < headers.length; index++) {
    if (headers[index]) record[headers[index]] = asString_(row[index]);
  }
  if (record.RowVersion === '') record.RowVersion = 0;
  return record;
}

function findTableRow_(table, key, value) {
  for (var index = 0; index < table.records.length; index++) {
    if (asString_(table.records[index][key]) === asString_(value)) {
      return { row: index + 2, record: table.records[index], headers: table.headers };
    }
  }
  return null;
}

function findRequired_(sheet, key, value, timing) {
  var startedAt = Date.now();
  var lastColumn = Math.max(sheet.getLastColumn(), 1);
  var headers = sheet.getRange(1, 1, 1, lastColumn).getDisplayValues()[0];
  var keyColumn = -1;
  for (var headerIndex = 0; headerIndex < headers.length; headerIndex++) {
    if (headers[headerIndex] === key) {
      keyColumn = headerIndex;
      break;
    }
  }
  if (keyColumn < 0) {
    addTiming_(timing, 'recordReadMs', startedAt);
    throw gatewayError_('CONFIG_ERROR', 'Thiếu field ' + key + '.');
  }

  var lastRow = sheet.getLastRow();
  var rowNumber = -1;
  if (lastRow > 1) {
    var keyValues = sheet.getRange(2, keyColumn + 1, lastRow - 1, 1).getDisplayValues();
    var expected = asString_(value);
    for (var rowIndex = 0; rowIndex < keyValues.length; rowIndex++) {
      if (asString_(keyValues[rowIndex][0]) === expected) {
        rowNumber = rowIndex + 2;
        break;
      }
    }
  }
  if (rowNumber < 0) {
    addTiming_(timing, 'recordReadMs', startedAt);
    throw gatewayError_('NOT_FOUND', 'Không tìm thấy record theo ' + key + '.');
  }

  var values = sheet.getRange(rowNumber, 1, 1, lastColumn).getDisplayValues()[0];
  addTiming_(timing, 'recordReadMs', startedAt);
  return { row: rowNumber, record: record_(headers, values), headers: headers };
}

function appendRecord_(sheet, headers, record, timing) {
  appendRecords_(sheet, headers, [record], timing);
}

function appendRecords_(sheet, headers, records, timing) {
  var startedAt = Date.now();
  var values = [];
  for (var rowIndex = 0; rowIndex < records.length; rowIndex++) {
    var row = [];
    for (var column = 0; column < headers.length; column++) {
      row.push(records[rowIndex][headers[column]] === undefined ? '' : records[rowIndex][headers[column]]);
    }
    values.push(row);
  }
  sheet.getRange(sheet.getLastRow() + 1, 1, values.length, headers.length).setValues(values);
  addTiming_(timing, 'commitMs', startedAt);
}

function writeRecord_(sheet, headers, rowNumber, record, timing) {
  var startedAt = Date.now();
  var current = sheet.getRange(rowNumber, 1, 1, headers.length).getValues()[0];
  for (var column = 0; column < headers.length; column++) {
    if (record[headers[column]] !== undefined) current[column] = record[headers[column]];
  }
  sheet.getRange(rowNumber, 1, 1, headers.length).setValues([current]);
  addTiming_(timing, 'commitMs', startedAt);
}

function deleteRowsByValues_(sheet, key, values, timing) {
  var allowed = {};
  for (var index = 0; index < values.length; index++) allowed[values[index]] = true;
  var data = table_(sheet, timing);
  var startedAt = Date.now();
  var rows = [];
  for (var rowIndex = 0; rowIndex < data.records.length; rowIndex++) {
    if (allowed[data.records[rowIndex][key]]) rows.push(rowIndex + 2);
  }
  rows.sort(function(a, b) { return b - a; });
  for (var deleteIndex = 0; deleteIndex < rows.length; deleteIndex++) {
    sheet.deleteRow(rows[deleteIndex]);
  }
  addTiming_(timing, 'commitMs', startedAt);
}

function applyFields_(record, fields) {
  Object.keys(fields).forEach(function(key) { record[key] = asString_(fields[key]); });
}

function rejectUnknownFields_(fields, allowed) {
  Object.keys(fields).forEach(function(key) {
    if (!allowed[key]) throw gatewayError_('INVALID_FIELD', 'Field mutation không được phép: ' + key);
  });
}

function hasField_(fields, key) { return Object.prototype.hasOwnProperty.call(fields, key); }

function assertVersion_(record, expected) {
  if (version_(record.RowVersion) !== version_(expected)) {
    throw gatewayError_('CONFLICT', 'Dữ liệu vừa được cập nhật ở máy khác. Vui lòng tải lại.');
  }
}

function version_(value) {
  var number = Number(value || 0);
  return isFinite(number) && number >= 0 ? Math.floor(number) : 0;
}

function asPositiveInt_(value) {
  var number = Number(value);
  if (!isFinite(number) || number < 1 || Math.floor(number) !== number) {
    throw gatewayError_('INVALID_DATA', 'Số lượng mẫu không hợp lệ.');
  }
  return number;
}

function normalizeContract_(value) {
  var raw = asString_(value).toUpperCase().replace(/^\s+|\s+$/g, '');
  var match = raw.match(/^(\d{1,4})\s*\/\s*(\d{4}|\d{2})(.*)$/);
  if (!match) return raw;
  var year = match[2].length === 2 ? '20' + match[2] : match[2];
  return ('0000' + match[1]).slice(-4) + '/' + year + match[3].replace(/^\s+|\s+$/g, '');
}

function normalizeVersion_(value) {
  var raw = asString_(value).toUpperCase();
  if (!raw) return 'V1';
  var match = raw.match(/^V(\d+)$/);
  if (!match || Number(match[1]) < 1) throw gatewayError_('INVALID_DATA', 'Phiên bản không hợp lệ.');
  return 'V' + Number(match[1]);
}

function normalizeSampleStatus_(value) {
  var raw = asString_(value);
  if (raw === 'Đã may') return raw;
  if (raw === 'Đang chờ NPL') return raw;
  if (raw === 'Đang chờ nhãn xanh') return raw;
  return 'Chờ may';
}

function copy_(source) {
  var result = {};
  Object.keys(source).forEach(function(key) { result[key] = source[key]; });
  return result;
}

function sheet_(name) {
  var sheet = SpreadsheetApp.openById(SPREADSHEET_ID).getSheetByName(name);
  if (!sheet) throw gatewayError_('CONFIG_ERROR', 'Thiếu sheet ' + name + '.');
  return sheet;
}

function parseMutationLog_() {
  return table_(sheet_(LOG_SHEET));
}

function findMutation_(operationId) {
  var table = parseMutationLog_();
  for (var index = 0; index < table.records.length; index++) {
    if (table.records[index].OperationId === operationId) {
      return { row: index + 2, payloadHash: table.records[index].PayloadHash, status: table.records[index].Status, resultJson: table.records[index].ResultJson };
    }
  }
  return null;
}

function createMutation_(mutation, timing) {
  var sheet = sheet_(LOG_SHEET);
  var startedAt = Date.now();
  sheet.appendRow([mutation.operationId, mutation.payloadHash, mutation.operationType, 'PROCESSING', '', new Date().toISOString()]);
  var row = sheet.getLastRow();
  addTiming_(timing, 'mutationLogMs', startedAt);
  return row;
}

function updateMutation_(row, mutation, status, result, timing) {
  var startedAt = Date.now();
  sheet_(LOG_SHEET).getRange(row, 1, 1, 6).setValues([[
    mutation.operationId,
    mutation.payloadHash,
    mutation.operationType,
    status,
    JSON.stringify(result),
    new Date().toISOString()
  ]]);
  addTiming_(timing, 'mutationLogMs', startedAt);
}

function completeResponse_(mutation, result, timing) {
  return {
    ok: true,
    operationId: mutation.operationId,
    operationType: mutation.operationType,
    result: result,
    timing: timingSnapshot_(timing)
  };
}

function json_(value) {
  return ContentService.createTextOutput(JSON.stringify(value)).setMimeType(ContentService.MimeType.JSON);
}

function gatewayError_(code, message) {
  var error = new Error(message);
  error.code = code;
  return error;
}

function asString_(value) { return value === undefined || value === null ? '' : String(value).trim(); }

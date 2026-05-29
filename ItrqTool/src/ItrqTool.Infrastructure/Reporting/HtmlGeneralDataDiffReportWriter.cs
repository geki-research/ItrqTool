using System.Text;
using System.Text.Json;
using ItrqTool.Domain.Reporting;

namespace ItrqTool.Infrastructure.Reporting;

public sealed class HtmlGeneralDataDiffReportWriter : IHtmlGeneralDataDiffReportWriter
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public void WriteReport(HtmlDiffGeneralDataReportData data, string filePath)
    {
        var dir = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        var json = JsonSerializer.Serialize(data, JsonOpts);

        var html = BuildHtml(data, json);
        File.WriteAllText(filePath, html, Encoding.UTF8);
    }

    private static string BuildHtml(HtmlDiffGeneralDataReportData data, string embeddedJson)
    {
        var prevFile = Path.GetFileName(data.PreviousWorkbookPath);
        var currFile = Path.GetFileName(data.CurrentWorkbookPath);
        var generated = data.GeneratedAt.ToString("yyyy-MM-dd HH:mm:ss zzz");
        var title = HtmlEncode(data.Title);

        return $$"""
<!DOCTYPE html>
<html lang="en">
<head>
<meta charset="UTF-8"/>
<meta name="viewport" content="width=device-width, initial-scale=1.0"/>
<title>{{title}}</title>
<style>
*, *::before, *::after { box-sizing: border-box; margin: 0; padding: 0; }
body {
  font-family: -apple-system, BlinkMacSystemFont, "Segoe UI", sans-serif;
  font-size: 14px;
  color: #1e293b;
  background: #f8fafc;
  padding: 24px;
}
h1 { font-size: 22px; font-weight: 700; margin-bottom: 4px; }
.meta { color: #64748b; font-size: 13px; margin-bottom: 6px; }
.meta span { display: block; }

/* Summary cards */
.cards { display: flex; gap: 16px; margin: 20px 0; flex-wrap: wrap; }
.card {
  flex: 1; min-width: 140px; padding: 16px 20px;
  border-radius: 8px; border-left: 4px solid;
  background: #fff; box-shadow: 0 1px 3px rgba(0,0,0,.08);
}
.card .count { font-size: 28px; font-weight: 700; line-height: 1; }
.card .label { font-size: 12px; color: #64748b; margin-top: 4px; text-transform: uppercase; letter-spacing: .04em; }
.card-added     { border-color: #16a34a; } .card-added .count     { color: #16a34a; }
.card-removed   { border-color: #dc2626; } .card-removed .count   { color: #dc2626; }
.card-changed   { border-color: #d97706; } .card-changed .count   { color: #d97706; }
.card-unchanged { border-color: #9ca3af; background: #f3f4f6; } .card-unchanged .count { color: #374151; }

/* Search */
.search-bar { margin-bottom: 12px; }
.search-bar input {
  width: 100%; max-width: 400px;
  padding: 8px 12px; border: 1px solid #cbd5e1;
  border-radius: 6px; font-size: 14px;
  font-family: inherit;
  outline: none;
}
.search-bar input:focus { border-color: #6366f1; box-shadow: 0 0 0 3px rgba(99,102,241,.15); }

/* Tabs */
.tabs { display: flex; gap: 4px; border-bottom: 2px solid #e2e8f0; margin-bottom: 16px; }
.tab-btn {
  padding: 8px 16px; border: none; background: none;
  font-family: inherit; font-size: 14px; cursor: pointer;
  color: #64748b; border-bottom: 2px solid transparent;
  margin-bottom: -2px; border-radius: 4px 4px 0 0;
  transition: color .15s, border-color .15s;
}
.tab-btn:hover { color: #1e293b; }
.tab-btn.active { color: #4f46e5; border-bottom-color: #4f46e5; font-weight: 600; }
.tab-panel { display: none; }
.tab-panel.active { display: block; }

/* Tables */
.tbl-wrap { overflow-x: auto; }
table { width: 100%; border-collapse: collapse; }
thead tr { background: #f1f5f9; }
th {
  padding: 9px 12px; text-align: left;
  font-size: 12px; font-weight: 600; color: #475569;
  text-transform: uppercase; letter-spacing: .04em;
  border-bottom: 2px solid #e2e8f0; white-space: nowrap;
}
td { padding: 9px 12px; border-bottom: 1px solid #e2e8f0; vertical-align: top; }
tbody tr:nth-child(even) { background: #f8fafc; }
tbody tr:hover { background: #f1f5f9; }
.no-data { color: #94a3b8; font-style: italic; padding: 16px 0; }

/* Diff spans */
.diff-del { background: #fee2e2; color: #991b1b; text-decoration: line-through; border-radius: 2px; padding: 0 2px; }
.diff-add { background: #dcfce7; color: #166534; border-radius: 2px; padding: 0 2px; }

/* Similarity badges */
.sim-green { color: #16a34a; font-weight: 600; }
.sim-amber { color: #d97706; font-weight: 600; }
.sim-red   { color: #dc2626; font-weight: 600; }

.num-chg { color: #d97706; font-weight: 600; }
.em { color: #94a3b8; }
.cell-unchanged { color: #9ca3af; font-style: italic; }

/* Change badges */
.badge { display: inline-block; padding: 2px 6px; border-radius: 4px; font-size: 11px; font-weight: 600; margin-right: 2px; }
.badge-text      { background: #e0f2fe; color: #075985; }
.badge-num       { background: #fef3c7; color: #92400e; }
.badge-dv        { background: #f3e8ff; color: #6b21a8; }
.badge-cf        { background: #fce7f3; color: #9d174d; }
.badge-ambiguous { background: #fef9c3; color: #a16207; }

.sim-cell    { display: inline-flex; flex-direction: column; gap: 1px; }
.sim-primary { font-weight: 600; }
.sim-secondary { font-size: 11px; color: #94a3b8; }

.explanation-block { margin-top: 6px; padding: 4px 8px; border-left: 3px solid #e2e8f0; font-size: 12px; color: #475569; }
.explanation-label { font-weight: 600; color: #64748b; text-transform: uppercase; letter-spacing: .04em; font-size: 11px; margin-bottom: 2px; }
.explanation-diff  { display: flex; flex-direction: column; gap: 2px; }

/* Sheet-order tab styles */
.sheet-entry-row { cursor: pointer; }
.sheet-entry-row:hover { background: #eef2ff; }
.sheet-entry-row.expanded { background: #e0e7ff; }
.sheet-entry-detail { background: #f8fafc; }
.sheet-entry-detail > td { padding: 12px 16px; border-top: none; }
.sheet-separator-row > td {
  background: #e2e8f0; color: #475569;
  font-weight: 600; font-size: 12px;
  padding: 6px 12px; text-transform: uppercase; letter-spacing: .04em;
}
.sheet-separator-row.sheet-separator-chapter > td {
  background: #cbd5e1; color: #1e293b;
}

.status-badge {
  display: inline-block; padding: 2px 8px; border-radius: 10px;
  font-size: 11px; font-weight: 600; text-transform: uppercase; letter-spacing: .03em;
}
.status-badge-added     { background: #dcfce7; color: #166534; }
.status-badge-removed   { background: #fee2e2; color: #991b1b; }
.status-badge-changed   { background: #fef3c7; color: #92400e; }
.status-badge-unchanged { background: #e5e7eb; color: #374151; }

.entry-card { padding: 4px 0; }
.entry-card dl { display: grid; grid-template-columns: 140px 1fr; gap: 6px 14px; margin: 0; }
.entry-card dt { font-weight: 600; color: #475569; font-size: 11px; text-transform: uppercase; letter-spacing: .04em; padding-top: 2px; }
.entry-card dd { font-size: 13px; color: #1e293b; }

/* General Data per-cell rendering */
.cell-line { padding: 1px 0; font-size: 12px; }
.cell-line-exp { color: #475569; }
.cell-coord { display: inline-block; min-width: 38px; font-weight: 600; color: #6366f1; font-size: 11px; }
.cell-dv { color: #6b21a8; font-size: 11px; }
.cell-cf { color: #9d174d; font-size: 11px; }
.cell-diff { width: 100%; border-collapse: collapse; margin: 2px 0; font-size: 12px; }
.cell-diff th { padding: 4px 8px; font-size: 10px; background: #f1f5f9; }
.cell-diff td { padding: 4px 8px; border-bottom: 1px solid #eef2f7; vertical-align: top; }
.badge-cells { background: #ede9fe; color: #5b21b6; }
.badge-expl  { background: #ffe4e6; color: #9f1239; }
</style>
</head>
<body>
<h1>{{title}}</h1>
<div class="meta">
  <span>Previous: {{HtmlEncode(prevFile)}}</span>
  <span>Current: {{HtmlEncode(currFile)}}</span>
  <span>Generated: {{HtmlEncode(generated)}}</span>
</div>

<div class="cards">
  <div class="card card-added">
    <div class="count" id="cnt-added">{{data.Added.Count}}</div>
    <div class="label">Added</div>
  </div>
  <div class="card card-removed">
    <div class="count" id="cnt-removed">{{data.Removed.Count}}</div>
    <div class="label">Removed</div>
  </div>
  <div class="card card-changed">
    <div class="count" id="cnt-changed">{{data.Changed.Count}}</div>
    <div class="label">Changed</div>
  </div>
  <div class="card card-unchanged">
    <div class="count" id="cnt-unchanged">{{data.Unchanged.Count}}</div>
    <div class="label">Unchanged</div>
  </div>
</div>

<div class="search-bar">
  <input type="text" id="searchInput" placeholder="Filter visible rows…" oninput="applyFilter()"/>
</div>

<div class="tabs">
  <button class="tab-btn active" onclick="showTab('added')">Added</button>
  <button class="tab-btn" onclick="showTab('removed')">Removed</button>
  <button class="tab-btn" onclick="showTab('changed')">Changed</button>
  <button class="tab-btn" onclick="showTab('unchanged')">Unchanged</button>
  <button class="tab-btn" onclick="showTab('current-sheet')">Current sheet</button>
  <button class="tab-btn" onclick="showTab('previous-sheet')">Previous sheet</button>
</div>

<div id="tab-added" class="tab-panel active">
  <div class="tbl-wrap"><table id="tbl-added">
    <thead><tr><th>#</th><th>Number</th><th>Section</th><th>Row</th><th>Question Text</th><th>Template Cells</th></tr></thead>
    <tbody id="tbody-added"></tbody>
  </table></div>
</div>

<div id="tab-removed" class="tab-panel">
  <div class="tbl-wrap"><table id="tbl-removed">
    <thead><tr><th>#</th><th>Number</th><th>Section</th><th>Row</th><th>Question Text</th><th>Template Cells</th></tr></thead>
    <tbody id="tbody-removed"></tbody>
  </table></div>
</div>

<div id="tab-changed" class="tab-panel">
  <div class="tbl-wrap"><table id="tbl-changed">
    <thead><tr><th>#</th><th>Changes</th><th>Section</th><th>Prev №</th><th>Curr №</th><th>Old Text</th><th>New Text</th><th>Similarity</th><th>Cell Changes</th></tr></thead>
    <tbody id="tbody-changed"></tbody>
  </table></div>
</div>

<div id="tab-unchanged" class="tab-panel">
  <div class="tbl-wrap"><table id="tbl-unchanged">
    <thead><tr><th>#</th><th>Section</th><th>Prev Row</th><th>Curr Row</th><th>Number</th><th>Question Text</th><th>Similarity</th><th>Template Cells</th></tr></thead>
    <tbody id="tbody-unchanged"></tbody>
  </table></div>
</div>

<div id="tab-current-sheet" class="tab-panel">
  <div class="tbl-wrap"><table id="tbl-current-sheet">
    <thead><tr><th>Status</th><th>Row</th><th>Number</th><th>Question Text</th></tr></thead>
    <tbody id="tbody-current-sheet"></tbody>
  </table></div>
</div>

<div id="tab-previous-sheet" class="tab-panel">
  <div class="tbl-wrap"><table id="tbl-previous-sheet">
    <thead><tr><th>Status</th><th>Row</th><th>Number</th><th>Question Text</th></tr></thead>
    <tbody id="tbody-previous-sheet"></tbody>
  </table></div>
</div>

<script>
const REPORT_DATA = {{embeddedJson}};

// ── Tab switching ─────────────────────────────────────────────────────────────
let currentTab = 'added';
function showTab(name) {
  document.querySelectorAll('.tab-panel').forEach(p => p.classList.remove('active'));
  document.querySelectorAll('.tab-btn').forEach(b => b.classList.remove('active'));
  document.getElementById('tab-' + name).classList.add('active');
  const buttons = document.querySelectorAll('.tab-btn');
  const labels = ['added','removed','changed','unchanged','current-sheet','previous-sheet'];
  buttons[labels.indexOf(name)].classList.add('active');
  currentTab = name;
  applyFilter();
}

// ── Helpers ───────────────────────────────────────────────────────────────────
function esc(s) {
  if (!s) return '<span class="em">—</span>';
  return s.replace(/&/g,'&amp;').replace(/</g,'&lt;').replace(/>/g,'&gt;');
}

function escPlain(s) {
  if (s == null) return '';
  return s.replace(/&/g,'&amp;').replace(/</g,'&lt;').replace(/>/g,'&gt;');
}

// ── Word-level diff ───────────────────────────────────────────────────────────
function tokenize(text) { return text.split(/\s+/).filter(t => t.length > 0); }

function lcs(a, b) {
  const m = a.length, n = b.length;
  const dp = Array.from({length: m+1}, () => new Array(n+1).fill(0));
  for (let i = 1; i <= m; i++)
    for (let j = 1; j <= n; j++)
      dp[i][j] = a[i-1] === b[j-1] ? dp[i-1][j-1] + 1 : Math.max(dp[i-1][j], dp[i][j-1]);
  return dp;
}

function diff(oldTokens, newTokens) {
  const dp = lcs(oldTokens, newTokens);
  const result = {old: [], neu: []};
  let i = oldTokens.length, j = newTokens.length;
  while (i > 0 || j > 0) {
    if (i > 0 && j > 0 && oldTokens[i-1] === newTokens[j-1]) {
      result.old.unshift({type:'eq', text: oldTokens[i-1]});
      result.neu.unshift({type:'eq', text: newTokens[j-1]});
      i--; j--;
    } else if (j > 0 && (i === 0 || dp[i][j-1] >= dp[i-1][j])) {
      result.neu.unshift({type:'add', text: newTokens[j-1]});
      j--;
    } else {
      result.old.unshift({type:'del', text: oldTokens[i-1]});
      i--;
    }
  }
  return result;
}

function renderDiff(oldText, newText) {
  const oldTok = tokenize(oldText || '');
  const newTok = tokenize(newText || '');
  const d = diff(oldTok, newTok);
  const renderTokens = (tokens, delClass, addClass) =>
    tokens.map(t => {
      const s = escPlain(t.text);
      if (t.type === 'del') return '<span class="' + delClass + '">' + s + '</span>';
      if (t.type === 'add') return '<span class="' + addClass + '">' + s + '</span>';
      return s;
    }).join(' ');
  return {
    oldHtml: renderTokens(d.old, 'diff-del', 'diff-add'),
    newHtml: renderTokens(d.neu, 'diff-del', 'diff-add')
  };
}

function simClass(score) {
  if (score >= 0.80) return 'sim-green';
  if (score >= 0.60) return 'sim-amber';
  return 'sim-red';
}

function renderSimCell(score, secondBest) {
  const pct = Math.round(score * 100) + '%';
  const cls = simClass(score);
  let html = '<span class="sim-cell"><span class="sim-primary ' + cls + '">' + pct + '</span>';
  if (secondBest != null) {
    const secondPct = Math.round(secondBest * 100) + '%';
    html += '<span class="sim-secondary">' + secondPct + '</span>';
    if ((score - secondBest) < 0.10) {
      html += '<span class="badge badge-ambiguous">~</span>';
    }
  }
  html += '</span>';
  return html;
}

// ── Search / filter ───────────────────────────────────────────────────────────
function applyFilter() {
  const q = (document.getElementById('searchInput').value || '').toLowerCase().trim();
  const tbodyId = {
    added:            'tbody-added',
    removed:          'tbody-removed',
    changed:          'tbody-changed',
    unchanged:        'tbody-unchanged',
    'current-sheet':  'tbody-current-sheet',
    'previous-sheet': 'tbody-previous-sheet'
  }[currentTab];
  const tbody = document.getElementById(tbodyId);
  if (!tbody) return;
  Array.from(tbody.rows).forEach(row => {
    // Detail rows: visibility is managed in lockstep with their parent entry row.
    if (row.classList.contains('sheet-entry-detail')) return;
    // Colspan-only rows (separators, no-data placeholders): always visible.
    if (row.cells.length === 1 && row.cells[0].colSpan > 1) { row.style.display = ''; return; }
    // Regular data row: filter by concatenated text.
    const text = Array.from(row.cells).map(c => c.textContent).join(' ').toLowerCase();
    const visible = !q || text.includes(q);
    row.style.display = visible ? '' : 'none';
    // For sheet entry rows, sync the corresponding detail row.
    if (row.classList.contains('sheet-entry-row')) {
      const detailId = row.getAttribute('data-detail-id');
      if (detailId) {
        const detail = document.getElementById(detailId);
        if (detail) {
          detail.style.display = (!visible) ? 'none' : (row.classList.contains('expanded') ? '' : 'none');
        }
      }
    }
  });
}

function toggleDetail(rowId, detailId) {
  const row = document.getElementById(rowId);
  const detail = document.getElementById(detailId);
  if (!row || !detail) return;
  const expanded = row.classList.toggle('expanded');
  detail.style.display = expanded ? '' : 'none';
}

// ── Cell rendering (compact list for Added/Removed/Unchanged) ──────────────────
function renderCellList(answerCells, explanationCells) {
  const parts = [];
  (answerCells || []).forEach(c => {
    let s = '<div class="cell-line"><span class="cell-coord">' + escPlain(c.column) + '[' + c.rowOffset + ']</span> ' + escPlain(c.text);
    if (c.dvDisplay && c.dvDisplay !== '—') s += ' <span class="cell-dv">' + escPlain(c.dvDisplay) + '</span>';
    if (c.cfDisplay && c.cfDisplay !== '—') s += ' <span class="cell-cf">CF:' + escPlain(c.cfDisplay) + '</span>';
    s += '</div>';
    parts.push(s);
  });
  (explanationCells || []).forEach(c => {
    let s = '<div class="cell-line cell-line-exp"><span class="cell-coord">G[' + c.rowOffset + ']</span> ' + escPlain(c.text);
    if (c.dvDisplay && c.dvDisplay !== '—') s += ' <span class="cell-dv">' + escPlain(c.dvDisplay) + '</span>';
    if (c.cfDisplay && c.cfDisplay !== '—') s += ' <span class="cell-cf">CF:' + escPlain(c.cfDisplay) + '</span>';
    s += '</div>';
    parts.push(s);
  });
  return parts.length ? parts.join('') : '<span class="em">—</span>';
}

// ── Per-cell change rendering (for Changed tab) ────────────────────────────────
function renderCellChangeRow(coord, ch) {
  let coordCell = escPlain(coord);
  let labelCell;
  if (ch.oldText == null) {
    coordCell += ' <span class="status-badge status-badge-added">new</span>';
    labelCell = '<span class="diff-add">' + escPlain(ch.newText) + '</span>';
  } else if (ch.newText == null) {
    coordCell += ' <span class="status-badge status-badge-removed">gone</span>';
    labelCell = '<span class="diff-del">' + escPlain(ch.oldText) + '</span>';
  } else if (ch.textChanged) {
    const d = renderDiff(ch.oldText, ch.newText);
    labelCell = d.oldHtml + ' → ' + d.newHtml;
  } else {
    labelCell = '<span class="cell-unchanged">' + escPlain(ch.oldText) + '</span>';
  }
  const dvCell = ch.dvChanged ? (esc(ch.oldDvDisplay) + ' → ' + esc(ch.newDvDisplay)) : '<span class="cell-unchanged">—</span>';
  const cfCell = ch.cfChanged ? (esc(ch.oldCfDisplay) + ' → ' + esc(ch.newCfDisplay)) : '<span class="cell-unchanged">—</span>';
  return '<tr><td>' + coordCell + '</td><td>' + labelCell + '</td><td>' + dvCell + '</td><td>' + cfCell + '</td></tr>';
}

function renderCellChanges(answerChanges, explanationChanges) {
  const rows = [];
  (answerChanges || []).forEach(ch => rows.push(renderCellChangeRow(ch.column + '[' + ch.rowOffset + ']', ch)));
  (explanationChanges || []).forEach(ch => rows.push(renderCellChangeRow('G[' + ch.rowOffset + ']', ch)));
  if (!rows.length) return '<span class="cell-unchanged">no cell changes</span>';
  return '<table class="cell-diff"><thead><tr><th>Cell</th><th>Label (old → new)</th><th>DV</th><th>CF</th></tr></thead><tbody>' +
    rows.join('') + '</tbody></table>';
}

// ── Tables ─────────────────────────────────────────────────────────────────────
function renderAdded() {
  const tbody = document.getElementById('tbody-added');
  if (!REPORT_DATA.added.length) { tbody.innerHTML = '<tr><td colspan="6" class="no-data">No added questions.</td></tr>'; return; }
  tbody.innerHTML = REPORT_DATA.added.map((q, i) =>
    '<tr>' +
    '<td>' + (i+1) + '</td>' +
    '<td>' + esc(q.questionNumber) + '</td>' +
    '<td>' + esc(q.section) + '</td>' +
    '<td>' + q.rowNumber + '</td>' +
    '<td>' + esc(q.questionText) + '</td>' +
    '<td>' + renderCellList(q.answerCells, q.explanationCells) + '</td>' +
    '</tr>'
  ).join('');
}

function renderRemoved() {
  const tbody = document.getElementById('tbody-removed');
  if (!REPORT_DATA.removed.length) { tbody.innerHTML = '<tr><td colspan="6" class="no-data">No removed questions.</td></tr>'; return; }
  tbody.innerHTML = REPORT_DATA.removed.map((q, i) =>
    '<tr>' +
    '<td>' + (i+1) + '</td>' +
    '<td>' + esc(q.questionNumber) + '</td>' +
    '<td>' + esc(q.section) + '</td>' +
    '<td>' + q.rowNumber + '</td>' +
    '<td>' + esc(q.questionText) + '</td>' +
    '<td>' + renderCellList(q.answerCells, q.explanationCells) + '</td>' +
    '</tr>'
  ).join('');
}

function renderChanged() {
  const tbody = document.getElementById('tbody-changed');
  if (!REPORT_DATA.changed.length) { tbody.innerHTML = '<tr><td colspan="9" class="no-data">No changed questions.</td></tr>'; return; }
  tbody.innerHTML = REPORT_DATA.changed.map((c, i) => {
    const d = renderDiff(c.oldText, c.newText);
    const sim = renderSimCell(c.similarityScore, c.secondBestSimilarity);
    let badges = '';
    if (c.textChanged)             badges += '<span class="badge badge-text">T</span>';
    if (c.numberChanged)           badges += '<span class="badge badge-num">#</span>';
    if (c.answerCellsChanged)      badges += '<span class="badge badge-cells">cells</span>';
    if (c.explanationCellsChanged) badges += '<span class="badge badge-expl">expl</span>';
    return '<tr>' +
      '<td>' + (i+1) + '</td>' +
      '<td>' + (badges || '<span class="em">—</span>') + '</td>' +
      '<td>' + esc(c.section) + '</td>' +
      '<td>' + esc(c.previousNumber) + '</td>' +
      '<td>' + esc(c.currentNumber) + '</td>' +
      '<td>' + d.oldHtml + '</td>' +
      '<td>' + d.newHtml + '</td>' +
      '<td>' + sim + '</td>' +
      '<td>' + renderCellChanges(c.answerCellChanges, c.explanationCellChanges) + '</td>' +
      '</tr>';
  }).join('');
}

function renderUnchanged() {
  const tbody = document.getElementById('tbody-unchanged');
  if (!REPORT_DATA.unchanged.length) { tbody.innerHTML = '<tr><td colspan="8" class="no-data">No unchanged questions.</td></tr>'; return; }
  tbody.innerHTML = REPORT_DATA.unchanged.map((u, i) =>
    '<tr>' +
    '<td>' + (i+1) + '</td>' +
    '<td>' + esc(u.section) + '</td>' +
    '<td>' + u.previousRowNumber + '</td>' +
    '<td>' + u.currentRowNumber + '</td>' +
    '<td>' + esc(u.questionNumber) + '</td>' +
    '<td>' + esc(u.questionText) + '</td>' +
    '<td>' + renderSimCell(u.similarityScore, u.secondBestSimilarity) + '</td>' +
    '<td>' + renderCellList(u.answerCells, u.explanationCells) + '</td>' +
    '</tr>'
  ).join('');
}

// ── Sheet-order tabs (sections only; General Data has no chapters) ─────────────
function buildSheetEntries(side) {
  const entries = [];
  if (side === 'current') {
    REPORT_DATA.added.forEach(q => entries.push({
      status: 'added', rowNumber: q.rowNumber, section: q.section,
      questionNumber: q.questionNumber, questionText: q.questionText, q: q
    }));
    REPORT_DATA.changed.forEach(c => entries.push({
      status: 'changed', rowNumber: c.currentRowNumber, section: c.section,
      questionNumber: c.currentNumber, questionText: c.newText, q: c
    }));
    REPORT_DATA.unchanged.forEach(u => entries.push({
      status: 'unchanged', rowNumber: u.currentRowNumber, section: u.section,
      questionNumber: u.questionNumber, questionText: u.questionText, q: u
    }));
  } else {
    REPORT_DATA.removed.forEach(q => entries.push({
      status: 'removed', rowNumber: q.rowNumber, section: q.section,
      questionNumber: q.questionNumber, questionText: q.questionText, q: q
    }));
    REPORT_DATA.changed.forEach(c => entries.push({
      status: 'changed', rowNumber: c.previousRowNumber, section: c.section,
      questionNumber: c.previousNumber, questionText: c.oldText, q: c
    }));
    REPORT_DATA.unchanged.forEach(u => entries.push({
      status: 'unchanged', rowNumber: u.previousRowNumber, section: u.section,
      questionNumber: u.questionNumber, questionText: u.questionText, q: u
    }));
  }
  entries.sort((a, b) => a.rowNumber - b.rowNumber);
  return entries;
}

function renderAddedRemovedCard(q) {
  return '<div class="entry-card"><dl>' +
    '<dt>Number</dt><dd>' + esc(q.questionNumber) + '</dd>' +
    '<dt>Section</dt><dd>' + esc(q.section) + '</dd>' +
    '<dt>Text</dt><dd>' + esc(q.questionText) + '</dd>' +
    '<dt>Cells</dt><dd>' + renderCellList(q.answerCells, q.explanationCells) + '</dd>' +
    '</dl></div>';
}

function renderChangedCard(c) {
  const d = renderDiff(c.oldText, c.newText);
  const sim = renderSimCell(c.similarityScore, c.secondBestSimilarity);
  let badges = '';
  if (c.textChanged)             badges += '<span class="badge badge-text">T</span>';
  if (c.numberChanged)           badges += '<span class="badge badge-num">#</span>';
  if (c.answerCellsChanged)      badges += '<span class="badge badge-cells">cells</span>';
  if (c.explanationCellsChanged) badges += '<span class="badge badge-expl">expl</span>';
  return '<div class="entry-card"><dl>' +
    '<dt>Changes</dt><dd>' + (badges || '<span class="em">—</span>') + '</dd>' +
    '<dt>Section</dt><dd>' + esc(c.section) + '</dd>' +
    '<dt>Prev №</dt><dd>' + esc(c.previousNumber) + '</dd>' +
    '<dt>Curr №</dt><dd>' + esc(c.currentNumber) + '</dd>' +
    '<dt>Old Text</dt><dd>' + d.oldHtml + '</dd>' +
    '<dt>New Text</dt><dd>' + d.newHtml + '</dd>' +
    '<dt>Similarity</dt><dd>' + sim + '</dd>' +
    '<dt>Cell Changes</dt><dd>' + renderCellChanges(c.answerCellChanges, c.explanationCellChanges) + '</dd>' +
    '</dl></div>';
}

function renderUnchangedCard(u) {
  return '<div class="entry-card"><dl>' +
    '<dt>Number</dt><dd>' + esc(u.questionNumber) + '</dd>' +
    '<dt>Section</dt><dd>' + esc(u.section) + '</dd>' +
    '<dt>Text</dt><dd>' + esc(u.questionText) + '</dd>' +
    '<dt>Cells</dt><dd>' + renderCellList(u.answerCells, u.explanationCells) + '</dd>' +
    '<dt>Similarity</dt><dd>' + renderSimCell(u.similarityScore, u.secondBestSimilarity) + '</dd>' +
    '</dl></div>';
}

function renderEntryDetailCard(entry) {
  if (entry.status === 'added' || entry.status === 'removed') return renderAddedRemovedCard(entry.q);
  if (entry.status === 'changed')   return renderChangedCard(entry.q);
  if (entry.status === 'unchanged') return renderUnchangedCard(entry.q);
  return '';
}

function renderSheetTab(side) {
  const tbody = document.getElementById('tbody-' + side + '-sheet');
  const entries = buildSheetEntries(side);
  if (!entries.length) {
    tbody.innerHTML = '<tr><td colspan="4" class="no-data">No questions on this sheet.</td></tr>';
    return;
  }
  let html = '';
  let lastSection = null;
  let idCounter = 0;
  entries.forEach(entry => {
    if (entry.section !== lastSection) {
      html += '<tr class="sheet-separator-row"><td colspan="4">' + esc(entry.section) + '</td></tr>';
      lastSection = entry.section;
    }
    const rowId = side + '-row-' + idCounter;
    const detailId = side + '-detail-' + idCounter;
    idCounter++;
    const statusBadge = '<span class="status-badge status-badge-' + entry.status + '">' + entry.status + '</span>';
    html += '<tr id="' + rowId + '" class="sheet-entry-row" data-detail-id="' + detailId + '" onclick="toggleDetail(\'' + rowId + '\',\'' + detailId + '\')">' +
      '<td>' + statusBadge + '</td>' +
      '<td>' + entry.rowNumber + '</td>' +
      '<td>' + esc(entry.questionNumber) + '</td>' +
      '<td>' + esc(entry.questionText) + '</td>' +
      '</tr>';
    html += '<tr id="' + detailId + '" class="sheet-entry-detail" style="display:none"><td colspan="4">' + renderEntryDetailCard(entry) + '</td></tr>';
  });
  tbody.innerHTML = html;
}

// ── Init ────────────────────────────────────────────────────────────────────────
renderAdded();
renderRemoved();
renderChanged();
renderUnchanged();
renderSheetTab('current');
renderSheetTab('previous');
</script>
</body>
</html>
""";
    }

    private static string HtmlEncode(string s) =>
        s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");
}

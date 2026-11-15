const $ = selector => document.querySelector(selector);
const labels = { tasks: 'Tasks', planner: 'Daily plan', reports: 'Reports' };
const defaults = { date: '2026-10-13', minimum: 3, capacity: 180 };
const dates = new Intl.DateTimeFormat(undefined, { month: 'short', day: 'numeric', timeZone: 'UTC' });
const fullDates = new Intl.DateTimeFormat(undefined, { year: 'numeric', month: 'short', day: 'numeric', timeZone: 'UTC' });
const parseDay = day => new Date(`${day}T00:00:00Z`);
const shortDate = day => dates.format(parseDay(day));
const settingsForm = $('#settings-form');
const planForm = $('#plan-form');
const taskForm = $('#task-form');
const dialog = $('#task-dialog');
let tasks = [];
let filter = 'all';
let view = 'tasks';
let action;
let taskRevision = 0;
let reportRequest;
let planRequest;
let cachedReport;
let downloadUrl;
let settingsTimer;

function element(tag, className, text) {
  const item = document.createElement(tag);
  if (className) item.className = className;
  if (text !== undefined) item.textContent = text;
  return item;
}

async function request(url, { data, signal } = {}) {
  if (typeof globalThis.dailyPlannerBrowserRequest === 'function') {
    return globalThis.dailyPlannerBrowserRequest(url, { data, signal });
  }
  const options = { signal };
  if (data) {
    options.method = 'POST';
    options.headers = { 'Content-Type': 'application/json' };
    options.body = JSON.stringify(data);
  }
  const response = await fetch(url, options);
  const result = await response.json().catch(() => null);
  if (!response.ok) throw new Error(result?.error || `Request failed (${response.status}).`);
  return result;
}

function settings() {
  const fields = settingsForm.elements;
  return { date: fields.date.value, minimum: Number(fields.minimum.value), capacity: Number(fields.capacity.value) };
}

function loadSettings() {
  let saved = defaults;
  try { saved = JSON.parse(localStorage.getItem('dailyplanner-settings')) || defaults; } catch {}
  for (const [name, value] of Object.entries(defaults)) settingsForm.elements[name].value = saved[name] ?? value;
  if (!settingsForm.checkValidity()) {
    for (const [name, value] of Object.entries(defaults)) settingsForm.elements[name].value = value;
  }
}

function notice(text, error = false) {
  const banner = $('#notice');
  banner.classList.toggle('error', error);
  banner.hidden = !text;
  banner.textContent = text;
}

function navigate() {
  const target = location.hash.slice(1);
  view = Object.hasOwn(labels, target) ? target : 'tasks';
  $('#page-label').textContent = labels[view];
  for (const link of document.querySelectorAll('[data-view]')) {
    const active = link.dataset.view === view;
    $(`#${link.dataset.view}-view`).hidden = !active;
    if (active) link.setAttribute('aria-current', 'page');
    else link.removeAttribute('aria-current');
  }
  if (view !== 'planner') void refreshReports();
}

function taskButton(task, mode, label) {
  const button = element('button', 'row-action', label);
  button.type = 'button';
  button.setAttribute('aria-label', `${label} ${task.title}`);
  button.addEventListener('click', () => openTask(mode, task));
  return button;
}

function drawTasks() {
  const done = tasks.filter(task => task.completed).length;
  $('#task-summary').textContent = `${tasks.length - done} open · ${done} done`;
  const query = $('#task-search').value.trim().toLowerCase();
  const visible = tasks.filter(task =>
    (filter === 'all' || task.completed === (filter === 'done')) &&
    `${task.title} ${task.id}`.toLowerCase().includes(query));
  visible.sort((a, b) => Number(a.completed) - Number(b.completed) || a.date.localeCompare(b.date) || b.importance - a.importance || a.id.localeCompare(b.id));
  const rows = visible.map(task => {
    const row = element('tr', task.completed ? 'completed' : '');
    const title = element('td', 'task-title', task.title);
    title.append(element('small', '', task.id));
    const due = element('td', '', shortDate(task.date));
    due.title = fullDates.format(parseDay(task.date));
    const actions = element('td', 'num');
    if (task.completed) actions.append(element('span', 'status', 'Done'));
    else {
      const group = element('div', 'task-actions');
      if (task.date !== '9999-12-31') group.append(taskButton(task, 'reschedule', 'Move'));
      group.append(taskButton(task, 'complete', 'Complete'));
      actions.append(group);
    }
    row.append(title, due, element('td', task.importance >= 4 ? 'priority high' : 'priority', `${task.importance}/5`),
      element('td', 'num', `${task.minutes} min`), actions);
    return row;
  });
  if (!rows.length) {
    const row = element('tr');
    const cell = element('td', 'empty', tasks.length ? 'No matching tasks.' : 'No tasks yet.');
    cell.colSpan = 5;
    row.append(cell);
    rows.push(row);
  }
  $('#task-rows').replaceChildren(...rows);
}

function nextDay(day) {
  const next = parseDay(day);
  next.setUTCDate(next.getUTCDate() + 1);
  return next.toISOString().slice(0, 10);
}

function openTask(mode, task) {
  action = { mode, task };
  taskForm.reset();
  const add = mode === 'add';
  $('#task-fields').hidden = !add;
  $('#date-fields').hidden = add;
  for (const field of $('#task-fields').querySelectorAll('input, select')) field.disabled = !add;
  const date = taskForm.elements.actionDate;
  date.disabled = add;
  $('#dialog-title').textContent = { add: 'New task', complete: 'Complete task', reschedule: 'Move task' }[mode];
  $('#save-task').textContent = { add: 'Save task', complete: 'Mark complete', reschedule: 'Save date' }[mode];
  $('#task-error').textContent = '';
  if (add) taskForm.elements.date.value = settings().date || defaults.date;
  else {
    $('#task-context').textContent = `${task.title} · due ${fullDates.format(parseDay(task.date))}`;
    $('#action-date-label').textContent = mode === 'complete' ? 'Completion date' : 'New due date';
    const earliest = mode === 'complete' ? task.date : nextDay(task.date);
    date.min = earliest;
    date.value = settings().date > earliest ? settings().date : earliest;
  }
  dialog.showModal();
  (add ? taskForm.elements.title : date).focus();
}

async function saveTask(event) {
  event.preventDefault();
  const { mode, task } = action;
  const data = Object.fromEntries(new FormData(taskForm));
  let endpoint = '/api/tasks';
  let payload = { ...data, importance: Number(data.importance), minutes: Number(data.minutes) };
  if (mode === 'reschedule') {
    endpoint += '/reschedule';
    payload = { id: task.id, from: task.date, to: data.actionDate };
  } else if (mode === 'complete') {
    endpoint += '/complete';
    payload = { id: task.id, on: data.actionDate };
  }

  const controls = ['#save-task', '#cancel-task', '#close-dialog'].map($);
  controls.forEach(button => { button.disabled = true; });
  $('#task-error').textContent = '';
  try {
    tasks = await request(endpoint, { data: payload });
    taskRevision++;
    dialog.close();
    if (mode === 'add') $('#task-search').value = '';
    drawTasks();
    notice({ add: 'Task added.', reschedule: 'Task moved.', complete: 'Task completed.' }[mode]);
    clearPlan();
    void refreshReports();
  } catch (problem) {
    $('#task-error').textContent = problem.message;
  } finally {
    controls.forEach(button => { button.disabled = false; });
  }
}

function workload(target, rows) {
  if (!rows.length) {
    target.replaceChildren(element('p', 'empty', 'No open tasks in this window.'));
    return;
  }
  target.replaceChildren(...rows.map(row => {
    const item = element('div', 'workload-row');
    const heading = element('div', 'workload-label');
    const date = shortDate(row.date);
    heading.append(element('span', '', date), element('span', row.loadPercent > 100 ? 'overload' : '', `${row.loadPercent}%`));
    const progress = element('progress');
    progress.max = 100;
    progress.value = Math.min(row.loadPercent, 100);
    progress.setAttribute('aria-label', `${date}: ${row.loadPercent}% of the time budget`);
    item.append(heading, progress, element('p', 'workload-count', `${row.tasks} ${row.tasks === 1 ? 'task' : 'tasks'} · ${row.totalMinutes} min`));
    return item;
  }));
}

function ranking(rows, empty = 'No tasks meet the minimum priority on this date.') {
  if (!rows.length) return element('p', 'empty', empty);
  const list = element('ol', 'rank-list');
  for (const row of rows) {
    const item = element('li');
    const number = element('span', 'rank-position', row.position);
    number.setAttribute('aria-hidden', 'true');
    item.append(number, element('strong', '', row.title), element('small', '', `${row.minutes} min · ${row.score} pts`));
    list.append(item);
  }
  return list;
}

function reportStatus(text, isError = false) {
  const status = $('#report-status');
  status.hidden = !text;
  status.textContent = text;
  status.classList.toggle('form-error', isError);
}

function drawReports({ reports, csv }, current) {
  workload($('#overview-workload'), reports.workload);
  workload($('#report-workload'), reports.workload);
  $('#importance-results').replaceChildren(ranking(reports.importance));
  $('#short-results').replaceChildren(ranking(reports.shortTasks));
  $('#report-results').hidden = false;
  if (downloadUrl) URL.revokeObjectURL(downloadUrl);
  downloadUrl = URL.createObjectURL(new Blob([csv], { type: 'text/csv;charset=utf-8' }));
  $('#download').href = downloadUrl;
  $('#download').download = `dailyplanner-${current.date}.csv`;
  $('#download').hidden = false;
  reportStatus('');
}

async function refreshReports() {
  reportRequest?.abort();
  const controller = new AbortController();
  reportRequest = controller;
  const current = settings();
  if (!settingsForm.checkValidity()) {
    $('#download').hidden = true;
    $('#report-results').hidden = true;
    $('#overview-range').textContent = '';
    $('#overview-workload').replaceChildren(element('p', 'empty', 'Enter valid planning settings.'));
    reportStatus('Enter valid planning settings.', true);
    return;
  }
  $('#overview-range').textContent = `${shortDate(current.date)} to ${shortDate(nextDay(current.date))} · ${current.capacity} min/day`;
  const key = `${taskRevision}:${new URLSearchParams(current)}`;
  if (cachedReport?.key === key) {
    drawReports(cachedReport.report, current);
    return;
  }
  $('#download').hidden = true;
  $('#report-results').hidden = true;
  $('#overview-workload').replaceChildren(element('p', 'empty', 'Loading workload…'));
  reportStatus('Loading reports…');
  try {
    const report = await request(`/api/reports?${new URLSearchParams(current)}`, { signal: controller.signal });
    if (controller.signal.aborted) return;
    cachedReport = { key, report };
    drawReports(report, current);
  } catch (problem) {
    if (controller.signal.aborted) return;
    $('#overview-workload').replaceChildren(element('p', 'form-error', problem.message));
    reportStatus(problem.message, true);
  }
}

function clearPlan() {
  planRequest?.abort();
  $('#plan-error').textContent = '';
  $('#plan-result').replaceChildren(element('p', 'empty', 'No plan yet.'));
}

async function showPlan(event) {
  event.preventDefault();
  if (!settingsForm.reportValidity()) return;
  clearPlan();
  const controller = new AbortController();
  planRequest = controller;
  const button = planForm.querySelector('[type=submit]');
  button.disabled = true;
  try {
    const data = { ...settings(), policy: planForm.elements.policy.value, fit: planForm.elements.fit.checked };
    const plan = await request('/api/plan', { data, signal: controller.signal });
    if (controller.signal.aborted) return;
    const summary = element('div', 'plan-summary');
    const usage = plan.loadPercent === null ? '' : ` · ${plan.loadPercent}% used`;
    summary.append(element('strong', '', `${plan.plan.totalMinutes} min planned · ${plan.remaining} min free`),
      element('p', '', `${plan.reference}${usage}`));
    const result = $('#plan-result');
    const rows = list => list.map(row => ({ ...row.task, score: row.score, position: row.position }));
    result.replaceChildren(summary, ranking(rows(plan.plan.tasks)));
    if (plan.deferred.length) result.append(element('h3', 'deferred-title', 'Deferred'), ranking(rows(plan.deferred)));
  } catch (problem) {
    if (!controller.signal.aborted) {
      $('#plan-error').textContent = problem.message;
      $('#plan-result').replaceChildren();
    }
  } finally { button.disabled = false; }
}

settingsForm.addEventListener('submit', event => event.preventDefault());
settingsForm.addEventListener('input', () => {
  clearPlan();
  if (settingsForm.checkValidity()) {
    try { localStorage.setItem('dailyplanner-settings', JSON.stringify(settings())); } catch {}
  }
  reportRequest?.abort();
  $('#download').hidden = true;
  $('#report-results').hidden = true;
  clearTimeout(settingsTimer);
  settingsTimer = setTimeout(refreshReports, 150);
});
$('.segments').addEventListener('click', event => {
  const button = event.target.closest('[data-filter]');
  if (!button) return;
  filter = button.dataset.filter;
  for (const option of document.querySelectorAll('[data-filter]')) option.setAttribute('aria-pressed', String(option === button));
  drawTasks();
});
$('#task-search').addEventListener('input', drawTasks);
$('#new-task').addEventListener('click', () => openTask('add'));
$('#close-dialog').addEventListener('click', () => dialog.close());
$('#cancel-task').addEventListener('click', () => dialog.close());
dialog.addEventListener('cancel', event => { if ($('#save-task').disabled) event.preventDefault(); });
taskForm.addEventListener('submit', saveTask);
planForm.addEventListener('submit', showPlan);
planForm.addEventListener('change', clearPlan);
window.addEventListener('hashchange', navigate);

loadSettings();
navigate();
request('/api/tasks').then(data => { if (taskRevision === 0) { tasks = data; drawTasks(); } })
  .catch(error => notice(error.message, true));

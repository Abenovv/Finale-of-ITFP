(() => {
  const storageKey = 'dailyplanner-pages-tasks-v1';
  const ordinal = (a, b) => a < b ? -1 : a > b ? 1 : 0;
  const inRange = (value, min, max) => Number.isInteger(value) && value >= min && value <= max;
  const validDay = value => {
    if (!/^\d{4}-\d{2}-\d{2}$/.test(value) || value.startsWith('0000')) return false;
    const day = new Date(value + 'T00:00:00Z');
    return Number.isFinite(+day) && day.toISOString().slice(0, 10) === value;
  };
  const minutes = rows => rows.reduce((total, row) => total + row.task.minutes, 0);
  const percent = (total, capacity) => Math.ceil(total * 100 / capacity);
  let sampleRequest;

  async function tasks() {
    let saved;
    try {
      saved = JSON.parse(localStorage.getItem(storageKey));
      if (saved !== null && !Array.isArray(saved)) throw new Error();
    } catch {
      throw new Error('The browser could not read your saved tasks.');
    }
    if (saved !== null) return saved;
    sampleRequest ??= fetch('./tasks.json').then(response => {
      if (!response.ok) throw new Error('The sample tasks could not be loaded.');
      return response.json();
    }).catch(error => {
      sampleRequest = undefined;
      throw error;
    });
    return sampleRequest;
  }

  function store(rows) {
    try {
      localStorage.setItem(storageKey, JSON.stringify(rows));
    } catch {
      throw new Error('The browser could not save this change.');
    }
    return rows;
  }

  function changeTask(rows, path, data) {
    const id = data.id?.trim();
    if (!id) throw new Error('A task ID is required.');
    const found = rows.find(task => task.id === id);
    if (path === '/api/tasks') {
      const title = data.title?.trim();
      if (!title || !validDay(data.date)) throw new Error('Enter a task title and a valid date.');
      if (!inRange(data.importance, 1, 5) || !inRange(data.minutes, 0, 480)) {
        throw new Error('Enter a priority from 1 to 5 and a duration from 0 to 480 minutes.');
      }
      if (found) throw new Error(`Task '${id}' already exists.`);
      const { date, importance, minutes } = data;
      return store([...rows, { id, title, date, importance, minutes, completed: false }]);
    }
    if (!found) throw new Error(`Task '${id}' was not found.`);
    if (found.completed) throw new Error(`Task '${id}' is already complete.`);

    let update;
    if (path === '/api/tasks/reschedule') {
      if (found.date !== data.from) throw new Error(`Task '${id}' is now scheduled for ${found.date}.`);
      if (!validDay(data.to) || data.to <= found.date) {
        throw new Error('The new date must be later than the scheduled date.');
      }
      update = { date: data.to };
    } else {
      if (!validDay(data.on) || data.on < found.date) {
        throw new Error(`Task '${id}' cannot be completed before ${found.date}.`);
      }
      update = { completed: true };
    }
    return store(rows.map(task => task === found ? { ...task, ...update } : task));
  }

  function rank(rows, { date, minimum }, policy) {
    const window = policy === 'short' ? 60 : 0;
    return rows.filter(task => task.date === date && !task.completed && task.importance >= minimum)
      .map(task => ({ task, score: task.importance * 10 + Math.max(0, window - task.minutes) }))
      .sort((a, b) => b.score - a.score || ordinal(a.task.id, b.task.id))
      .map((row, index) => ({ ...row, position: index + 1 }));
  }

  function plan(rows, settings) {
    const { date, capacity, policy, fit } = settings;
    if (policy !== 'short' && policy !== 'importance') throw new Error('Choose a scoring policy.');
    const ranked = rank(rows, settings, policy);
    let selected = ranked;
    const deferred = [];
    if (fit) {
      selected = [];
      let remaining = capacity;
      for (const row of ranked) {
        if (row.task.minutes > remaining) deferred.push(row);
        else {
          remaining -= row.task.minutes;
          selected.push({ ...row, position: selected.length + 1 });
        }
      }
    }
    if (!selected.length) {
      throw new Error(fit ? 'No eligible task fits the available time.' : 'Select at least one task before confirming a plan.');
    }
    const totalMinutes = minutes(selected);
    if (totalMinutes > capacity) throw new Error('The selected tasks exceed the available time.');
    return {
      reference: `PLAN-${date.replaceAll('-', '')}`,
      plan: { date, tasks: selected, totalMinutes },
      remaining: capacity - totalMinutes,
      deferred,
      loadPercent: fit ? percent(totalMinutes, capacity) : null,
    };
  }

  function reports(rows, settings) {
    const { date, capacity } = settings;
    const focus = policy => rank(rows, settings, policy).map(({ task, score, position }) =>
      ({ id: task.id, title: task.title, minutes: task.minutes, score, position }));
    const tomorrow = new Date(date + 'T00:00:00Z');
    tomorrow.setUTCDate(tomorrow.getUTCDate() + 1);
    const workload = [date, tomorrow.toISOString().slice(0, 10)].flatMap(day => {
      const open = rows.filter(task => !task.completed && task.date === day);
      const totalMinutes = open.reduce((total, task) => total + task.minutes, 0);
      return open.length ? [{ date: day, tasks: open.length, totalMinutes, loadPercent: percent(totalMinutes, capacity) }] : [];
    }).sort((a, b) => b.totalMinutes - a.totalMinutes || ordinal(a.date, b.date));
    const importance = focus('importance');
    const shortTasks = focus('short');
    const lines = [['report', 'date', 'policy', 'position', 'id', 'title', 'tasks', 'minutes', 'score', 'load_percent']];
    for (const [policy, list] of [['importance', importance], ['short', shortTasks]]) {
      for (const row of list) lines.push(['focus', date, policy, row.position, row.id, row.title, 1, row.minutes, row.score, '']);
    }
    for (const row of workload) {
      lines.push(['workload', row.date, '', '', '', '', row.tasks, row.totalMinutes, '', row.loadPercent]);
    }
    const cell = value => /[",\r\n]/.test(value) ? '"' + String(value).replaceAll('"', '""') + '"' : value;
    const csv = lines.map(line => line.map(cell).join(',')).join('\n') + '\n';
    return { reports: { day: date, importance, shortTasks, workload }, csv };
  }

  globalThis.dailyPlannerBrowserRequest = async (url, { data, signal } = {}) => {
    const { pathname, searchParams } = new URL(url, location.origin);
    const rows = await tasks();
    signal?.throwIfAborted();
    if (pathname === '/api/tasks' && !data) return rows;
    if (data && ['/api/tasks', '/api/tasks/reschedule', '/api/tasks/complete'].includes(pathname)) {
      return changeTask(rows, pathname, data);
    }
    const settings = data ?? {
      date: searchParams.get('date'),
      minimum: Number(searchParams.get('minimum')),
      capacity: Number(searchParams.get('capacity')),
    };
    if (!validDay(settings.date) || settings.date > '9999-12-30' ||
        !inRange(settings.minimum, 1, 5) || !inRange(settings.capacity, 1, 480)) {
      throw new Error('Enter a valid date, priority, and time budget.');
    }
    if (pathname === '/api/plan') return plan(rows, settings);
    if (pathname === '/api/reports') return reports(rows, settings);
    throw new Error('Unknown DailyPlanner action.');
  };
})();

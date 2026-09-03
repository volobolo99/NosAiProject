const titles = {
  overview: 'System Overview', eye: 'Eye Ai View', world: 'WorldState Inspector', decision: 'Decision Trace',
  simulation: 'Simulation & Tactical Ranking', runtime: 'Runtime & Safety', resources: 'Resources & Provider Router', config: 'Configuration'
};

async function api(path, options = {}) {
  const response = await fetch(path, { headers: {'Content-Type': 'application/json'}, ...options });
  if (!response.ok) throw new Error(await response.text());
  return response.json();
}

function command(action) {
  api('/api/command', {method: 'POST', body: JSON.stringify({action})})
    .then(() => refreshState()).catch(showError);
}

function showError(error) { console.error(error); }

function classified(field) {
  if (!field || field.source === 'UNKNOWN' || field.value === null || field.value === undefined) return 'UNKNOWN';
  return String(field.value);
}

function sourceLabel(field) {
  return field && field.source ? field.source : 'UNKNOWN';
}

function fieldLabel(field) {
  const value = classified(field);
  const source = sourceLabel(field);
  return source === 'UNKNOWN' ? 'UNKNOWN' : `${value} [${source}]`;
}

function inspectorField(state, path) {
  return (state.observation_inspector || []).find(field => field.path === path) || null;
}

function valueText(value) {
  if (value === null || value === undefined) return 'UNKNOWN';
  if (typeof value === 'object') return JSON.stringify(value);
  return String(value);
}

function observationMeta(field) {
  if (!field) return 'UNKNOWN';
  const source = field.source || 'UNCLASSIFIED';
  const reason = field.failure_reason ? ` · ${field.failure_reason}` : '';
  const observed = field.observed_at_utc ? ` · ${field.observed_at_utc}` : '';
  return `${source}${observed}${reason}`;
}

function setLiveMetric(id, field, value = field && field.value) {
  document.querySelector(`#${id}`).textContent = valueText(value);
  document.querySelector(`#${id}-meta`).textContent = observationMeta(field);
}

function renderLiveTelemetry(state) {
  const hp = inspectorField(state, 'client.gameplayBaseline.hp');
  const mp = inspectorField(state, 'client.gameplayBaseline.mp');
  const map = inspectorField(state, 'client.gameplayBaseline.mapId');
  const cellX = inspectorField(state, 'client.gameplayBaseline.standingCell.x');
  const cellY = inspectorField(state, 'client.gameplayBaseline.standingCell.y');
  const entities = inspectorField(state, 'client.gameplayBaseline.entitiesInView');
  const observed = inspectorField(state, 'gameObservation.packetsObserved');
  const decoded = inspectorField(state, 'gameObservation.packetsDecoded');
  const active = inspectorField(state, 'gameObservation.active');
  const target = inspectorField(state, 'client.gameplayBaseline.hasTarget');
  const combat = inspectorField(state, 'client.gameplayBaseline.inCombat');
  setLiveMetric('live-hp', hp, hp && `${valueText(hp.value)}/${valueText(inspectorField(state, 'client.gameplayBaseline.maxHp')?.value)}`);
  setLiveMetric('live-mp', mp, mp && `${valueText(mp.value)}/${valueText(inspectorField(state, 'client.gameplayBaseline.maxMp')?.value)}`);
  setLiveMetric('live-map', map, map && `${valueText(map.value)} · ${valueText(cellX?.value)},${valueText(cellY?.value)}`);
  setLiveMetric('live-entities', entities);
  setLiveMetric('live-packets', observed, observed && `${valueText(observed.value)} / ${valueText(decoded?.value)} decodificati`);
  setLiveMetric('live-observation', active);
  setLiveMetric('live-target', target);
  setLiveMetric('live-combat', combat);
  document.querySelector('#live-observed-at').textContent = hp?.observed_at_utc || state.gate1?.capturedAtUtc || 'in attesa del runtime';
}

function displayPath(path) {
  return path.replace(/\[(\d+)\]/g, ' #$1').replaceAll('.', ' › ');
}

function renderInspector(state) {
  const fields = state.observation_inspector || [];
  document.querySelector('#inspector-count').textContent = String(fields.length);
  const active = inspectorField(state, 'gameObservation.active');
  document.querySelector('#inspector-observation').textContent = valueText(active?.value);
  document.querySelector('#inspector-observation-meta').textContent = observationMeta(active);
  const search = document.querySelector('#inspector-search').value.trim().toLowerCase();
  const source = document.querySelector('#inspector-source').value;
  const selected = fields.filter(field => (!source || field.source === source) && (!search || `${field.path} ${valueText(field.value)} ${field.failure_reason || ''}`.toLowerCase().includes(search)));
  const groups = selected.reduce((all, field) => {
    const group = field.path.split('.')[0] || 'runtime';
    (all[group] ||= []).push(field);
    return all;
  }, {});
  const container = document.querySelector('#inspector-groups');
  container.replaceChildren();
  if (!selected.length) {
    const empty = document.createElement('p');
    empty.className = 'muted-text';
    empty.textContent = fields.length ? 'Nessun campo corrisponde al filtro.' : 'Il runtime non ha ancora fornito campi da ispezionare.';
    container.append(empty);
    return;
  }
  Object.entries(groups).sort(([left], [right]) => left.localeCompare(right)).forEach(([group, rows]) => {
    const details = document.createElement('details');
    details.open = group === 'client' || group === 'gameObservation';
    const summary = document.createElement('summary');
    summary.textContent = `${group} (${rows.length} campi)`;
    details.append(summary);
    const table = document.createElement('table');
    table.className = 'inspector-table';
    table.innerHTML = '<thead><tr><th>Campo</th><th>Valore</th><th>Fonte</th><th>Osservato</th><th>Motivo</th></tr></thead>';
    const body = document.createElement('tbody');
    rows.forEach(field => {
      const row = document.createElement('tr');
      [displayPath(field.path), valueText(field.value), field.source || 'UNCLASSIFIED', field.observed_at_utc || '—', field.failure_reason || '—'].forEach((value, index) => {
        const cell = document.createElement('td');
        cell.textContent = value;
        if (index === 2) cell.className = `source source-${String(value).toLowerCase()}`;
        row.append(cell);
      });
      body.append(row);
    });
    table.append(body);
    details.append(table);
    container.append(details);
  });
}

function render(state) {
  document.querySelector('#mode').textContent = state.mode;
  document.querySelector('#watchdog').textContent = state.mode;
  document.querySelector('#world-version').textContent = `v${state.observation_version}`;
  document.querySelector('#trust').textContent = `${state.trust_level} · ${['OBSERVE','SIMULATE','REVERSIBLE','SENSITIVE','CRITICAL'][state.trust_level] || 'UNKNOWN'}`;
  document.querySelector('#provider').textContent = state.provider || '—';
  document.querySelector('#prev-state').textContent = state.observation_version > 0 ? `v${state.observation_version - 1}` : '—';
  document.querySelector('#connection').textContent = state.connected ? 'RUNTIME CONNECTED' : 'RUNTIME OFFLINE';
  document.querySelector('#connection').className = `pill ${state.connected ? 'ok' : 'muted'}`;
  const gate1 = state.gate1 || null;
  document.querySelector('#world-json').textContent = JSON.stringify({
    version: state.observation_version,
    connected: state.connected,
    telemetry_source: state.telemetry_source,
    provider: state.provider,
    gate1
  }, null, 2);
  const cpu = gate1 && gate1.hardware ? gate1.hardware.cpu : null;
  const gpu = gate1 && gate1.hardware ? gate1.hardware.gpu : null;
  const ram = gate1 && gate1.hardware ? gate1.hardware.processWorkingSetMb : null;
  const client = gate1 && gate1.client ? gate1.client : null;
  document.querySelector('#res-cpu').textContent = classified(cpu);
  document.querySelector('#res-cpu-src').textContent = sourceLabel(cpu);
  document.querySelector('#res-gpu').textContent = classified(gpu);
  document.querySelector('#res-gpu-src').textContent = sourceLabel(gpu);
  document.querySelector('#res-ram').textContent = classified(ram);
  document.querySelector('#res-ram-src').textContent = sourceLabel(ram);
  document.querySelector('#res-client').textContent = client ? client.status : 'UNKNOWN';
  document.querySelector('#res-client-src').textContent = client ? sourceLabel(client.attached) : 'UNKNOWN';
  document.querySelector('#client-status').textContent = client ? `${client.status} [${sourceLabel(client.attached)}]` : 'UNKNOWN';
  document.querySelector('#client-name').textContent = fieldLabel(client && client.processName);
  document.querySelector('#client-pid').textContent = fieldLabel(client && client.processId);
  document.querySelector('#client-title').textContent = fieldLabel(client && client.windowTitle);
  document.querySelector('#client-handle').textContent = fieldLabel(client && client.windowHandle);
  document.querySelector('#client-responding').textContent = fieldLabel(client && client.processResponding);
  document.querySelector('#client-visible').textContent = fieldLabel(client && client.windowVisible);
  const gameplay = client && client.gameplayBaseline;
  document.querySelector('#client-gameplay').textContent = gameplay
    ? `${sourceLabel(gameplay)}${gameplay.failureReason ? ' · ' + gameplay.failureReason : ''}`
    : 'UNKNOWN';
  document.querySelector('#client-warning').textContent = (client && (client.warning || client.failureReason))
    || (state.gate1_failure ? `runtime: ${state.gate1_failure}` : 'No client warning.');
  renderLiveTelemetry(state);
  renderInspector(state);
  document.querySelectorAll('[data-config]').forEach(el => {
    const key = el.dataset.config;
    if (key in state.config) {
      if (el.type === 'checkbox') el.checked = Boolean(state.config[key]);
      else el.value = state.config[key];
    }
  });
}

async function refreshState() {
  try { render(await api('/api/state')); }
  catch (error) { document.querySelector('#connection').textContent = 'DASHBOARD API ERROR'; showError(error); }
}

async function saveConfig() {
  const payload = {};
  document.querySelectorAll('[data-config]').forEach(el => {
    payload[el.dataset.config] = el.type === 'checkbox' ? el.checked : el.value;
  });
  try { render(await api('/api/config', {method:'POST', body: JSON.stringify(payload)})); }
  catch (error) { showError(error); }
}

document.querySelectorAll('.nav').forEach(button => button.addEventListener('click', () => {
  document.querySelectorAll('.nav').forEach(b => b.classList.remove('active'));
  document.querySelectorAll('.view').forEach(v => v.classList.remove('active-view'));
  button.classList.add('active');
  document.querySelector(`#${button.dataset.view}`).classList.add('active-view');
  document.querySelector('#page-title').textContent = titles[button.dataset.view];
}));

document.querySelector('#refresh').addEventListener('click', refreshState);
document.querySelector('#inspector-search').addEventListener('input', refreshState);
document.querySelector('#inspector-source').addEventListener('change', refreshState);
document.querySelectorAll('.toggle').forEach(t => t.addEventListener('click', () => t.classList.toggle('active')));
refreshState();
setInterval(refreshState, 2000);

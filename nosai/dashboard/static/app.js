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

function render(state) {
  document.querySelector('#mode').textContent = state.mode;
  document.querySelector('#watchdog').textContent = state.mode;
  document.querySelector('#world-version').textContent = `v${state.observation_version}`;
  document.querySelector('#trust').textContent = `${state.trust_level} · ${['OBSERVE','SIMULATE','REVERSIBLE','SENSITIVE','CRITICAL'][state.trust_level] || 'UNKNOWN'}`;
  document.querySelector('#provider').textContent = state.provider || '—';
  document.querySelector('#prev-state').textContent = state.observation_version > 0 ? `v${state.observation_version - 1}` : '—';
  document.querySelector('#connection').textContent = state.connected ? 'RUNTIME CONNECTED' : 'RUNTIME OFFLINE';
  document.querySelector('#connection').className = `pill ${state.connected ? 'ok' : 'muted'}`;
  document.querySelector('#world-json').textContent = JSON.stringify({
    version: state.observation_version,
    connected: state.connected,
    telemetry_source: state.telemetry_source,
    provider: state.provider
  }, null, 2);
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
document.querySelectorAll('.toggle').forEach(t => t.addEventListener('click', () => t.classList.toggle('active')));
refreshState();
setInterval(refreshState, 2000);

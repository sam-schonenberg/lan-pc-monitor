namespace PCMonitor.Service.Services;

public sealed class DiagnosticsPageService
{
    public string CreateHtml() => """
        <!doctype html>
        <html lang="en">
        <head>
          <meta charset="utf-8">
          <meta name="viewport" content="width=device-width, initial-scale=1">
          <title>LAN PC Monitor diagnostics</title>
          <style>
            :root { color-scheme: dark; font-family: system-ui, -apple-system, "Segoe UI", sans-serif; }
            * { box-sizing: border-box; }
            body { margin: 0; background: #0f172a; color: #e2e8f0; }
            main { width: min(1100px, 94vw); margin: 0 auto; padding: 2rem 0 4rem; }
            header { display: flex; justify-content: space-between; align-items: start; gap: 1rem; margin-bottom: 1.5rem; }
            h1 { margin: 0 0 .3rem; font-size: clamp(1.6rem, 4vw, 2.3rem); }
            .muted { color: #94a3b8; }
            .panel { padding: 1rem; margin-bottom: 1rem; background: #1e293b; border: 1px solid #334155; border-radius: 14px; }
            .summary { display: grid; grid-template-columns: repeat(auto-fit, minmax(145px, 1fr)); gap: .8rem; }
            .metric { padding: .8rem; background: #0f172a; border-radius: 10px; }
            .metric span { display: block; color: #94a3b8; font-size: .78rem; }
            .metric strong { display: block; margin-top: .2rem; font-size: 1.05rem; overflow-wrap: anywhere; }
            form { display: grid; grid-template-columns: repeat(auto-fit, minmax(150px, 1fr)); gap: .75rem; align-items: end; }
            label { display: grid; gap: .35rem; color: #cbd5e1; font-size: .82rem; }
            input, select, button { min-height: 2.55rem; padding: .55rem .7rem; border: 1px solid #475569; border-radius: 8px; background: #0f172a; color: #e2e8f0; }
            button { cursor: pointer; background: #2563eb; border-color: #3b82f6; font-weight: 650; }
            button.secondary { background: #334155; border-color: #475569; }
            button:disabled { opacity: .5; cursor: default; }
            #message { padding: 2rem; text-align: center; color: #94a3b8; }
            .event { display: grid; grid-template-columns: 9rem 8rem 1fr auto; gap: .85rem; align-items: center; width: 100%; padding: 1rem; margin-bottom: .65rem; text-align: left; background: #1e293b; border: 1px solid #334155; border-radius: 12px; color: inherit; }
            .event:hover { border-color: #64748b; }
            .badge { justify-self: start; padding: .25rem .55rem; border-radius: 999px; font-size: .76rem; font-weight: 750; text-transform: uppercase; }
            .critical { color: #fecaca; background: #7f1d1d; }
            .error { color: #fed7aa; background: #7c2d12; }
            .identity strong, .identity span { display: block; }
            .identity span { margin-top: .2rem; color: #94a3b8; font-size: .82rem; }
            dialog { width: min(680px, 92vw); padding: 0; color: #e2e8f0; background: #1e293b; border: 1px solid #475569; border-radius: 14px; }
            dialog::backdrop { background: #020617b8; }
            dialog header { padding: 1rem 1rem 0; }
            pre { margin: 1rem; padding: 1rem; overflow: auto; background: #0f172a; border-radius: 9px; white-space: pre-wrap; }
            .actions { display: flex; justify-content: space-between; gap: .75rem; margin-top: 1rem; }
            @media (max-width: 720px) { .event { grid-template-columns: 1fr auto; } .event time, .identity { grid-column: 1; } }
          </style>
        </head>
        <body>
          <main>
            <header><div><h1>Windows diagnostics</h1><div class="muted">Read-only Error and Critical events collected by LAN PC Monitor.</div></div></header>
            <section class="panel summary" id="summary" aria-label="Collection status"></section>
            <section class="panel">
              <form id="filters">
                <label>Severity<select id="severity"><option value="">All retained</option><option value="critical">Critical</option><option value="error">Error and Critical</option></select></label>
                <label>Provider<select id="provider"><option value="">All providers</option></select></label>
                <label>Event ID<input id="eventId" type="number" min="0" placeholder="Any"></label>
                <button type="submit">Apply filters</button>
                <button class="secondary" type="button" id="refresh">Refresh</button>
              </form>
            </section>
            <section id="events" aria-live="polite"><div id="message">Loading diagnostics…</div></section>
            <div class="actions"><span class="muted" id="pageLabel"></span><button id="next" disabled>Older events</button></div>
          </main>
          <dialog id="details"><header><div><strong>Event details</strong><div class="muted">Compact data stored by the service</div></div><button class="secondary" id="close">Close</button></header><pre id="json"></pre></dialog>
          <script>
            const api = '/api/v1/diagnostics';
            const state = { before: null, hasMore: false, page: 1 };
            const el = id => document.getElementById(id);
            const metric = (label, value) => { const box = document.createElement('div'); box.className = 'metric'; const name = document.createElement('span'); name.textContent = label; const data = document.createElement('strong'); data.textContent = value; box.append(name, data); return box; };

            async function loadStatus() {
              const response = await fetch(`${api}/status`, { cache: 'no-store' });
              if (!response.ok) throw new Error(`Status request failed (${response.status})`);
              const status = await response.json();
              el('summary').replaceChildren(
                metric('Collector', status.enabled ? 'Enabled' : 'Disabled'),
                metric('Stored events', status.storedEventCount),
                metric('Scan interval', `${status.scanIntervalMinutes} minutes`),
                metric('Last successful scan', status.lastSuccessfulScan ? new Date(status.lastSuccessfulScan).toLocaleString() : 'Not yet'),
                metric('Retention', `${status.retentionDays} days`),
                metric('Storage limit', `${status.maximumStorageMegabytes} MB`));
              const providers = el('provider');
              providers.replaceChildren(new Option('All providers', ''));
              for (const provider of status.providers) { const option = document.createElement('option'); option.value = provider; option.textContent = provider; providers.append(option); }
            }

            function eventButton(item) {
              const button = document.createElement('button'); button.className = 'event'; button.type = 'button';
              const time = document.createElement('time'); time.dateTime = item.timestamp; time.textContent = new Date(item.timestamp).toLocaleString();
              const badge = document.createElement('span'); badge.className = `badge ${item.severity}`; badge.textContent = item.severity;
              const identity = document.createElement('span'); identity.className = 'identity';
              const title = document.createElement('strong'); title.textContent = item.title || item.category.replaceAll('-', ' ');
              const summary = document.createElement('span'); summary.textContent = item.summary || `${item.provider} · Event ${item.eventId}`;
              const source = document.createElement('span'); source.textContent = `${item.provider} · Event ${item.eventId}`;
              identity.append(title, summary, source);
              const sequence = document.createElement('span'); sequence.className = 'muted'; sequence.textContent = `#${item.sequence}`;
              button.append(time, badge, identity, sequence);
              button.addEventListener('click', () => { el('json').textContent = JSON.stringify(item, null, 2); el('details').showModal(); });
              return button;
            }

            async function loadEvents(reset = false) {
              if (reset) { state.before = null; state.page = 1; }
              const query = new URLSearchParams({ limit: '50' });
              if (state.before) query.set('beforeSequence', state.before);
              if (el('severity').value) query.set('minimumSeverity', el('severity').value);
              if (el('provider').value) query.set('provider', el('provider').value);
              if (el('eventId').value) query.set('eventId', el('eventId').value);
              el('events').replaceChildren(metric('Loading', 'Please wait…'));
              const response = await fetch(`${api}/events?${query}`, { cache: 'no-store' });
              if (!response.ok) throw new Error(`Event request failed (${response.status})`);
              const result = await response.json();
              state.hasMore = result.hasMore; state.before = result.previousSequence;
              el('events').replaceChildren(...(result.events.length ? result.events.map(eventButton) : [metric('No events', 'No retained events match these filters.') ]));
              el('next').disabled = !state.hasMore;
              el('pageLabel').textContent = `Page ${state.page} · ${result.events.length} event${result.events.length === 1 ? '' : 's'}`;
            }

            async function safely(action) { try { await action(); } catch (error) { el('events').replaceChildren(metric('Unable to load diagnostics', error.message)); } }
            el('filters').addEventListener('submit', event => { event.preventDefault(); safely(() => loadEvents(true)); });
            el('refresh').addEventListener('click', () => safely(async () => { await loadStatus(); await loadEvents(true); }));
            el('next').addEventListener('click', () => { state.page++; safely(() => loadEvents()); });
            el('close').addEventListener('click', () => el('details').close());
            safely(async () => { await loadStatus(); await loadEvents(true); });
          </script>
        </body>
        </html>
        """;
}

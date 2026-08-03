import json
import os

ROOT = 'hardening'
README = os.path.join(ROOT, 'README.md')

rows = []
files = []
total = 0


def load(path):
    full = os.path.join(ROOT, path)
    if not os.path.isfile(full):
        return None
    with open(full, encoding='utf-8-sig') as f:
        return json.load(f)


def semgrep(data):
    return len(data.get('results', [])) if isinstance(data, dict) else 0


def trivy(data):
    n = 0
    for run in data.get('runs', []):
        n += len(run.get('results', []))
    return n


def dockle(data):
    entries = data if isinstance(data, list) else data.get('checks', [])
    return sum(1 for c in entries if c.get('level') not in ('INFO', 'SKIP', None))


def zap(data):
    sites = data.get('site', []) if isinstance(data, dict) else []
    return sum(len(s.get('alerts', [])) for s in sites)


checks = [
    ('Semgrep (SAST)', 'semgrep/semgrep-report.json', semgrep),
    ('Trivy (imagen)', 'trivy/trivy-results.sarif', trivy),
    ('Dockle (contenedor)', 'dockle/dockle-report.json', dockle),
    ('ZAP (DAST)', 'zap/zap-report.json', zap),
    ('ZAP PR (DAST)', 'zap-pr/zap-pr-report.json', zap),
]

for label, path, fn in checks:
    try:
        data = load(path)
    except Exception as e:
        rows.append((label, 'ERROR: %s' % e))
        continue
    if data is None:
        continue
    try:
        n = fn(data)
        rows.append((label, n))
        total += n
        files.append(path)
    except Exception as e:
        rows.append((label, 'ERROR: %s' % e))

os.makedirs(ROOT, exist_ok=True)
lines = [
    '# Hardening Report Consolidado',
    '',
    '> Commit: `%s` | Rama: `%s` | Evento: `%s`' % (
        os.environ.get('GITHUB_SHA', '?')[:7],
        os.environ.get('GITHUB_REF_NAME', '?'),
        os.environ.get('GITHUB_EVENT_NAME', '?')),
    '',
    '| Herramienta | Hallazgos |',
    '|---|---|',
]
for label, n in rows:
    lines.append('| %s | %s |' % (label, n))
lines.append('')
lines.append('**Total hallazgos: %s**' % total)
if files:
    lines.append('')
    lines.append('## Reportes incluidos')
    for f in sorted(files):
        lines.append('- `%s`' % f)

content = '\n'.join(lines) + '\n'
with open(README, 'w', encoding='utf-8') as f:
    f.write(content)
print(content)

import xml.etree.ElementTree as ET
import glob
import sys

files = glob.glob('coverage/*/coverage.opencover.xml')
if not files:
    print('ERROR: No coverage reports found')
    sys.exit(1)

total_seq = 0
total_vis = 0
for f in files:
    root = ET.parse(f).getroot()
    summary = root.find('.//Summary')
    if summary is None:
        summary = root.find('.//{*}Summary')
    if summary is not None:
        s = summary.attrib
        total_seq += int(s.get('numSequencePoints', 0))
        total_vis += int(s.get('visitedSequencePoints', 0))
        print('  %s: %s%%' % (f, s.get('sequenceCoverage', '?')))

pct = (total_vis / total_seq * 100) if total_seq else 0
print('Total: %.1f%% (%d/%d)' % (pct, total_vis, total_seq))
if pct < 75:
    print('ERROR: Coverage below 75% threshold')
    sys.exit(1)
print('OK: Coverage >= 75%')

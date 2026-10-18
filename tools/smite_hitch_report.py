"""Read-only hitch report for a SMITE 2 (Hemingway) log.

Usage: python -I scripts/smite_hitch_report.py <Hemingway.log> [more logs...]

Counts the log-visible stutter sources: Slate font-cache flushes, DLSS-G
re-creation after focus loss, PSO precaching misses per map, and frames the
game itself reports as >100 ms (Streamline frame-timer reset or the movement
component's max-delta tick). Log lines are not a frame capture: use PresentMon
for frame-time percentiles.
"""
import collections
import datetime
import io
import re
import sys

TS = re.compile(r'^\[(\d{4})\.(\d\d)\.(\d\d)-(\d\d)\.(\d\d)\.(\d\d):(\d{3})\]\[\s*\d+\](\w+): (.*)')
CONTEXT = [
    ('PSO PRECACHING MISS', 'pso-miss'),
    ('FlushAsyncLoading', 'flush-async-loading'),
    ('blocking wait for shader preload', 'shader-preload-wait'),
    ('FontCache flush', 'font-cache-flush'),
    ('Recreating Clothing', 'cloth-recreate'),
    ('interpolation state changed', 'fg-toggle'),
    ('synchronously load', 'sync-load'),
]


def parse(path):
    recs = []
    with io.open(path, encoding='utf-8', errors='replace') as f:
        for line in f:
            m = TS.match(line)
            if m:
                g = m.groups()
                t = datetime.datetime(*map(int, g[:6]), int(g[6]) * 1000)
                recs.append([t, g[7], g[8].rstrip()])
            elif recs and line.startswith('PSO PRECACHING MISS'):
                recs.append([recs[-1][0], 'PSO', 'PSO PRECACHING MISS'])
    current = '?'
    for r in recs:
        m = re.search(r'map=(L_\w+)', r[2]) or re.search(r'LoadMap\(/Game/Maps/\S*/(L_\w+)\)', r[2])
        if m:
            current = m.group(1)
        r.append(current)
    return recs


def report(path):
    recs = parse(path)
    if not recs:
        print(path, ': no timestamped lines')
        return
    print('=====', path)
    print('span:', recs[0][0], '->', recs[-1][0])

    flushes = [r for r in recs if 'FontCache flush' in r[2]]
    limits = collections.Counter(re.search(r'(\d+/\d+) Textures', r[2]).group(1)
                                 for r in flushes if 'Textures' in r[2])
    print('font-cache flushes:', len(flushes), dict(limits))

    created, recreate_ms = None, []
    for t, _, msg, _ in recs:
        if 'NVSDK_NGX_CreateFeature_Validate' in msg:
            created = t
        elif 'from disabled to enabled' in msg and created:
            recreate_ms.append(int((t - created).total_seconds() * 1000))
            created = None
    focus = sum('window not focused' in r[2] or 'complete loss of focus' in r[2] for r in recs)
    print('DLSS-G re-creates (ms):', sorted(ms for ms in recreate_ms if ms < 5000), '| focus-loss lines:', focus)

    print('PSO misses by map:', dict(collections.Counter(r[3] for r in recs if r[1] == 'PSO')))

    # D3D12RHI logs 100, 200, 400 ... ms while one PSO is still compiling; keep the longest per stall.
    stalls = []
    for t, _, msg, map_name in recs:
        m = re.match(r'Waited for PSO creation for ([0-9.]+)ms', msg)
        if not m:
            continue
        ms = float(m.group(1))
        if stalls and ms > stalls[-1][1]:
            stalls[-1][1] = ms
        else:
            stalls.append([t, ms, map_name])
    print('driver PSO stalls >=100 ms:', [(t.strftime('%H:%M:%S'), int(ms), m) for t, ms, m in stalls])

    events, last = [], None
    for i, r in enumerate(recs):
        if 'Frame rate over' in r[2] or 'maximum delta time movement tick' in r[2]:
            if last is None or (r[0] - recs[last][0]).total_seconds() >= 1.0:
                events.append(i)
                last = i
    in_match = [i for i in events if not recs[i][3].startswith('L_M')]
    print('>100 ms frames reported by game:', len(events), '| outside lobbies:', len(in_match))
    tagged = collections.Counter()
    for i in in_match:
        seen = set()
        j = i - 1
        while j > 0 and (recs[i][0] - recs[j][0]).total_seconds() < 1.5:
            for needle, tag in CONTEXT:
                if needle in recs[j][2]:
                    seen.add(tag)
            j -= 1
        tagged.update(seen or {'no-log-cause'})
    print('preceding causes (within 1.5 s):', dict(tagged.most_common()))


if __name__ == '__main__':
    for p in sys.argv[1:]:
        report(p)

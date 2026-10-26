#!/usr/bin/env python3
"""Group BO3-Customs logger runtime errors by exact script, offset, and message."""
import argparse
import collections
import pathlib
import re

PATTERN = re.compile(r'^\[(?P<time>[^]]+)\] \[Script\] (?P<side>server|client) \((?P<language>gsc|csc)\) runtime error (?P<index>\d+) in (?P<script>[^ ]+) \+(?P<offset>0x[0-9A-Fa-f]+): (?P<message>.*)$')

def summarize(path):
    groups = collections.defaultdict(list)
    events = []
    for lineno, line in enumerate(path.read_text(errors='replace').splitlines(), 1):
        if '[Mods]' in line and ('loads with' in line or 'loads now' in line or 'unload' in line or 'mod 1 is' in line or 'mod 2 is' in line):
            events.append((lineno, line))
        if '[Server] shutdown' in line or '[Log] new level' in line:
            events.append((lineno, line))
        m = PATTERN.match(line)
        if not m:
            continue
        d = m.groupdict()
        key = (d['script'], d['offset'], d['message'], d['side'])
        groups[key].append((lineno, d['time']))
    print(f'{path}: {sum(map(len, groups.values()))} logged runtime errors across {len(groups)} signatures')
    for (script, offset, message, side), instances in sorted(groups.items(), key=lambda x: (-len(x[1]), x[0])):
        print(f'  {len(instances):>4} {side:6} {script} +{offset}: {message} [first line {instances[0][0]}, {instances[0][1]}]')
    print('  Timeline: mod transitions and first occurrence of each runtime-error signature')
    firsts = [(v[0][0], f'[{v[0][1]}] [FirstError] {k[0]} +{k[1]}: {k[2]}') for k, v in groups.items()]
    for lineno, event in sorted(events + firsts):
        print(f'    line {lineno}: {event}')
    return groups

if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('logs', type=pathlib.Path, nargs='+')
    args = parser.parse_args()
    for log in args.logs:
        summarize(log)

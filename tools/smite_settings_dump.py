"""Read-only dump of the graphics map in SMITE 2's HWGameUserSettings.sav.

Usage: python -I scripts/smite_settings_dump.py [save] [--json out.json]

The save is a GVAS file. The setting values live in the top-level
`SavedSettingsConfig` property: MapProperty<NameProperty, StrProperty>, written
with the UE 5.4+ property tag (type tree, int32 size, uint8 flags).
"""
import json
import os
import struct
import sys

DEFAULT = os.path.join(os.environ.get('LOCALAPPDATA', ''), 'SMITE2Alpha', 'Saved', 'SaveGames', 'HWGameUserSettings.sav')


def read_fstring(data, offset):
    (length,) = struct.unpack_from('<i', data, offset)
    offset += 4
    if length == 0:
        return '', offset
    if length < 0:  # UTF-16
        raw = data[offset:offset - length * 2]
        return raw.decode('utf-16-le').rstrip('\x00'), offset - length * 2
    raw = data[offset:offset + length]
    return raw.decode('utf-8', 'replace').rstrip('\x00'), offset + length


def settings_map(data):
    marker = b'\x14\x00\x00\x00SavedSettingsConfig\x00'
    start = data.find(marker)
    if start < 0:
        raise ValueError('SavedSettingsConfig not found')
    offset = start + len(marker)
    kind, offset = read_fstring(data, offset)
    if kind != 'MapProperty':
        raise ValueError('unexpected type ' + kind)
    (params,) = struct.unpack_from('<i', data, offset)
    offset += 4
    types = []
    for _ in range(params):
        name, offset = read_fstring(data, offset)
        (sub,) = struct.unpack_from('<i', data, offset)
        offset += 4
        if sub != 0:
            raise ValueError('nested type params not supported')
        types.append(name)
    if types != ['NameProperty', 'StrProperty']:
        raise ValueError('unexpected map types %r' % types)
    size_offset = offset
    (size,) = struct.unpack_from('<i', data, offset)
    offset += 4
    flags = data[offset]
    offset += 1
    body_start = offset
    removed, count = struct.unpack_from('<ii', data, offset)
    offset += 8
    entries = []
    for _ in range(count):
        key, offset = read_fstring(data, offset)
        value, offset = read_fstring(data, offset)
        entries.append((key, value))
    if offset - body_start != size:
        raise ValueError('size mismatch: header %d, parsed %d' % (size, offset - body_start))
    return {'size_offset': size_offset, 'size': size, 'flags': flags, 'removed': removed, 'entries': entries}


def main(argv):
    path = DEFAULT
    out = None
    rest = list(argv)
    if '--json' in rest:
        i = rest.index('--json')
        out = rest[i + 1]
        del rest[i:i + 2]
    if rest:
        path = rest[0]
    data = open(path, 'rb').read()
    info = settings_map(data)
    for key, value in info['entries']:
        print('%-30s %s' % (key, value))
    print('(%d entries, map size %d bytes, file %d bytes)' % (len(info['entries']), info['size'], len(data)))
    if out:
        with open(out, 'w', encoding='utf-8') as handle:
            json.dump(dict(info['entries']), handle, ensure_ascii=False, indent=1)


if __name__ == '__main__':
    main(sys.argv[1:])

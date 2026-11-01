"""Independent Pillow decoder comparison and read-only input manifest.

Usage: python scripts/verify_samples.py artifacts/sample-report [--manifest]
No game-specific paths or resource numbers are built into this verifier.
"""
import csv
import hashlib
import io
import json
import struct
import sys
from pathlib import Path
from PIL import Image


def expand(data):
    if data[:4] != b'LZP2':
        return data
    size, packed = struct.unpack_from('<II', data, 8)
    assert len(data) == packed + 16 and size <= 64 * 1024**2
    out = bytearray()
    pos = 16
    while len(out) < size:
        command = data[pos]
        pos += 1
        if command < 64:
            out.extend(data[pos:pos + command])
            pos += command
        elif command < 128:
            length = ((command & 63) << 8) + data[pos] + 4
            out.extend(bytes([data[pos + 1]]) * length)
            pos += 2
        else:
            distance = ((command & 7) << 8) + data[pos] + 1
            pos += 1
            for _ in range(((command >> 3) & 15) + 3):
                out.append(out[-distance])
        assert len(out) <= size
    assert 1 <= len(data) - pos <= 16 and not any(data[pos:])
    return bytes(out)


def main():
    directory = Path(sys.argv[1])
    report = json.loads((directory / 'scan-report.json').read_text('utf-8-sig'))
    rows = {r['entry']: r for r in csv.DictReader((directory / 'index.csv').open(encoding='utf-8-sig'))}
    checks = []
    for sample in report['decoded']:
        row = rows[sample['locator']]
        with Path(row['source']).open('rb') as source:
            source.seek(int(row['source_offset']))
            raw = expand(source.read(int(row['stored_length'])))
        width, height = int(row['width']), int(row['height'])
        payload = raw[int(row['payload_offset']):]
        if row['container'] == 'Image':
            image = Image.open(io.BytesIO(raw)).convert('RGBA')
        elif row['format'] == 'ARGB8':
            image = Image.frombytes('RGBA', (width, height), payload[:width * height * 4], 'raw', 'ARGB')
        else:
            header = bytearray(128)
            header[:4] = b'DDS '
            struct.pack_into('<7I', header, 4, 124, 0x81007, height, width, len(payload), 0, 1)
            struct.pack_into('<II4s', header, 76, 32, 4, {'BC1': b'DXT1', 'BC2': b'DXT3', 'BC3': b'DXT5'}[row['format']])
            struct.pack_into('<I', header, 108, 0x1000)
            image = Image.open(io.BytesIO(header + payload)).convert('RGBA')
        digest = hashlib.sha256(image.tobytes('raw', 'BGRA')).hexdigest()
        checks.append({'locator': sample['locator'], 'equal': digest == sample['bgraSha256'], 'reference_bgra_sha256': digest})
    result = {'reference': 'Pillow ' + Image.__version__, 'checks': checks, 'passed': sum(c['equal'] for c in checks), 'total': len(checks)}
    (directory / 'independent-verification.json').write_text(json.dumps(result, ensure_ascii=False, indent=2), 'utf-8')
    print(f"Pillow exact pixel comparisons: {result['passed']}/{result['total']}", flush=True)
    if '--manifest' in sys.argv:
        root = Path(report['root'])
        manifest = []
        for path in sorted(root.rglob('*')):
            if path.is_file():
                with path.open('rb') as stream:
                    sha = hashlib.file_digest(stream, 'sha256').hexdigest()
                manifest.append({'path': path.relative_to(root).as_posix(), 'size': path.stat().st_size, 'sha256': sha})
        (directory / 'input-manifest.json').write_text(json.dumps({'root': str(root), 'files': manifest}, ensure_ascii=False, indent=2), 'utf-8')
        print(f"Input manifest: {len(manifest)} files", flush=True)
    if not all(c['equal'] for c in checks):
        raise SystemExit(1)


if __name__ == '__main__':
    main()

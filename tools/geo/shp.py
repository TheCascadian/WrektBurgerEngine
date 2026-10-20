"""A small reader for ESRI shapefiles (polygons and polylines) and their dbf tables: no third-party package needed.

shapes(path) -> list of (parts, points) where parts are lists of (lon, lat); records(path) -> list of dicts.
Natural Earth (public domain) ships its vectors in this format.
"""
import struct


def shapes(path):
    data = open(path if path.endswith('.shp') else path + '.shp', 'rb').read()
    pos = 100
    out = []
    while pos + 8 <= len(data):
        _, length = struct.unpack('>ii', data[pos:pos + 8])
        body = data[pos + 8:pos + 8 + length * 2]
        pos += 8 + length * 2
        st = struct.unpack('<i', body[:4])[0]
        if st == 0:
            out.append([])
            continue
        if st not in (3, 5):
            raise ValueError(f'shape type {st} not supported')
        nparts, npts = struct.unpack('<ii', body[36:44])
        parts = list(struct.unpack('<%di' % nparts, body[44:44 + 4 * nparts]))
        off = 44 + 4 * nparts
        pts = struct.unpack('<%dd' % (2 * npts), body[off:off + 16 * npts])
        xy = list(zip(pts[0::2], pts[1::2]))
        parts.append(npts)
        out.append([xy[a:b] for a, b in zip(parts[:-1], parts[1:])])
    return out


def records(path):
    base = path[:-4] if path.endswith(('.shp', '.dbf')) else path
    data = open(base + '.dbf', 'rb').read()
    n, hlen, rlen = struct.unpack('<xxxxIHH', data[:12])
    fields = []
    pos = 32
    while data[pos] != 0x0D:
        name = data[pos:pos + 11].split(b'\0')[0].decode('ascii')
        size = data[pos + 16]
        fields.append((name, size))
        pos += 32
    out = []
    for i in range(n):
        rec = data[hlen + i * rlen:hlen + (i + 1) * rlen]
        p = 1
        row = {}
        for name, size in fields:
            row[name] = rec[p:p + size].decode('utf-8', 'replace').replace('\x00', '').strip()
            p += size
        out.append(row)
    return out

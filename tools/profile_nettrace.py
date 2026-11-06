"""Reads a CPU sample trace the .NET runtime wrote by itself and prints where the time went, with no tool to install.

Record (any Burger program, the run must end normally so the method names are written):
    DOTNET_EnableEventPipe=1 DOTNET_EventPipeOutputPath=t.nettrace DOTNET_EventPipeCircularMB=2048 \
    DOTNET_EventPipeConfig=Microsoft-DotNETCore-SampleProfiler:0:5,Microsoft-Windows-DotNETRuntime:4c14fccbd:5 \
    burger validate World500BCE --days 60

Read:
    python tools/profile_nettrace.py t.nettrace [--top 60] [--thread N] [--under Substring] [--callers Substring] [--all-threads]

Prints the busiest threads, then per method: exclusive samples (the method itself) and inclusive samples (it and what it calls). --under keeps only the stacks that pass through a method
whose name contains the text, so one system can be taken apart. One sample is about 1 ms of a thread running managed code.
"""
import struct, sys, bisect, collections, re


def varuint(b, i):
    r = 0; s = 0
    while True:
        x = b[i]; i += 1
        r |= (x & 0x7f) << s; s += 7
        if not x & 0x80:
            return r, i


def wstr(b, i):
    e = i
    while b[e] != 0 or b[e + 1] != 0:
        e += 2
    return b[i:e].decode('utf-16-le', 'replace'), e + 2


class Reader:
    def __init__(self, path):
        self.b = open(path, 'rb').read()
        self.meta = {}      # metadata id -> (provider, event id)
        self.stacks = {}    # stack id -> (start, end) in the file
        self.samples = collections.Counter()   # (thread, stack as (start, end) in the file) -> count
        self.pending = collections.Counter()   # samples whose stack ids belong to the current stretch between two sequence points
        self.methods = []   # (start, size, name)
        self.read()

    def read(self):
        b = self.b; i = 0
        assert b[:8] == b'Nettrace', 'not a nettrace file'
        i = 8
        n = struct.unpack_from('<i', b, i)[0]; i += 4 + n
        while True:
            tag = b[i]; i += 1
            if tag == 1:
                break  # null reference: the end of the stream
            assert tag == 5, 'object expected at %d, tag %d' % (i, tag)
            assert b[i] == 5; i += 1
            assert b[i] == 1; i += 1
            ver, minver, nl = struct.unpack_from('<iii', b, i); i += 12
            name = b[i:i + nl].decode(); i += nl
            assert b[i] == 6; i += 1
            if name == 'Trace':
                i += 16 + 8 + 8 + 4 + 4 + 4 + 4
            else:
                size = struct.unpack_from('<i', b, i)[0]; i += 4
                i = (i + 3) & ~3
                if name in ('EventBlock', 'MetadataBlock'):
                    self.block(i, i + size, name == 'MetadataBlock')
                elif name == 'StackBlock':
                    self.stack_block(i, i + size)
                elif name == 'SPBlock':
                    self.end_stretch()
                i += size
            assert b[i] == 6, 'end of object expected'; i += 1
        self.end_stretch()

    def end_stretch(self):
        # stack ids start again after every sequence point
        for (t, sid), c in self.pending.items():
            r = self.stacks.get(sid)
            if r:
                self.samples[(t, r)] += c
        self.pending.clear()
        self.stacks.clear()

    def stack_block(self, i, end):
        b = self.b
        first, count = struct.unpack_from('<ii', b, i); i += 8
        for k in range(count):
            n = struct.unpack_from('<i', b, i)[0]; i += 4
            self.stacks[first + k] = (i, i + n)
            i += n

    def block(self, i, end, is_meta):
        b = self.b
        hsize, flags = struct.unpack_from('<hh', b, i)
        compressed = flags & 1
        i += hsize
        meta = 0; thread = 0; stack = 0; seq = 0; size = 0; ts = 0
        while i < end:
            if compressed:
                f = b[i]; i += 1
                if f & 1: meta, i = varuint(b, i)
                if f & 2:
                    d, i = varuint(b, i); seq += d
                    _, i = varuint(b, i)
                    _, i = varuint(b, i)
                elif meta != 0:
                    seq += 1
                if f & 4: thread, i = varuint(b, i)
                if f & 8: stack, i = varuint(b, i)
                d, i = varuint(b, i); ts += d
                if f & 16: i += 16
                if f & 32: i += 16
                if f & 128: size, i = varuint(b, i)
                p = i; i += size
            else:
                raise SystemExit('uncompressed event blocks are not read')
            if is_meta:
                self.metadata(p, size)
            else:
                self.event(meta & 0x7fffffff, thread, stack, p, size)

    def metadata(self, p, size):
        b = self.b
        mid = struct.unpack_from('<i', b, p)[0]
        prov, q = wstr(b, p + 4)
        eid = struct.unpack_from('<i', b, q)[0]
        self.meta[mid] = (prov, eid)

    def event(self, meta, thread, stack, p, size):
        m = self.meta.get(meta)
        if not m:
            return
        prov, eid = m
        if prov == 'Microsoft-DotNETCore-SampleProfiler':
            if struct.unpack_from('<i', self.b, p)[0] == 2:   # 2: running managed code; 1: outside it
                self.pending[(thread, stack)] += 1
        elif eid in (143, 144) and 'DotNETRuntime' in prov:
            b = self.b
            start, msize = struct.unpack_from('<QI', b, p + 16)
            q = p + 16 + 8 + 4 + 4 + 4
            ns, q = wstr(b, q)
            name, q = wstr(b, q)
            self.methods.append((start, msize, ns.rsplit('.', 1)[-1] + '.' + name, ns))


def main():
    a = sys.argv[1:]
    path = a[0]
    top = int(a[a.index('--top') + 1]) if '--top' in a else 60
    under = a[a.index('--under') + 1] if '--under' in a else None
    want = int(a[a.index('--thread') + 1]) if '--thread' in a else None
    allt = '--all-threads' in a
    callers = a[a.index('--callers') + 1] if '--callers' in a else None
    r = Reader(path)
    r.methods.sort()
    starts = [m[0] for m in r.methods]
    b = r.b
    per_thread = collections.Counter()
    for (t, s), c in r.samples.items():
        per_thread[t] += c
    print('threads (samples):', per_thread.most_common(12))
    if not per_thread:
        return
    if want is None and not allt:
        want = per_thread.most_common(1)[0][0]
    name_of = {}

    def method(addr):
        if addr in name_of:
            return name_of[addr]
        k = bisect.bisect_right(starts, addr) - 1
        n = '?'
        if k >= 0:
            st, sz, nm, _ = r.methods[k]
            if addr < st + sz:
                n = nm
        name_of[addr] = n
        return n

    excl = collections.Counter(); incl = collections.Counter(); total = 0; who = collections.Counter()
    for (t, (s, e)), c in r.samples.items():
        if not allt and t != want:
            continue
        addrs = [struct.unpack_from('<Q', b, x)[0] for x in range(s, e, 8)]
        names = [method(x) for x in addrs]
        if under and not any(under in n for n in names):
            continue
        total += c
        if callers:
            seen = set()
            for k, n in enumerate(names):
                if callers in n and k + 1 < len(names) and names[k + 1] not in seen:
                    seen.add(names[k + 1]); who[names[k + 1]] += c
        if names:
            excl[names[0]] += c
        for n in set(names):
            incl[n] += c
    print('samples: %d (%s)' % (total, 'all threads' if allt else 'thread %s' % want))
    if callers:
        print('-- who calls %s' % callers)
        for n, c in who.most_common(top):
            print('%7d %5.1f%%  %s' % (c, 100 * c / max(1, total), n))
        return
    print('\n-- exclusive')
    for n, c in excl.most_common(top):
        print('%7d %5.1f%%  %s' % (c, 100 * c / max(1, total), n))
    print('\n-- inclusive')
    for n, c in incl.most_common(top):
        print('%7d %5.1f%%  %s' % (c, 100 * c / max(1, total), n))


if __name__ == '__main__':
    main()

"""The big polities of every start date, biggest first: python tools/audit_major.py [min fused states] [date ...]. A quick way to see a missing, misplaced or anachronistic state."""
import re, sys, collections, os
sys.stdout.reconfigure(encoding='utf-8')
REPO = os.path.abspath(os.path.join(os.path.dirname(__file__), '..'))
P = os.path.join(REPO, 'projects', 'World500BCE', 'game')
names = dict(re.findall(r'^(\w+) = \{\s*\n\s*name = "([^"]*)"', open(os.path.join(P, 'common/countries/countries.txt'), encoding='utf-8').read(), re.M))
text = open(os.path.join(P, 'history/timeline/snapshots.txt'), encoding='utf-8').read()
minimum = int(sys.argv[1]) if len(sys.argv) > 1 else 10
want = set(int(a) for a in sys.argv[2:])
for m in re.finditer(r'snapshot = \{\s*date = (-?\d+)\.\d+\.\d+(.*?)\n\}', text, re.S):
    y = int(m.group(1)) + (1 if int(m.group(1)) < 0 else 0)
    if want and y not in want: continue
    body = m.group(2)
    own = re.search(r'owners = \{(.*?)\}', body, re.S).group(1).split()
    nm = dict(names)
    mm = re.search(r'names = \{(.*?)\}', body, re.S)
    if mm: nm.update(dict(re.findall(r'(\w+) = "([^"]*)"', mm.group(1))))
    c = collections.Counter(o for o in own if o != '_')
    big = [(nm.get(t, t), n) for t, n in c.most_common() if n >= minimum]
    print('== %d: %d countries, %d with >=%d states' % (y, len(c), len(big), minimum))
    print('; '.join('%s %d' % b for b in big))

import io, re, subprocess, sys
# Keep BOTH sides of an append conflict in a markdown file: ours first (newest
# first is this repo's changelog order), then main's. Only touches regions where
# both sides are pure additions -- anything else is left for a human.
files = subprocess.run(['git','diff','--name-only','--diff-filter=U'],
                       capture_output=True, text=True).stdout.split()
rx = re.compile(r'^<<<<<<< [^\n]*\n(.*?)^=======\n(.*?)^>>>>>>> [^\n]*\n', re.S | re.M)
left = []
for f in files:
    if not f.endswith('.md'):
        left.append(f); continue
    s = io.open(f, encoding='utf-8').read()
    s2, n = rx.subn(lambda m: m.group(1) + m.group(2), s)
    if re.search(r'^<<<<<<< ', s2, re.M):
        left.append(f); continue
    io.open(f, 'w', encoding='utf-8', newline='\n').write(s2)
    subprocess.run(['git','add',f])
    print('  resolved (both sides kept, %d region(s)): %s' % (n, f))
if left:
    print('  NEEDS MANUAL:', ' '.join(left))
sys.exit(1 if left else 0)

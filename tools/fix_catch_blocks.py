import re, os, sys

WARN = 'StingLog.Warn($"Suppressed: {ex.Message}");'

def replace_catch(m):
    body = m.group(1)
    bs = body.strip()
    if '/*' in bs or '//' in bs:
        return m.group(0)
    if not bs:
        return 'catch (Exception ex) { ' + WARN + ' }'
    return 'catch (Exception ex) { ' + WARN + ' ' + bs + ' }'

pattern = re.compile(r'\bcatch\s*\{([^{}]*?)\}', re.DOTALL)

changed = []
for root, dirs, files in os.walk('/home/user/STINGTOOLS/StingTools'):
    dirs[:] = [d for d in dirs if d not in ['bin', 'obj']]
    for fname in files:
        if not fname.endswith('.cs'):
            continue
        fpath = os.path.join(root, fname)
        with open(fpath, 'r', encoding='utf-8-sig', errors='replace') as f:
            orig = f.read()
        new = pattern.sub(replace_catch, orig)
        if new != orig:
            with open(fpath, 'w', encoding='utf-8') as f:
                f.write(new)
            changed.append(fpath)

print(f"Changed {len(changed)} files")
for p in sorted(changed):
    print(' ', p.replace('/home/user/STINGTOOLS/StingTools/', ''))

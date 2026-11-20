# -*- coding: utf-8 -*-
"""prune_assets.py : sprites.json 이 쓰지 않는 Assets 하위 폴더/파일을 찾아 _unused 로 옮긴다 (지우지 않는다).
사용: python prune_assets.py <Assets 폴더>            (목록만 보여 줌)
      python prune_assets.py <Assets 폴더> --apply    (실제로 <Assets>\\_unused 로 이동)
기준: sprites.json 의 모든 pattern(걷기/공격/피격/사망/대기, 무기, 앵커)이 가리키는 파일과 atlases 에 적힌 파일.
몬스터/영웅/나무 폴더 안에서는 참조되지 않는 파일만, 폴더 전체가 참조되지 않으면 폴더째 옮긴다.
fx, props, skillicons 는 코드가 직접 읽으므로 건드리지 않는다. 그 밖에 코드가 직접 읽는 폴더가 더 생기면 KEEP 에 더한다.
"""
import json, os, shutil, sys

# 코드가 sprites.json 과 상관없이 직접 읽는 폴더(FxSprites, Decor, SkillIcons)는 후보에서 뺀다.
KEEP = {'fx', 'props', 'skillicons'}

def fmt(p, *a):
    return p.replace('{0:000}', '{0:03d}').replace('{1:00}', '{1:02d}').replace('{0}', '{0}').format(*a)

def used_files(doc, root):
    used = set()
    def add(rel): used.add(rel.replace('\\', '/'))
    for a in doc.get('atlases', []):
        add(a)
        ap = os.path.join(root, a)
        if os.path.exists(ap):
            try:
                j = json.load(open(ap, encoding='utf-8-sig'))
                base = os.path.dirname(a)
                for v in _strings(j):
                    if v.lower().endswith(('.png', '.json')): add((base + '/' + v) if base else v)
            except Exception: pass
    for s in doc['sprites']:
        n = s.get('angles', 1)
        step = 360 // n if n else 360
        angs = [i * step for i in range(n)]
        for k, fk in (('pattern', None), ('hitPattern', None), ('attackPattern', 'attackFrames'),
                      ('deathPattern', 'deathFrames'), ('walkPattern', 'walkFrames'), ('idlePattern', 'idleFrames')):
            if k not in s: continue
            for a in angs:
                if fk:
                    for i in range(s[fk]): add(fmt(s[k], a, i))
                else: add(fmt(s[k], a))
        w = s.get('weapon')
        if w:
            if 'anchors' in w: add(w['anchors'])
            for pk in ('backPattern', 'frontPattern'):
                if pk in w:
                    for pose in range(w.get('poses', 0)):
                        for a in range(n):
                            for form in (lambda: w[pk].format(pose, a), lambda: w[pk].replace('{1:000}', '{1:03d}').format(pose, a * step)):
                                try: add(form())
                                except Exception: pass
    return used

def _strings(o):
    if isinstance(o, str): yield o
    elif isinstance(o, dict):
        for v in o.values(): yield from _strings(v)
    elif isinstance(o, list):
        for v in o: yield from _strings(v)

def main():
    if len(sys.argv) < 2: print(__doc__); return 1
    root = sys.argv[1]; apply = '--apply' in sys.argv
    doc = json.load(open(os.path.join(root, 'sprites.json'), encoding='utf-8-sig'))
    used = used_files(doc, root)
    folders_used = {u.split('/')[0] for u in used if '/' in u}
    cand = []
    for name in sorted(os.listdir(root)):
        p = os.path.join(root, name)
        if name.startswith('_') or name in KEEP or name == 'sprites.json' or not os.path.isdir(p): continue
        if name not in folders_used: cand.append((name, True)); continue
        for dp, dn, fn in os.walk(p):
            for f in fn:
                rel = os.path.relpath(os.path.join(dp, f), root).replace(os.sep, '/')
                if rel not in used: cand.append((rel, False))
    folders = [c for c in cand if c[1]]; files = [c for c in cand if not c[1]]
    print('참조되지 않는 폴더 %d개, 파일 %d개' % (len(folders), len(files)))
    for n, _ in folders: print('  [폴더] ' + n)
    for n, _ in files[:50]: print('  ' + n)
    if len(files) > 50: print('  ... 외 %d개' % (len(files) - 50))
    if not apply: print('\n(목록만 보았습니다. 옮기려면 --apply)'); return 0
    dst = os.path.join(root, '_unused')
    for n, _ in cand:
        s = os.path.join(root, n); d = os.path.join(dst, n)
        os.makedirs(os.path.dirname(d), exist_ok=True); shutil.move(s, d)
    print('이동 완료: ' + dst); return 0

if __name__ == '__main__':
    sys.exit(main())

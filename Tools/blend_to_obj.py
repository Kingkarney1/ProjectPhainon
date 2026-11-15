"""Reads a Blender 5.x .blend (zstd) and writes its mesh objects as OBJ, for an editor with no Blender to import with.

    python3 Tools/blend_to_obj.py SourceArt/Hackathon.blend Assets/Phainon/Art/Models/Hackathon.obj

Mirror modifiers are applied, many-sided faces are cut into triangles, closed pieces that are inside out
are turned, and texture coordinates are written afresh in metres (see below). It handles only what
Hackathon.blend uses: unparented mesh objects, an X mirror about the object or another object.
It stops with an assertion on anything else, so a file it cannot read is never half converted."""
import sys, struct, re, math, subprocess, collections

src, dst = sys.argv[1], sys.argv[2]
# Metres per model unit in the cockpit. Must match CockpitBuilder.ModelScale.
COCKPIT_SCALE = 0.25828
d = subprocess.run(['zstd', '-dc', src], capture_output=True, check=True).stdout
assert d[:7] == b'BLENDER' and d[7:9] == b'17', d[:17]
pos = 17; blocks = []
while pos < len(d):
    code = d[pos:pos+4]; sdna, old, ln, nr = struct.unpack_from('<iQqq', d, pos+4)
    blocks.append((code.rstrip(b'\0').decode('latin1'), sdna, old, ln, nr, pos+32))
    pos += 32 + ln
    if code[:4] == b'ENDB': break
dna = [b for b in blocks if b[0] == 'DNA1'][0]
base = dna[5]; p = base + 8
def strs(p):
    n = struct.unpack_from('<i', d, p)[0]; p += 4; out = []
    for _ in range(n):
        e = d.index(b'\0', p); out.append(d[p:e].decode()); p = e + 1
    return out, base + ((p - base + 3) & ~3)
names, p = strs(p); p += 4
types, p = strs(p); p += 4
tlen = struct.unpack_from('<%dh' % len(types), d, p); p += 2 * len(types); p = base + ((p - base + 3) & ~3)
p += 4
ns = struct.unpack_from('<i', d, p)[0]; p += 4
structs = []; byname = {}
for i in range(ns):
    t, nf = struct.unpack_from('<hh', d, p); p += 4
    fl = []
    for _ in range(nf):
        ft, fn = struct.unpack_from('<hh', d, p); p += 4; fl.append((types[ft], names[fn]))
    structs.append((types[t], fl)); byname[types[t]] = i
tl = dict(zip(types, tlen))
def fsize(ft, fn):
    cnt = 1
    for m in re.findall(r'\[(\d+)\]', fn): cnt *= int(m)
    return (8 if fn.startswith('*') or fn.startswith('(*') else tl[ft]) * cnt
cache = {}
def offsets(sname):
    if sname not in cache:
        off = 0; out = {}
        for ft, fn in structs[byname[sname]][1]:
            out[re.sub(r'\[.*', '', fn).lstrip('*')] = off; off += fsize(ft, fn)
        cache[sname] = out
    return cache[sname]
def cstr(off, n=256): return d[off:off+n].split(b'\0')[0].decode('latin1')
def U(fmt, off): return struct.unpack_from(fmt, d, off)
idn = offsets('ID')['name']
groups = []; cur = None
for b in blocks:
    if b[0] == 'DATA':
        if cur is not None: cur[1].append(b)
    else:
        cur = (b, []); groups.append(cur)
idptr = {g[0][2]: g[0] for g in groups if g[0][2] != 0}
mo, ao, aro, so, oo, mmo = (offsets(n) for n in ('Mesh', 'Attribute', 'AttributeArray', 'AttributeStorage', 'Object', 'MirrorModifierData'))

meshes = {}
for g in groups:
    b = g[0]
    if b[0] != 'ME': continue
    loc = {x[2]: x for x in g[1]}
    off = b[5]; st = off + mo['attribute_storage']
    ap = U('<Q', st + so['dna_attributes'])[0]; an = U('<i', st + so['dna_attributes_num'])[0]
    res = {}
    for i in range(an):
        a = loc[ap][5] + i * tl['Attribute']
        nm = cstr(loc[U('<Q', a + ao['name'])[0]][5])
        if U('<b', a + ao['storage_type'])[0] == 0:
            arr = loc[U('<Q', a + ao['data'])[0]]; raw = U('<Q', arr[5] + aro['data'])[0]; size = U('<q', arr[5] + aro['size'])[0]
            if raw in loc: res[nm] = (loc[raw][5], size, U('<h', a + ao['data_type'])[0])
    v = struct.unpack_from('<%df' % (3 * res['position'][1]), d, res['position'][0])
    cv = struct.unpack_from('<%di' % res['.corner_vert'][1], d, res['.corner_vert'][0])
    npoly = U('<i', off + mo['totpoly'])[0]
    offs = struct.unpack_from('<%di' % (npoly + 1), d, loc[U('<Q', off + mo['poly_offset_indices'])[0]][5])
    uvname = [k for k in res if not k.startswith('.') and k != 'position' and res[k][1] == len(cv)]
    uv = struct.unpack_from('<%df' % (2 * len(cv)), d, res[uvname[0]][0]) if uvname else None
    meshes[b[2]] = ([v[i:i+3] for i in range(0, len(v), 3)], cv, offs, uv)

def matrix(loc, rot, sc):
    # Blender XYZ Euler: R = Rz * Ry * Rx
    cx, sx, cy, sy, cz, sz = math.cos(rot[0]), math.sin(rot[0]), math.cos(rot[1]), math.sin(rot[1]), math.cos(rot[2]), math.sin(rot[2])
    r = [[cy*cz, sx*sy*cz - cx*sz, cx*sy*cz + sx*sz],
         [cy*sz, sx*sy*sz + cx*cz, cx*sy*sz - sx*cz],
         [-sy, sx*cy, cx*cy]]
    return [[r[i][j] * sc[j] for j in range(3)] + [loc[i]] for i in range(3)]
def apply(m, q): return tuple(m[i][0]*q[0] + m[i][1]*q[1] + m[i][2]*q[2] + m[i][3] for i in range(3))
def inverse_rigid_scale(m):
    # general 3x4 affine inverse
    a = [row[:3] for row in m]
    det = (a[0][0]*(a[1][1]*a[2][2]-a[1][2]*a[2][1]) - a[0][1]*(a[1][0]*a[2][2]-a[1][2]*a[2][0]) + a[0][2]*(a[1][0]*a[2][1]-a[1][1]*a[2][0]))
    inv = [[(a[(j+1)%3][(i+1)%3]*a[(j+2)%3][(i+2)%3] - a[(j+1)%3][(i+2)%3]*a[(j+2)%3][(i+1)%3]) / det for j in range(3)] for i in range(3)]
    t = [-(inv[i][0]*m[0][3] + inv[i][1]*m[1][3] + inv[i][2]*m[2][3]) for i in range(3)]
    return [inv[i] + [t[i]] for i in range(3)]

def triangulate(f, pts):
    """Every face is cut into triangles by ear clipping, so a concave face (a wall with a window
    notch) cannot be filled in across its notch by a naive fan, and a folded four-sided face
    gets texture coordinates that are true on both halves."""
    if len(f) <= 3: return [f]
    n = [0.0, 0.0, 0.0]
    for i in range(len(pts)):
        a, b = pts[i], pts[(i+1) % len(pts)]
        n[0] += (a[1]-b[1])*(a[2]+b[2]); n[1] += (a[2]-b[2])*(a[0]+b[0]); n[2] += (a[0]-b[0])*(a[1]+b[1])
    ax = max(range(3), key=lambda i: abs(n[i])); u, v = [(1, 2), (2, 0), (0, 1)][ax]
    sign = 1.0 if n[ax] > 0 else -1.0
    P = [(q[u], q[v]) for q in pts]
    def cross(o, a, b): return ((a[0]-o[0])*(b[1]-o[1]) - (a[1]-o[1])*(b[0]-o[0])) * sign
    idx = list(range(len(f))); tris = []
    while len(idx) > 3:
        for k in range(len(idx)):
            i0, i1, i2 = idx[k-1], idx[k], idx[(k+1) % len(idx)]
            if cross(P[i0], P[i1], P[i2]) <= 1e-12: continue
            if any(j not in (i0, i1, i2) and cross(P[i0], P[i1], P[j]) > 1e-12 and cross(P[i1], P[i2], P[j]) > 1e-12
                   and cross(P[i2], P[i0], P[j]) > 1e-12 for j in idx): continue
            tris.append([f[i0], f[i1], f[i2]]); del idx[k]; break
        else:
            # Degenerate outline (collinear corners): fall back to a fan for what is left.
            tris += [[f[idx[0]], f[idx[i]], f[idx[i+1]]] for i in range(1, len(idx) - 1)]; idx = []
    if len(idx) == 3: tris.append([f[i] for i in idx])
    return tris

objs = {}
for g in groups:
    b = g[0]
    if b[0] != 'OB': continue
    off = b[5]
    objs[b[2]] = dict(name=cstr(off + idn)[2:], data=U('<Q', off + oo['data'])[0],
                      m=matrix(U('<3f', off + oo['loc']), U('<3f', off + oo['rot']), U('<3f', off + oo['scale' if 'scale' in oo else 'size'])),
                      parent=U('<Q', off + oo['parent'])[0], mods=[x for x in g[1] if structs[x[1]][0].endswith('ModifierData')])
out = ['# Converted from Hackathon.blend (Blender 5.2). Axes as Blender\'s OBJ exporter writes them: Y up, -Z forward.']
vbase = 1; tbase = 1; summary = []
for key, o in objs.items():
    if o['data'] not in meshes: continue
    assert o['parent'] not in idptr, 'parented objects are not handled'
    vs, cv, offs, uv = meshes[o['data']]
    world = [apply(o['m'], q) for q in vs]
    faces = [list(range(offs[i], offs[i+1])) for i in range(len(offs) - 1)]   # corner indices
    parts = [(world, False)]
    for mod in o['mods']:
        kind = structs[mod[1]][0]
        assert kind == 'MirrorModifierData', kind
        flag = U('<h', mod[5] + mmo['flag'])[0]
        assert flag & (1 << 3) and not flag & ((1 << 4) | (1 << 5)), 'only an X mirror is handled'
        mob = U('<Q', mod[5] + mmo['mirror_ob'])[0]
        frame = objs[mob]['m'] if mob in objs else o['m']
        # Mirror in the frame's local X: to frame space, negate x, back to world.
        inv = inverse_rigid_scale(frame)
        def mirror(q, frame=frame, inv=inv):
            l = apply(inv, q); return apply(frame, (-l[0], l[1], l[2]))
        parts.append(([mirror(q) for q in world], True))
    out.append('o ' + o['name'].replace(' ', '_'))
    out.append('g ' + o['name'].replace(' ', '_'))
    nv = 0
    for pts, flipped in parts:
        for x, y, z in pts: out.append('v %.6f %.6f %.6f' % (x, z, -y))
        nv += len(pts)
    allp = [q for pts, _ in parts for q in pts]
    tris = []          # (corner vertex indices into allp, corner uv indices)
    for k, (pts, flipped) in enumerate(parts):
        for f in [t for f0 in faces for t in triangulate(f0, [world[cv[c]] for c in f0])]:
            order = list(reversed(f)) if flipped else f
            tris.append(([k*len(vs) + cv[c] for c in order], list(order)))
    # Unity draws one side of a face. A closed piece whose faces point inwards would show its far
    # side, so such pieces are turned the right way out. Open surfaces (the cabin shell) are left alone.
    root = list(range(len(allp)))
    def find(i):
        while root[i] != i: root[i] = root[root[i]]; i = root[i]
        return i
    for vi, _ in tris:
        for i in vi[1:]: root[find(i)] = find(vi[0])
    pieces = collections.defaultdict(list)
    for t in tris: pieces[find(t[0][0])].append(t)
    turned = 0
    for piece in pieces.values():
        edges = collections.Counter()
        for vi, _ in piece:
            for i in range(len(vi)): edges[tuple(sorted((vi[i], vi[(i+1) % len(vi)])))] += 1
        if any(n != 2 for n in edges.values()): continue
        pts = {i for vi, _ in piece for i in vi}
        c = [sum(allp[i][k] for i in pts) / len(pts) for k in range(3)]
        vol = 0.0
        for vi, _ in piece:
            q = [[allp[i][k] - c[k] for k in range(3)] for i in vi]
            for j in range(1, len(q) - 1):
                a_, b_, c_ = q[0], q[j], q[j+1]
                vol += (a_[0]*(b_[1]*c_[2]-b_[2]*c_[1]) - a_[1]*(b_[0]*c_[2]-b_[2]*c_[0]) + a_[2]*(b_[0]*c_[1]-b_[1]*c_[0])) / 6.0
        if vol < 0:
            for t in piece: t[0].reverse(); t[1].reverse()
            turned += 1
    # Texture coordinates. The file's own are collapsed (most faces have no area in them), so each face
    # is projected along whichever axis it faces most squarely, in metres at cockpit scale. A tiled
    # finish then has the same size everywhere and is never stretched.
    nvt = 0
    for vi, ti in tris:
        q = [allp[i] for i in vi]
        e1 = [q[1][k] - q[0][k] for k in range(3)]; e2 = [q[-1][k] - q[0][k] for k in range(3)]
        n = (e1[1]*e2[2] - e1[2]*e2[1], e1[2]*e2[0] - e1[0]*e2[2], e1[0]*e2[1] - e1[1]*e2[0])
        ax = max(range(3), key=lambda k: abs(n[k])); a, b = [(1, 2), (0, 2), (0, 1)][ax]
        for c in q: out.append('vt %.5f %.5f' % (c[a] * COCKPIT_SCALE, c[b] * COCKPIT_SCALE))
        out.append('f ' + ' '.join('%d/%d' % (vbase + v_, tbase + nvt + k) for k, v_ in enumerate(vi)))
        nvt += len(vi)
    xs, ys, zs = zip(*allp)
    summary.append('%-18s verts %4d faces %4d pieces %2d turned %d  x[%.3f,%.3f] y[%.3f,%.3f] z[%.3f,%.3f]' % (o['name'], nv, len(tris), len(pieces), turned, min(xs), max(xs), min(ys), max(ys), min(zs), max(zs)))
    vbase += nv; tbase += nvt
for g in groups:
    b = g[0]
    if b[0] == 'OB' and cstr(b[5] + idn) == 'OBCamera':
        summary.append('camera at blender %s' % (U('<3f', b[5] + oo['loc']),))
open(dst, 'w').write('\n'.join(out) + '\n')
print('\n'.join(summary))

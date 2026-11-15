"""Derives the cabin's panel joints, window seals and fasteners from the cabin model itself.

    python3 -I Tools/cockpit_detail.py Assets/Phainon/Art/Models/Hackathon.obj Assets/Phainon/Art/Models/HackathonDetail.obj

The model is a blockout: it has flat faces and fold lines, but no modelled panel joints. Nothing is
placed by hand here. Every detail is computed from the shell's real geometry, so it cannot sit
where the model has no edge or no surface:

  Seams      a narrow dark gap along every fold line, where two flat faces meet at an angle.
  Gaskets    a rubber strip round each window opening, along the opening's own edge.
  Fasteners  screw heads set in from the border of each large flat face, at an even pitch.

The output uses the same axes and units as the model, so the game places both with one transform.
The script checks its own result and stops with an assertion if a detail is off its surface.
"""
import sys, math, collections

SCALE = 0.25828                 # metres per model unit in the cockpit; must match CockpitBuilder.ModelScale
def mm(x): return x / 1000.0 / SCALE
EYE = (0.0, 1.2535, 0.0789)     # the pilot's eye in the model's axes; details face it

FOLD_DEGREES = 10.0             # faces meeting at more than this are separate panels
SEAM_HALF = mm(2.2)             # half the width of a panel gap
SEAM_LIFT = mm(0.4)
GASKET_WIDTH = mm(14.0)
GASKET_LIFT = mm(1.6)
PANEL_MIN_AREA = 0.07 / SCALE ** 2      # only faces of at least 0.07 square metres get fasteners
INSET = mm(26.0)                # fasteners sit this far in from the panel's edge
PITCH = mm(240.0)               # and about this far apart
CLEARANCE = mm(15.0)            # never closer than this to any edge of the panel
HEAD_RADIUS = mm(4.2)
HEAD_HEIGHT = mm(1.0)
SIDES = 6                       # a hexagonal head

def sub(a, b): return (a[0]-b[0], a[1]-b[1], a[2]-b[2])
def add(a, b): return (a[0]+b[0], a[1]+b[1], a[2]+b[2])
def mul(a, s): return (a[0]*s, a[1]*s, a[2]*s)
def dot(a, b): return a[0]*b[0] + a[1]*b[1] + a[2]*b[2]
def cross(a, b): return (a[1]*b[2]-a[2]*b[1], a[2]*b[0]-a[0]*b[2], a[0]*b[1]-a[1]*b[0])
def length(a): return math.sqrt(dot(a, a))
def unit(a):
    l = length(a); return mul(a, 1.0 / l) if l > 1e-12 else (0.0, 0.0, 0.0)

def read_shell(path):
    verts, tris, name, index = [], [], None, {}
    raw = []
    for line in open(path):
        t = line.split()
        if not t: continue
        if t[0] == 'o': name = t[1]
        elif t[0] == 'v': raw.append(tuple(float(q) for q in t[1:4]))
        elif t[0] == 'f' and name == 'Plane':
            ids = []
            for q in t[1:]:
                p = raw[int(q.split('/')[0]) - 1]; key = tuple(round(c, 4) for c in p)
                if key not in index: index[key] = len(verts); verts.append(p)
                ids.append(index[key])
            assert len(ids) == 3, 'the model must be triangulated'
            if len(set(ids)) == 3: tris.append(tuple(ids))
    return verts, tris

def main(src, dst):
    V, T = read_shell(src)
    normal = []
    for a, b, c in T:
        n = unit(cross(sub(V[b], V[a]), sub(V[c], V[a])))
        centre = mul(add(add(V[a], V[b]), V[c]), 1.0 / 3.0)
        if dot(n, sub(EYE, centre)) < 0: n = mul(n, -1.0)      # the side the pilot sees
        normal.append(n)
    area = [0.5 * length(cross(sub(V[b], V[a]), sub(V[c], V[a]))) for a, b, c in T]

    edge_tris = collections.defaultdict(list)
    for i, (a, b, c) in enumerate(T):
        for e in ((a, b), (b, c), (c, a)): edge_tris[tuple(sorted(e))].append(i)

    # Flat faces: triangles joined across edges where the surface does not fold.
    parent = list(range(len(T)))
    def find(i):
        while parent[i] != i: parent[i] = parent[parent[i]]; i = parent[i]
        return i
    cos_fold = math.cos(math.radians(FOLD_DEGREES))
    for e, ts in edge_tris.items():
        if len(ts) == 2 and area[ts[0]] > 1e-9 and area[ts[1]] > 1e-9 and dot(normal[ts[0]], normal[ts[1]]) > cos_fold:
            parent[find(ts[0])] = find(ts[1])
    faces = collections.defaultdict(list)
    for i in range(len(T)):
        if area[i] > 1e-9: faces[find(i)].append(i)

    out = {'Seams': [], 'Gaskets': [], 'Fasteners': []}     # lists of (quad or triangle corner tuples)
    def strip(kind, a, b, inward, n, width, lift):
        """A flat strip on a face, from the edge a-b inwards, facing the pilot."""
        up = mul(n, lift)
        q = [add(a, up), add(b, up), add(add(b, mul(inward, width)), up), add(add(a, mul(inward, width)), up)]
        if dot(cross(sub(q[1], q[0]), sub(q[2], q[0])), n) < 0: q.reverse()
        out[kind].append(q)

    def inward_on(tri, e):
        """The direction across the triangle's own surface, away from its edge e."""
        third = [v for v in T[tri] if v not in e][0]
        along = unit(sub(V[e[1]], V[e[0]]))
        m = sub(sub(V[third], V[e[0]]), mul(along, dot(sub(V[third], V[e[0]]), along)))
        return unit(m)

    seams = gaskets = 0
    open_edges = []
    for e, ts in edge_tris.items():
        ts = [t for t in ts if area[t] > 1e-9]
        if len(ts) == 2 and find(ts[0]) != find(ts[1]):
            for t in ts: strip('Seams', V[e[0]], V[e[1]], inward_on(t, e), normal[t], SEAM_HALF, SEAM_LIFT)
            seams += 1
        elif len(ts) == 1: open_edges.append((e, ts[0]))

    # Window openings: loops of open edges ahead of the seat. (The model is also open behind the seat; that is not a window.)
    adjacent = collections.defaultdict(list)
    for e, t in open_edges: adjacent[e[0]].append((e, t)); adjacent[e[1]].append((e, t))
    seen, windows = set(), 0
    for start in list(adjacent):
        if start in seen: continue
        loop, stack, edges = [], [start], set()
        while stack:
            v = stack.pop()
            if v in seen: continue
            seen.add(v); loop.append(v)
            for e, t in adjacent[v]:
                edges.add((e, t)); stack.append(e[0] if e[1] == v else e[1])
        forward = sum(-V[v][2] for v in loop) / len(loop)       # the model looks along -Z
        if len(loop) > 20 or forward < 1.0: continue
        windows += 1
        for e, t in edges:
            strip('Gaskets', V[e[0]], V[e[1]], inward_on(t, e), normal[t], GASKET_WIDTH, GASKET_LIFT); gaskets += 1

    # Fasteners on each large flat face, set in from its border.
    panels = heads = 0
    placed = []
    for members in faces.values():
        if sum(area[i] for i in members) < PANEL_MIN_AREA: continue
        n = unit(tuple(sum(normal[i][k] * area[i] for i in members) for k in range(3)))
        origin = V[T[members[0]][0]]
        ux = unit(cross(n, (0.0, 1.0, 0.0)) if abs(n[1]) < 0.9 else cross(n, (1.0, 0.0, 0.0)))
        uy = cross(n, ux)
        def flat(p): d = sub(p, origin); return (dot(d, ux), dot(d, uy))
        border = collections.Counter()
        for i in members:
            a, b, c = T[i]
            for e in ((a, b), (b, c), (c, a)): border[tuple(sorted(e))] += 1
        border = [e for e, k in border.items() if k == 1]
        segments = [(flat(V[a]), flat(V[b])) for a, b in border]
        # A straight side of the panel is often several model edges end to end. Fasteners are spaced
        # along the whole side, so the edges are first joined into straight runs.
        at = collections.defaultdict(list)
        for a, b in border: at[a].append(b); at[b].append(a)
        runs, used = [], set()
        def straight(a, v, b):
            p, q, r = flat(V[a]), flat(V[v]), flat(V[b])
            return abs((q[0]-p[0])*(r[1]-q[1]) - (q[1]-p[1])*(r[0]-q[0])) < 1e-4 * (math.hypot(q[0]-p[0], q[1]-p[1]) * math.hypot(r[0]-q[0], r[1]-q[1]) + 1e-9) * 50
        for a, b in border:
            if (a, b) in used: continue
            chain = [a, b]; used.add((a, b)); used.add((b, a))
            for end in (1, 0):
                while True:
                    tip = chain[-1] if end else chain[0]; before = chain[-2] if end else chain[1]
                    onward = [w for w in at[tip] if w != before and (tip, w) not in used]
                    if len(at[tip]) != 2 or len(onward) != 1 or not straight(before, tip, onward[0]): break
                    used.add((tip, onward[0])); used.add((onward[0], tip))
                    if end: chain.append(onward[0])
                    else: chain.insert(0, onward[0])
            runs.append((flat(V[chain[0]]), flat(V[chain[-1]])))
        triangles = [[flat(V[v]) for v in T[i]] for i in members]
        def inside(p):
            for a, b, c in triangles:
                d1 = (b[0]-a[0])*(p[1]-a[1]) - (b[1]-a[1])*(p[0]-a[0])
                d2 = (c[0]-b[0])*(p[1]-b[1]) - (c[1]-b[1])*(p[0]-b[0])
                d3 = (a[0]-c[0])*(p[1]-c[1]) - (a[1]-c[1])*(p[0]-c[0])
                if (d1 >= 0 and d2 >= 0 and d3 >= 0) or (d1 <= 0 and d2 <= 0 and d3 <= 0): return True
            return False
        def clearance(p):
            best = 1e9
            for a, b in segments:
                dx, dy = b[0]-a[0], b[1]-a[1]; l2 = dx*dx + dy*dy
                t = 0.0 if l2 < 1e-12 else max(0.0, min(1.0, ((p[0]-a[0])*dx + (p[1]-a[1])*dy) / l2))
                best = min(best, math.hypot(p[0]-a[0]-t*dx, p[1]-a[1]-t*dy))
            return best
        spots = []
        for a, b in runs:
            dx, dy = b[0]-a[0], b[1]-a[1]; run = math.hypot(dx, dy)
            if run < 2.2 * INSET: continue
            ex, ey = dx / run, dy / run
            count = max(1, int(round((run - 2 * INSET) / PITCH)))
            for side in (1.0, -1.0):                            # try both sides; only the one inside the panel survives
                for k in range(count + 1):
                    along = INSET + (run - 2 * INSET) * k / count
                    p = (a[0] + ex*along - ey*INSET*side, a[1] + ey*along + ex*INSET*side)
                    if inside(p) and clearance(p) >= CLEARANCE and all(math.hypot(p[0]-q[0], p[1]-q[1]) > mm(40.0) for q in spots):
                        spots.append(p)
        if len(spots) < 3: continue                             # too narrow to be a fitted panel
        panels += 1
        for p in spots:
            assert inside(p) and clearance(p) >= CLEARANCE - 1e-9
            centre = add(origin, add(mul(ux, p[0]), mul(uy, p[1])))
            assert abs(dot(sub(centre, origin), n)) < 1e-6, 'a fastener is off its panel'
            placed.append(centre); heads += 1
            top = add(centre, mul(n, HEAD_HEIGHT)); ring_low, ring_top = [], []
            for k in range(SIDES):
                ang = math.pi * 2 * k / SIDES
                r = add(mul(ux, math.cos(ang) * HEAD_RADIUS), mul(uy, math.sin(ang) * HEAD_RADIUS))
                ring_low.append(add(centre, r)); ring_top.append(add(top, mul(r, 0.82)))
            for k in range(SIDES):
                j = (k + 1) % SIDES
                for q in ([ring_low[k], ring_low[j], ring_top[j], ring_top[k]], [top, ring_top[k], ring_top[j]]):
                    if dot(cross(sub(q[1], q[0]), sub(q[2], q[0])), sub(mul(add(add(q[0], q[1]), q[2]), 1/3.0), centre)) < 0: q = list(reversed(q))
                    out['Fasteners'].append(q)

    assert seams > 50 and windows == 4 and heads > 40, (seams, windows, heads)
    lines = ['# Panel joints, window seals and fasteners, derived from Hackathon.obj by Tools/cockpit_detail.py. Do not edit by hand.']
    count = 1
    for kind in ('Seams', 'Gaskets', 'Fasteners'):
        lines.append('o ' + kind); lines.append('g ' + kind)
        for q in out[kind]:
            n = cross(sub(q[1], q[0]), sub(q[2], q[0]))
            ax = max(range(3), key=lambda k: abs(n[k])); a, b = [(1, 2), (0, 2), (0, 1)][ax]
            for p in q:
                lines.append('v %.5f %.5f %.5f' % p); lines.append('vt %.5f %.5f' % (p[a] * SCALE, p[b] * SCALE))
            lines.append('f ' + ' '.join('%d/%d' % (count + k, count + k) for k in range(len(q))))
            count += len(q)
    open(dst, 'w').write('\n'.join(lines) + '\n')
    print('flat faces %d, of which %d are panels with fasteners' % (len(faces), panels))
    print('seams along %d fold lines, gaskets round %d window openings (%d edges), %d fasteners' % (seams, windows, gaskets, heads))
    print('triangles and quads written: %d' % sum(len(v) for v in out.values()))

if __name__ == '__main__':
    main(sys.argv[1], sys.argv[2])

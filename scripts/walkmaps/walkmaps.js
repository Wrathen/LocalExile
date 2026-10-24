// Walk-map capture (Frida, loaded by walkmaps.py into the running client).
//
// When the client generates an area it builds a terrain walkability grid before drawing it. Right after the grid
// provider returns (client 3.29.3b, VA 0x1410ddcfd, just after the vtable +0x50 call in 0x1410ddb90) rax points to
//   { i32 width; i32 height; vector<u8> cells [+0x08 begin, +0x10 end]; vector<u8> second [+0x20, +0x28] }.
// The first vector is width × height bytes, one per cell; the client walks on cells >= 2. The second, the same size,
// stops projectiles where it is 0 (walls, doodads; water is open there).
// Each grid is sent to walkmaps.py, which names it after the area the Gateway is serving.

const IMAGE_BASE = ptr('0x140000000');
const HOOK_VA = ptr('0x1410ddcfd');
// The instructions at the hook in the 3.29.3b client (mov rcx,[rax]; mov [rbp-0x38],rcx; lea rdx,[rax+8]; ...).
// Nothing is hooked unless they match, so another client build is left untouched.
const EXPECTED = '488b0848894dc8488d5008488d4dd048';

// The client executable: the .exe module (Process.mainModule can report a driver DLL when attaching late).
const exe = Process.enumerateModules().find(m => m.name.toLowerCase().endsWith('.exe'));
if (!exe) {
  send({ type: 'refused', reason: 'no .exe module in the process' });
} else {
  const hook = exe.base.add(HOOK_VA.sub(IMAGE_BASE));
  const inside = hook.compare(exe.base) >= 0 && hook.add(EXPECTED.length / 2).compare(exe.base.add(exe.size)) <= 0;
  const bytes = inside ? Array.from(new Uint8Array(hook.readByteArray(EXPECTED.length / 2)))
    .map(b => b.toString(16).padStart(2, '0')).join('') : '';
  if (bytes !== EXPECTED) {
    send({ type: 'refused', reason: `${exe.name} is not the 3.29.3b client (code at the hook does not match)` });
  } else {
    Interceptor.attach(hook, function () {
      try {
        const r = this.context.rax;
        const w = r.readS32(), h = r.add(4).readS32();
        const begin = r.add(0x08).readPointer(), end = r.add(0x10).readPointer();
        const n = end.sub(begin).toInt32();
        if (w <= 0 || h <= 0 || n !== w * h) {
          send({ type: 'skip', w, h, bytes: n });
          return;
        }
        send({ type: 'grid', w, h }, begin.readByteArray(n));
        // The second vector, same size: what stops projectiles (0 blocks; water and the like are 1..5 there).
        const b2 = r.add(0x20).readPointer(), n2 = r.add(0x28).readPointer().sub(b2).toInt32();
        if (n2 === w * h) send({ type: 'projectile', w, h }, b2.readByteArray(n2));
      } catch (e) {
        send({ type: 'error', message: String(e) });
      }
    });
    hookTileObjects(exe);
    hookDoodads(exe);
    hookGeneration(exe);
    send({ type: 'ready', module: exe.name });
  }
}

// Area transitions and waypoints of the generated layout (Agents/docs/protocol/08-area-transitions.md §5).
// While generating, 0x141d7f700 walks each placed tile's .tmd records and calls 0x1420415e0(ctx, record):
//   ctx[1] → placed tile: +0x30 i16 height offset (steps of 7.8125), +0x36 u8 rotation/flip code 0..7
//   ctx[2] → i32 x, i32 y: the tile's grid origin;  ctx[3] → handle, *handle+0x28 → +0x60/+0x61 u8 size in tiles
//   record: i32 x, i32 y (tile-local cell), f32 z (+0x10), f32 angle (+0x14); +0x30 type, +0x38 .ao (handle+8 → path)
// The client spawns only its own doodads from them; server types (AreaTransition …) are skipped there, so their
// placement is computed here the way 0x14202f1c0 does it, with the constants read from the client.
function hookTileObjects(exe) {
  const at = va => exe.base.add(ptr(va).sub(IMAGE_BASE));
  const hook = at('0x1420415e0');
  const bytes = Array.from(new Uint8Array(hook.readByteArray(16))).map(b => b.toString(16).padStart(2, '0')).join('');
  if (bytes !== '488bc448895808555657415441554156') {
    send({ type: 'refused', reason: 'tile object hook: code does not match 3.29.3b' });
    return;
  }
  const codes = Array.from(new Uint8Array(at('0x142fa04b0').readByteArray(24)));
  const quarter = at('0x1436a3318').readFloat(), turn = at('0x1436a3674').readFloat();
  const noHeight = at('0x1436a48d4').readFloat();
  const path = h => { try { return h.isNull() ? null : h.add(8).readPointer().readUtf16String(); } catch (e) { return null; } };
  Interceptor.attach(hook, {
    onEnter(a) {
      try {
        const ctx = a[0], rec = a[1];
        const type = path(rec.add(0x30).readPointer());
        // Quest exits extend AreaTransition under their own name: The Twilight Strand's way into Lioneye's Watch is
        // Metadata/QuestObjects/SouthBeachTownEntrance (12-quests.md).
        if (!type || !/AreaTransition|Waypoint$|TownEntrance$/.test(type)) return;
        const tile = ctx.add(8).readPointer();
        const code = Math.min(tile.add(0x36).readU8(), 8), heightOffset = tile.add(0x30).readS16();
        const origin = ctx.add(0x10).readPointer();
        const info = ctx.add(0x18).readPointer().readPointer().add(0x28).readPointer();
        const w = info.add(0x60).readU8(), h = info.add(0x61).readU8();
        const x = rec.readS32(), y = rec.add(4).readS32();
        const c = code < 8 ? codes.slice(code * 3, code * 3 + 3) : [0, 1, 1];
        const cells = [w * 23 - x - 1, x, h * 23 - y - 1, y];
        const gx = cells[c[1] + 2 * c[0]] + origin.readS32(), gy = cells[(c[0] === 0 ? 2 : 0) + c[2]] + origin.add(4).readS32();
        let rotation = rec.add(0x14).readFloat();
        if (code >= 4 && code < 8) rotation = turn - rotation;
        rotation += (code & 3) * quarter;
        while (rotation < 0) rotation += turn;
        while (rotation > turn) rotation -= turn;
        const z = rec.add(0x10).readFloat();
        send({ type: 'object', objectType: type, model: path(rec.add(0x38).readPointer()), x: gx, y: gy, rotation,
               z: z === noHeight ? null : z - heightOffset * 7.8125, rawZ: z, heightOffset, code });
      } catch (e) {
        send({ type: 'error', message: 'tile object: ' + String(e) });
      }
    },
  });
}

// Every doodad the client places, rooms' doodads included (Agents/docs/protocol/14-monster-packs.md §3). 0x142042360
// (record, …) creates one client-side doodad from a placed record laid out like a .tmd record, already in world terms:
// i32 grid x, i32 grid y, f32 world x, f32 world y, f32 z (+0x10), f32 angle (+0x14), type handle +0x30, .ao handle +0x38.
// The Gateway matches them against data/room-points.json to find where rooms with monster points ("mapboss") were put.
// Sent in batches; walkmaps.py writes <area>_seed<seed>.doodads.json.
function hookDoodads(exe) {
  const hook = exe.base.add(ptr('0x142042360').sub(IMAGE_BASE));
  const bytes = Array.from(new Uint8Array(hook.readByteArray(16))).map(b => b.toString(16).padStart(2, '0')).join('');
  if (bytes !== '4c8bdc49895310535657415441554156') {
    send({ type: 'refused', reason: 'doodad hook: code does not match 3.29.3b (or another script hooks it)' });
    return;
  }
  const path = h => { try { return h.isNull() ? null : h.add(8).readPointer().readUtf16String(); } catch (e) { return null; } };
  let batch = [], timer = null;
  const flush = () => { timer = null; if (batch.length) { send({ type: 'doodads', doodads: batch }); batch = []; } };
  Interceptor.attach(hook, {
    onEnter(a) {
      try {
        const r = a[1];
        const model = path(r.add(0x38).readPointer());
        if (!model) return;
        batch.push([model, r.readS32(), r.add(4).readS32(), r.add(0x14).readFloat()]);
        if (batch.length >= 500) flush();
        else if (timer === null) timer = setTimeout(flush, 200);
      } catch (e) {
        send({ type: 'error', message: 'doodad: ' + String(e) });
      }
    },
  });
}

// The level generator's trace (Agents/docs/re/011-level-generation.md §8): while 0x14204d820 generates an area, every
// random draw on its thread, then the placed tile grid. walkmaps.py writes <area>_seed<seed>.gen.json, which
// `dotnet run --project scripts/re/levelgen -- compare` checks the server's generator against, draw by draw.
//   draws: [kind, caller VA, argument, result]; kind 'n' = 0x141ff9720(n), 'u' = 0x141f5d130(n) called directly,
//   'r' = a raw mt19937 draw (0x140a0f990) not made by those two, 'l' = LCG bits (0x140e3b310).
//   tiles: [x, y, .tdt path, height (+0x30), rotation (+0x36), flags +0x34, +0x35] for every non-empty grid entry.
function hookGeneration(exe) {
  const at = va => exe.base.add(ptr(va).sub(IMAGE_BASE));
  const hooks = {
    generate: ['0x14204d820', '4c8bdc49894b08535657415441554156'],
    next: ['0x141ff9720', '48895c24084889742410574883ec3065'],
    uniform: ['0x141f5d130', '48895c240848896c2410488974241848'],
    raw: ['0x140a0f990', '448b1941b9700200004c8bc1453bd975'],
    lcg: ['0x140e3b310', '4883ec084533d285c9747048891c248b'],
  };
  for (const [name, [va, expected]] of Object.entries(hooks)) {
    const bytes = Array.from(new Uint8Array(at(va).readByteArray(16))).map(b => b.toString(16).padStart(2, '0')).join('');
    if (bytes !== expected) {
      send({ type: 'refused', reason: `generation trace: ${name} (${va}) does not match 3.29.3b` });
      return;
    }
  }
  const va = p => '0x' + p.sub(exe.base).add(IMAGE_BASE).toString(16);
  const inside = (p, start, size) => p.compare(at(start)) >= 0 && p.compare(at(start).add(size)) < 0;
  let thread = null, draws = [];
  const wstring = s => s.add(0x18).readU64().compare(7) > 0 ? s.readPointer().readUtf16String() : s.readUtf16String();

  Interceptor.attach(at(hooks.generate[0]), {
    onEnter(a) {
      this.gen = a[0];
      if (thread !== null) return;
      thread = this.threadId;
      draws = [];
      graphStages = [];
      relaxRecords = [];
      fillVertices = null;
      paint = null;
      dungeon = null;
      dungeons = [];
      subgraphs = [];
      dungeonPlaced = [];
      dungeonStages = [];
      dungeonSizes = [];
      dungeonMap = null;
      dungeonFills = [];
      heightStages = [];
      currentGen = a[0];
      this.seed = a[0].add(0x30).readPointer().readU32();
    },
    onLeave() {
      if (this.threadId !== thread) return;
      thread = null;
      try {
        const gen = this.gen, world = gen.add(0x38).readPointer();
        const row = world.add(0xb0).readPointer();
        const area = row.isNull() ? '' : row.readPointer().readUtf16String();
        const grid = gen.add(0x28).readPointer();
        const w = grid.add(0x18).readU64().toNumber(), h = grid.add(0x20).readU64().toNumber();
        const entries = grid.add(0x28).readPointer();
        const tiles = [];
        for (let y = 0; y < h; y++)
          for (let x = 0; x < w; x++) {
            const e = entries.add((y * w + x) * 0x38);
            if (e.readPointer().isNull()) continue;
            const def = e.add(8).readPointer();
            tiles.push([x, y, def.isNull() ? null : wstring(def.add(8)), e.add(0x30).readS16(), e.add(0x36).readU8(),
                        e.add(0x34).readU8(), e.add(0x35).readU8()]);
          }
        send({ type: 'generation', area, level: world.add(0xd4).readU8(), seed: this.seed, finalSeed: gen.add(0x30).readPointer().add(4).readU32(),
               tileHash: world.add(0x114).readU32(), w, h, tiles, draws, graphs: graphStages, relaxRecords, fillVertices, paint, dungeon, dungeons, subgraphs, fills: dungeonFills, heights: heightStages });
      } catch (e) {
        send({ type: 'error', message: 'generation: ' + String(e) });
      }
      draws = [];
      graphStages = [];
      relaxRecords = [];
      fillVertices = null;
      paint = null;
      dungeon = null;
      dungeons = [];
      subgraphs = [];
      dungeonPlaced = [];
      dungeonStages = [];
      dungeonSizes = [];
      dungeonMap = null;
      dungeonFills = [];
      heightStages = [];
      currentGen = null;
    },
  });
  const traced = (kind, va0) => Interceptor.attach(at(va0), {
    onEnter(a) {
      this.on = this.threadId === thread;
      if (!this.on) return;
      this.caller = this.returnAddress;
      this.arg = a[1] !== undefined && kind === 'u' ? a[1].toUInt32() : a[0].toUInt32();
    },
    onLeave(r) {
      if (!this.on) return;
      // Skip draws made inside the other traced helpers (next → uniform → raw): they are part of one entry.
      if (kind === 'u' && inside(this.caller, hooks.next[0], 0xcc)) return;
      if (kind === 'r' && (inside(this.caller, hooks.uniform[0], 0x99) || inside(this.caller, hooks.next[0], 0xcc))) return;
      draws.push([kind, va(this.caller), this.arg, r.toUInt32()]);
    },
  });
  traced('n', hooks.next[0]);
  traced('u', hooks.uniform[0]);
  traced('r', hooks.raw[0]);
  traced('l', hooks.lcg[0]);
  hookGraphStages(exe, () => thread, () => draws.length);
}

// Tile graph (.tgr) snapshots at the layout's stage boundaries (Agents/docs/re/011-level-generation.md §3e), added to
// the generation trace as `graphs`: [stage, draws so far, nodes, edges]. Graph +0x10 edges, +0x28 nodes (vectors of
// pointers). Node: +0x4c x, +0x50 y, +0x20 tag (std::wstring), +0x48 height, +0x58..+0x70 edge slots, +0x88 placed.
// Edge: +8 internal points (i32 x, y), +0x20 / +0x28 nodes, +0x38 type, +0x48 / +0x49 v-end set, +0x4a, +0x4c v-end
// points (A: 2 points, B: 2 points), +0x6d split, +0x70 / +0x78 end offsets, +0x80 / +0x84 min / max width.
var graphStages = [];
// Per relaxation (0x1421239d0), its placement records (0x98 bytes each, the vector in its second argument): the node's
// position (+0x78), the room's tile position (+0x70 / +0x74), size (+0x40 / +0x41), slot sides (+0x80..+0x8c), align (+0x91).
var relaxRecords = [];
// The view's vertices as the fill (0x141fef070) starts: [ground index, height, flags] row by row over
// (w + 1) × (h + 1), with the ground paths by index. View: +0 map, +8 / +0xc offset, +0x18 / +0x1c size; map: +0x40
// vertex stride, +0x50 vertices (3 bytes: ground index, height, flags), +0x68 ground handles (+8 path).
var fillVertices = null;
// 0x142110d60 (the level painting): its two levels, the grounds at layout +0x20 → +0xa0 / +0xa8 / +0xb0, and the view's
// vertices as it returns (as fillVertices).
var paint = null;
// Dungeon graphs: the layout as 0x1421038d0 returns, and the rooms in the order they were placed (see below).
var dungeon = null, dungeonPlaced = [], dungeonStages = [], dungeonSizes = [], dungeonMap = null;
// Composite maps: every layout of the load in order (dungeon is the last one), and every subgraph as 0x1420452e0
// starts it: {index, draws, path, x, y, w, h, flags (+0x90), parent (+0x92), count (records in the list)}.
var dungeons = [], subgraphs = [];
// Dungeon tilising fill records: ['r', draws, x, y, w, h, height, accessible] as 0x1420eec10 records them, then
// ['f', draws, x, y, height, accessible, placed height] as 0x1420f3de0 fills each (placed height null: already taken).
var dungeonFills = [];
// Tile heights (entry +0x30, null for an empty entry) row by row at generation stages: [stage, draws, heights].
var heightStages = [], currentGen = null;
function hookGraphStages(exe, currentThread, drawCount) {
  const at = va => exe.base.add(ptr(va).sub(IMAGE_BASE));
  const stages = [
    ['special', '0x1421225d0', '48895c24184889742420555741544156', a => a[0].add(0x78).readPointer().add(0x20).readPointer()],
    // Taken on entry: the graph as the relaxation sees it (special and plain nodes placed, not yet pushed).
    ['relax', '0x1421239d0', '488bc448895810448840184889480855', a => a[0].add(0x78).readPointer().add(0x20).readPointer(), true],
    ['route', '0x142125790', '48895c24185556574154415541564157', a => a[0].readPointer()],
    ['corners', '0x142114f00', '48895c2410488974241855574156488d', a => a[0].add(0x20).readPointer()],
    ['virtualNode', '0x14211e9b0', '4885d20f840601000048894c24085553', a => a[0].add(0x78).readPointer().add(0x20).readPointer()],
    ['split', '0x142113c10', '48895c2410488974242055574156488d', a => a[0].add(0x20).readPointer()],
    ['merge', '0x1421148c0', '48895c24185556574881ecb000000048', a => a[0].add(0x20).readPointer()],
  ];
  const wstring = s => s.add(0x18).readU64().compare(7) > 0 ? s.readPointer().readUtf16String() : s.readUtf16String();
  const vector = (p, size) => {
    const out = [];
    for (let q = p.readPointer(), end = p.add(8).readPointer(); q.compare(end) < 0; q = q.add(size)) out.push(q);
    return out;
  };
  const typePath = e => {
    try { return wstring(e.add(0x38).readPointer().add(8)); } catch (x) { return null; }
  };
  const snapshot = (name, graph) => {
    const nodes = vector(graph.add(0x28), 8).map(p => p.readPointer());
    const edges = vector(graph.add(0x10), 8).map(p => p.readPointer());
    const ni = new Map(nodes.map((n, i) => [n.toString(), i])), ei = new Map(edges.map((e, i) => [e.toString(), i]));
    const idx = (m, p) => p.isNull() ? null : (m.has(p.toString()) ? m.get(p.toString()) : -1);
    graphStages.push([name, drawCount(),
      nodes.map(n => [n.add(0x4c).readS32(), n.add(0x50).readS32(), wstring(n.add(0x20)), n.add(0x48).readS32(),
                      [0, 1, 2, 3].map(k => idx(ei, n.add(0x58 + 8 * k).readPointer())), n.add(0x88).readU8()]),
      edges.map(e => [idx(ni, e.add(0x20).readPointer()), idx(ni, e.add(0x28).readPointer()), typePath(e),
                      vector(e.add(8), 8).map(p => [p.readS32(), p.add(4).readS32()]),
                      [e.add(0x48).readU8(), e.add(0x49).readU8(), e.add(0x4a).readU8(), e.add(0x6d).readU8()],
                      [0, 1, 2, 3, 4, 5, 6, 7].map(k => e.add(0x4c + 4 * k).readS32()),
                      [0, 1, 2, 3].map(k => e.add(0x70 + 4 * k).readS32()), [e.add(0x80).readS32(), e.add(0x84).readS32()]])]);
  };
  const viewVertices = view => {
    const map = view.readPointer();
    const ox = view.add(8).readU32(), oy = view.add(0xc).readU32(), w = view.add(0x18).readU32(), h = view.add(0x1c).readU32();
    const stride = map.add(0x40).readU64().toNumber(), verts = map.add(0x50).readPointer(), list = map.add(0x68).readPointer();
    const grounds = {}, v = [];
    for (let y = 0; y <= h; y++)
      for (let x = 0; x <= w; x++) {
        const p = verts.add(((oy + y) * stride + ox + x) * 3);
        const g = p.readU8();
        if (!(g in grounds)) {
          const handle = list.add(8 * g).readPointer();
          grounds[g] = handle.isNull() ? null : wstring(handle.add(8));
        }
        v.push([g, p.add(1).readS8(), p.add(2).readU8()]);
      }
    return { ox, oy, w, h, grounds, v };
  };
  // Dungeon graphs (.dgr): every room 0x1420fe780 places, as it returns, and the layout as 0x1421038d0 returns.
  // Layout (its third argument): +0x28 / +0x2c size in room units, +0x40 cells (i32: 0 free, 1 reserved, k ≥ 2 =
  // room k − 2), +0x58 rooms (0x60 each): +0 room (+8 path), +8 / +0xc x, y, +0x10, +0x11 exit count, +0x12 height,
  // +0x13 orientation, +0x14 kind, +0x15 flags, +0x20 / +0x24 w, h, +0x28 exits (0x10 each: edge type (+8 path), 4 bytes).
  const dungeonRoom = (layout, i) => {
    const r = layout.add(0x58).readPointer().add(0x60 * i);
    const h = r.readPointer();
    const exits = vector(r.add(0x28), 0x10).map(x => {
      const t = x.readPointer();
      return [t.isNull() ? null : wstring(t.add(8)), x.add(8).readU8(), x.add(9).readU8(), x.add(10).readU8(), x.add(11).readU8()];
    });
    return [h.isNull() ? null : wstring(h.add(8)), r.add(8).readS32(), r.add(0xc).readS32(), r.add(0x20).readS32(), r.add(0x24).readS32(),
            r.add(0x13).readU8(), r.add(0x12).readU8(), r.add(0x14).readU8(), r.add(0x15).readU8(), r.add(0x10).readU8(), exits];
  };
  const dungeonAt = at('0x1421038d0'), roomAt = at('0x1420fe780');
  const bytesAt = p => Array.from(new Uint8Array(p.readByteArray(16))).map(b => b.toString(16).padStart(2, '0')).join('');
  const cellsOf = l => {
    const w = l.add(0x28).readU32(), h = l.add(0x2c).readU32(), c = [];
    for (let i = 0; i < w * h; i++) c.push(l.add(0x40).readPointer().add(4 * i).readS32());
    return c;
  };
  // The cells as each layout stage starts ('<') and returns ('>').
  const dungeonStageHooks = [
    ['fill', '0x1420fd4e0', '488bc448895010488948085553565741'], ['connect', '0x1420facb0', '405355565741544155415641574881ec'],
    ['prune', '0x1420fd9c0', '48894c24085553565741544155415641'], ['fc690', '0x1420fc690', '48895c24184889542410555657415441'],
    ['fca30', '0x1420fca30', '48895c2410488974241848897c242055'], ['100d60', '0x142100d60', '4c89442418488954241048894c240855'],
    ['1012c0', '0x1421012c0', '4c894424184889542410555356574154'], ['100910', '0x142100910', '4c8bdc5553564155498d6ba14881ec88'],
    ['108dc0', '0x142108dc0', '40534883ec60488bd9488b4928e88e06'], ['pick', '0x1421083e0', '48895c2410488974241848897c242055'],
  ];
  for (const [name, va0, expected] of dungeonStageHooks) {
    if (bytesAt(at(va0)) !== expected) { send({ type: 'refused', reason: `dungeon stage ${name} (${va0}) does not match 3.29.3b` }); continue; }
    Interceptor.attach(at(va0), {
      onEnter(a) {
        this.on = this.threadId === currentThread() && dungeonMap !== null;
        if (!this.on) return;
        try { dungeonStages.push([name + '<', drawCount(), cellsOf(dungeonMap), name === 'pick' ? a[1].toInt32() : null]); } catch (e) { this.on = false; }
      },
      onLeave() {
        if (!this.on) return;
        try { dungeonStages.push([name + '>', drawCount(), cellsOf(dungeonMap)]); } catch (e) { }
      },
    });
  }
  // 0x1420fd4e0 after its lookup (rdi): the edge type's room sizes [w, h, weight] and their total (+0x18).
  if (bytesAt(at('0x1420fd58e')) === '48897d7f488b47084839070f84fb0300')
    Interceptor.attach(at('0x1420fd58e'), function () {
      if (this.threadId !== currentThread()) return;
      try {
        const l = this.context.rdi, sizes = [];
        for (let p = l.readPointer(), end = l.add(8).readPointer(); p.compare(end) < 0; p = p.add(12))
          sizes.push([p.readU32(), p.add(4).readU32(), p.add(8).readS32()]);
        dungeonSizes.push([drawCount(), sizes, l.add(0x18).readS32()]);
      } catch (e) { send({ type: 'error', message: 'dungeon sizes: ' + String(e) }); }
    });
  // 0x1420452e0(generator, index): one subgraph record (0x98 bytes, list at generator +0x30 → +0x10 / +0x18): graph
  // path (std::wstring +0x20), rect +0x40 x, +0x44 y, +0x48 w, +0x4c h, +0x90 flags (bit 0: .dgr), +0x92 parent.
  const subgraphAt = at('0x1420452e0');
  if (bytesAt(subgraphAt) !== '48895c24184889742420555741544156') send({ type: 'refused', reason: 'subgraph: 0x1420452e0 does not match 3.29.3b' });
  else Interceptor.attach(subgraphAt, {
    onEnter(a) {
      if (this.threadId !== currentThread()) return;
      try {
        const list = a[0].add(0x30).readPointer(), begin = list.add(0x10).readPointer(), index = a[1].toUInt32();
        const count = list.add(0x18).readPointer().sub(begin).toInt32() / 0x98, r = begin.add(index * 0x98);
        const ws = s => s.add(0x18).readU64().compare(7) > 0 ? s.readPointer().readUtf16String() : s.readUtf16String();
        subgraphs.push({ index, draws: drawCount(), path: ws(r.add(0x20)), x: r.add(0x40).readS32(), y: r.add(0x44).readS32(),
                         w: r.add(0x48).readS32(), h: r.add(0x4c).readS32(), flags: r.add(0x90).readU8(), parent: r.add(0x92).readU8(), count });
      } catch (e) { send({ type: 'error', message: 'subgraph: ' + String(e) }); }
    },
  });
  if (bytesAt(dungeonAt) !== '4c8bdc4d89431849894b085356574154') send({ type: 'refused', reason: 'dungeon: 0x1421038d0 does not match 3.29.3b' });
  else {
    Interceptor.attach(dungeonAt, {
      onEnter(a) {
        this.on = this.threadId === currentThread();
        if (!this.on) return;
        try {
          snapshot('dungeon', a[1]);
          this.layout = a[2];
          dungeonMap = a[2];
        } catch (e) {
          this.on = false;
          send({ type: 'error', message: 'dungeon: ' + String(e) });
        }
      },
      onLeave() {
        if (!this.on) return;
        try {
          const l = this.layout, w = l.add(0x28).readU32(), h = l.add(0x2c).readU32(), cells = [];
          for (let i = 0; i < w * h; i++) cells.push(l.add(0x40).readPointer().add(4 * i).readS32());
          const n = (l.add(0x60).readPointer().sub(l.add(0x58).readPointer())).toInt32() / 0x60;
          dungeon = { draws: drawCount(), w, h, cells, rooms: Array.from({ length: n }, (_, i) => dungeonRoom(l, i)), placed: dungeonPlaced,
                      stages: dungeonStages, sizes: dungeonSizes };
          dungeons.push(dungeon);
        } catch (e) {
          send({ type: 'error', message: 'dungeon: ' + String(e) });
        }
        dungeonPlaced = [];
        dungeonStages = [];
        dungeonSizes = [];
        dungeonMap = null;
      },
    });
    Interceptor.attach(roomAt, {
      onEnter(a) {
        this.on = this.threadId === currentThread();
        if (!this.on) return;
        this.layout = a[0];
        this.x = a[2].readS32();
        this.y = a[2].add(4).readS32();
      },
      onLeave() {
        if (!this.on) return;
        try {
          const l = this.layout, w = l.add(0x28).readU32();
          const k = l.add(0x40).readPointer().add(4 * (this.y * w + this.x)).readS32();
          dungeonPlaced.push([drawCount(), k - 2].concat(k >= 2 ? dungeonRoom(l, k - 2) : []));
        } catch (e) {
          send({ type: 'error', message: 'dungeon room: ' + String(e) });
        }
      },
    });
  }
  if (bytesAt(at('0x1420eec10')) !== '48895c24204c89442418555657415441') send({ type: 'refused', reason: 'fills: 0x1420eec10 does not match 3.29.3b' });
  else Interceptor.attach(at('0x1420eec10'), {
    onEnter(a) {
      if (this.threadId !== currentThread()) return;
      try {
        dungeonFills.push(['r', drawCount(), a[1].readS32(), a[1].add(4).readS32(), a[2].add(0x20).readU8(), a[2].add(0x21).readU8(),
                           a[3].toInt32(), a[4].toInt32() & 0xff]);
      } catch (e) { send({ type: 'error', message: 'fill record: ' + String(e) }); }
    },
  });
  if (bytesAt(at('0x1420f3de0')) !== '48895c2418574883ec304c8bd24c8bd9') send({ type: 'refused', reason: 'fills: 0x1420f3de0 does not match 3.29.3b' });
  else Interceptor.attach(at('0x1420f3de0'), {
    onEnter(a) {
      this.on = this.threadId === currentThread();
      if (!this.on) return;
      try {
        // param_1[1] + 8: the view (+0 map, +8 / +0xc offset); map +0x18 stride, +0x28 entries (0x38 bytes).
        const view = a[0].add(8).readPointer().add(8).readPointer(), map = view.readPointer();
        const x = a[1].readS32(), y = a[1].add(4).readS32();
        const at = map.add(0x28).readPointer().add(((view.add(0xc).readS32() + y) * map.add(0x18).readU64().toNumber() +
                                                     view.add(8).readS32() + x) * 0x38);
        this.entry = () => at;
        this.x = x;
        this.y = y;
        this.rec = ['f', drawCount(), this.x, this.y, a[1].add(0x14).readS32(), a[1].add(0x10).readU8()];
        this.before = this.entry().readPointer().isNull();
      } catch (e) { this.on = false; send({ type: 'error', message: 'fill: ' + String(e) }); }
    },
    onLeave() {
      if (!this.on) return;
      try {
        const e = this.entry();
        this.rec.push(this.before && !e.readPointer().isNull() ? e.add(0x30).readS16() : null);
        dungeonFills.push(this.rec);
      } catch (e) { send({ type: 'error', message: 'fill: ' + String(e) }); }
    },
  });
  const heightsOf = () => {
    const grid = currentGen.add(0x28).readPointer(), w = grid.add(0x18).readU64().toNumber(), h = grid.add(0x20).readU64().toNumber();
    const entries = grid.add(0x28).readPointer(), out = [];
    for (let i = 0; i < w * h; i++) {
      const e = entries.add(i * 0x38);
      out.push(e.readPointer().isNull() ? null : e.add(0x30).readS16());
    }
    return out;
  };
  const heightHooks = [
    ['tiler', '0x1420f3480', '40555356574154415541564157488dac'], ['outer', '0x142044f30', '48895c241048896c2418565741544156'],
    ['scale', '0x14204cbd0', '48895c241048896c2418488974242057'], ['mission', '0x14203e1f0', '48895c24105556574154415541564157'],
    ['postunique', '0x14203ece0', '48894c24085553565741544155415641'],
  ];
  for (const [name, va0, expected] of heightHooks) {
    if (bytesAt(at(va0)) !== expected) { send({ type: 'refused', reason: `heights ${name} (${va0}) does not match 3.29.3b` }); continue; }
    Interceptor.attach(at(va0), {
      onEnter() {
        this.on = this.threadId === currentThread() && currentGen !== null;
        if (!this.on) return;
        try {
          heightStages.push([name + '<', drawCount(), heightsOf()]);
          // As the outer ground starts: every vertex [height, flags] row by row over (w + 1) × (h + 1) (map +0x40
          // stride, +0x50 vertices of 3 bytes: ground index, height, flags).
          if (name === 'outer') {
            const grid = currentGen.add(0x28).readPointer(), w = grid.add(0x18).readU64().toNumber(), h = grid.add(0x20).readU64().toNumber();
            const stride = grid.add(0x40).readU64().toNumber(), vs = grid.add(0x50).readPointer(), v = [];
            for (let y = 0; y <= h; y++)
              for (let x = 0; x <= w; x++) {
                const p = vs.add((y * stride + x) * 3);
                v.push([p.add(1).readS8(), p.add(2).readU8()]);
              }
            heightStages.push(['outer-vertices', drawCount(), v]);
          }
        } catch (e) { this.on = false; send({ type: 'error', message: 'heights: ' + String(e) }); }
      },
      onLeave() {
        if (!this.on) return;
        try { heightStages.push([name + '>', drawCount(), heightsOf()]); } catch (e) { send({ type: 'error', message: 'heights: ' + String(e) }); }
      },
    });
  }
  const paintAt = at('0x142110d60');
  const paintBytes = Array.from(new Uint8Array(paintAt.readByteArray(16))).map(b => b.toString(16).padStart(2, '0')).join('');
  if (paintBytes !== '48895c2410488974241848897c242055') send({ type: 'refused', reason: 'paint: 0x142110d60 does not match 3.29.3b' });
  else Interceptor.attach(paintAt, {
    onEnter(a) {
      this.on = this.threadId === currentThread() && paint === null;
      if (!this.on) return;
      try {
        const sub = a[0].add(0x20).readPointer();
        const g = off => { const hd = sub.add(off).readPointer(); return hd.isNull() ? null : wstring(hd.add(8)); };
        this.info = { high: a[1].toInt32(), low: a[2].toInt32(), a0: g(0xa0), a8: g(0xa8), b0: g(0xb0) };
        this.view = a[0].add(0x28).readPointer();
      } catch (e) {
        this.on = false;
        send({ type: 'error', message: 'paint: ' + String(e) });
      }
    },
    onLeave() {
      if (!this.on) return;
      try { paint = Object.assign(this.info, { after: viewVertices(this.view) }); } catch (e) { send({ type: 'error', message: 'paint: ' + String(e) }); }
    },
  });
  const fillAt = at('0x141fef070');
  const fillBytes = Array.from(new Uint8Array(fillAt.readByteArray(16))).map(b => b.toString(16).padStart(2, '0')).join('');
  if (fillBytes !== '48895c2418565741564883ec30488bf1') send({ type: 'refused', reason: 'fill vertices: 0x141fef070 does not match 3.29.3b' });
  else Interceptor.attach(fillAt, {
    onEnter(a) {
      if (this.threadId !== currentThread() || fillVertices !== null) return;
      try {
        const view = a[0].add(8).readPointer(), map = view.readPointer();
        const ox = view.add(8).readU32(), oy = view.add(0xc).readU32(), w = view.add(0x18).readU32(), h = view.add(0x1c).readU32();
        const stride = map.add(0x40).readU64().toNumber(), verts = map.add(0x50).readPointer(), list = map.add(0x68).readPointer();
        const grounds = {}, v = [];
        for (let y = 0; y <= h; y++)
          for (let x = 0; x <= w; x++) {
            const p = verts.add(((oy + y) * stride + ox + x) * 3);
            const g = p.readU8();
            if (!(g in grounds)) {
              const handle = list.add(8 * g).readPointer();
              grounds[g] = handle.isNull() ? null : wstring(handle.add(8));
            }
            v.push([g, p.add(1).readS8(), p.add(2).readU8()]);
          }
        fillVertices = { ox, oy, w, h, grounds, v };
      } catch (e) {
        send({ type: 'error', message: 'fill vertices: ' + String(e) });
      }
    },
  });
  for (const [name, va0, expected, graphOf, onEntry] of stages) {
    const bytes = Array.from(new Uint8Array(at(va0).readByteArray(16))).map(b => b.toString(16).padStart(2, '0')).join('');
    if (bytes !== expected) {
      send({ type: 'refused', reason: `graph snapshot: ${name} (${va0}) does not match 3.29.3b` });
      continue;
    }
    Interceptor.attach(at(va0), {
      onEnter(a) {
        this.on = this.threadId === currentThread();
        if (!this.on) return;
        try {
          this.graph = graphOf(a);
          if (name === 'route') snapshot('beforeRoute', this.graph);
          if (onEntry) {
            snapshot(name, this.graph);
            this.on = false;
            if (name === 'relax') {
              const recs = [];
              for (let r = a[1].readPointer(), end = a[1].add(8).readPointer(); r.compare(end) < 0; r = r.add(0x98)) {
                const n = r.add(0x78).readPointer();
                recs.push([n.add(0x4c).readS32(), n.add(0x50).readS32(), r.add(0x70).readS32(), r.add(0x74).readS32(),
                           r.add(0x40).readU8(), r.add(0x41).readU8(), [0, 1, 2, 3].map(k => r.add(0x80 + 4 * k).readU32()),
                           r.add(0x91).readU8()]);
              }
              relaxRecords.push(recs);
            }
          }
        } catch (e) {
          this.on = false;
          send({ type: 'error', message: `graph snapshot ${name}: ` + String(e) });
        }
      },
      onLeave() {
        if (!this.on) return;
        try { snapshot(name, this.graph); } catch (e) { send({ type: 'error', message: `graph snapshot ${name}: ` + String(e) }); }
      },
    });
  }
}

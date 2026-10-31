// Cheap ("nullified") client for agent testing: loaded by scripts/dev/nullifier.py into Client/Game.patched.exe
// while the process is still suspended at spawn, before the engine starts. See Agents/docs/dev/headless-client.md.
// nullifier.py prepends `const OPTIONS = {...};`. No input simulation and no executable patching on disk; the hooks
// live only in this process and only for its life. The settings file is not this script's business: the client runs
// in a folder whose config.json names its own settings file.
// Always: the window is never activated (no focus stealing), and the two-copies mutexes get session-local names
// (runMutexPrefix). Everything else only when asked for:
//   window        minimized | hidden | normal: how the window shows.
//   nullRenderer  the engine's own "Render: OFF" switch: it then creates its Null device (no D3D/Vulkan device, no
//                 swap chain, no GPU memory, no texture uploads) instead of the configured DirectX/Vulkan one, and
//                 the Null index buffers report their index format (below).
//   noRenderJobs  the engine's "Test Disable Frame Render Jobs" switch (frame render jobs are not kicked).
//   noTextureUpload, noTextureStreaming  the texture manager's Upload / Streaming switches.
//   frameSleepMs  sleep this long once per main-loop frame (in the state-stack update), a frame-rate cap that also
//                 works with the Null device, which has no vsync or present to wait on.
//   crashLog      append a crash's registers and backtrace to this file (diagnostics).
//   traceFiles    log every CreateFileW of a path matching this regular expression (diagnostics).
'use strict';
const module = Process.getModuleByName('Game.patched.exe');
const at = va => module.base.add(ptr(va).sub(ptr('0x140000000')));
function checked(name, va, expected) {
  const p = at(va);
  const actual = Array.from(new Uint8Array(p.readByteArray(expected.length / 2)))
    .map(x => x.toString(16).padStart(2, '0')).join('');
  if (actual !== expected) throw new Error(`Unsupported client: ${name} code differs from 3.29.3b`);
  return p;
}
const log = message => send({type: 'log', message});
const keep = [];

// At spawn only ntdll and the executable are mapped: hook a DLL once the loader brings it in.
function whenLoaded(name, install) {
  const loaded = Process.findModuleByName(name);
  if (loaded) { install(loaded); return; }
  let done = false;
  const observer = Process.attachModuleObserver({onAdded(m) {
    if (done || m.name.toLowerCase() !== name) return;
    done = true; install(m);
  }});
  keep.push(observer);
}

// The client allows two copies per PC: at startup (0x140101d85) it creates Global\PoERunMutexA, takes it with a zero
// wait, else Global\PoERunMutexB, else throws "You may only run up to two copies of Path of Exile at a time." The
// handles are never used again. Our clients get session-local names instead (OPTIONS.runMutexPrefix + "PoERunMutexA"
// / "B", e.g. Local\LocalExile-pr-x-bot1-PoERunMutexA), so they never take one of the two Global slots the user's and
// other sessions' clients need. Only these two exact names are renamed: PathOfExileSingleInstance (file-association
// hand-off), Global\PoERunMutexCrashUpload and LMDB's Global\MDB… locks (the shared shader cache) stay as they are.
// The client imports CreateMutexW (this check) and CreateMutexA / OpenMutexA (LMDB, 0x1411c0a40); all six name-taking
// variants are watched, so the names are renamed whichever path asks for them. nullifier.py waits for the "run mutex
// ->" line and fails the launch without it.
if (OPTIONS.runMutexPrefix) {
  const renamed = {};
  for (const slot of ['A', 'B']) {
    const name = OPTIONS.runMutexPrefix + 'PoERunMutex' + slot;
    const key = 'global\\poerunmutex' + slot.toLowerCase();
    renamed[key] = {name, wide: Memory.allocUtf16String(name), ansi: Memory.allocAnsiString(name)};
    keep.push(renamed[key].wide, renamed[key].ansi);
  }
  // [export, index of the name argument, wide]
  const apis = [['CreateMutexW', 2, true], ['CreateMutexExW', 1, true], ['OpenMutexW', 2, true],
    ['CreateMutexA', 2, false], ['CreateMutexExA', 1, false], ['OpenMutexA', 2, false]];
  whenLoaded('kernelbase.dll', kernelbase => {
    for (const [api, index, wide] of apis) {
      const target = kernelbase.findExportByName(api);
      if (target === null) continue;
      Interceptor.attach(target, {onEnter(args) {
        const p = args[index];
        if (p.isNull()) return;
        const name = (wide ? p.readUtf16String() : p.readAnsiString()) || '';
        const to = renamed[name.toLowerCase()];
        if (to) { args[index] = wide ? to.wide : to.ansi; log(`run mutex -> ${to.name}`); }
      }});
    }
  });
}

// The window is created and shown as usual (the engine needs it), but never activated: it cannot take the keyboard
// focus from the user's window. Its top-level windows get WS_EX_NOACTIVATE (live 2026-10-31, without it a new client's
// window took the focus at creation even with the hooks below; it also keeps them off the taskbar), ShowWindow never
// activates, and SetForegroundWindow is neutralised.
whenLoaded('user32.dll', user32 => {
  const show = {minimized: 7 /* SW_SHOWMINNOACTIVE */, hidden: 0 /* SW_HIDE */, normal: 4 /* SW_SHOWNOACTIVATE */};
  const mode = show[OPTIONS.window] ?? 4;
  Interceptor.attach(user32.getExportByName('CreateWindowExW'), {onEnter(args) {
    if (args[8].isNull()) args[0] = ptr(args[0].toUInt32() | 0x08000000);  // a top-level window: WS_EX_NOACTIVATE
  }});
  Interceptor.attach(user32.getExportByName('ShowWindow'), {onEnter(args) {
    const cmd = args[1].toInt32();
    if (cmd !== 0 && cmd !== 6 /* SW_MINIMIZE */) args[1] = ptr(mode);
  }});
  // SetForegroundWindow(NULL) fails, which the client tolerates (it does so whenever Windows refuses).
  Interceptor.attach(user32.getExportByName('SetForegroundWindow'), {onEnter(args) { args[0] = ptr(0); }});
});

// A minimized client runs no frames: its window procedure (0x140e67380 → 0x140e68220) logs "[WINDOW] Minimized" on
// WM_SIZE / SIZE_MINIMIZED and the main loop then waits (live 2026-10-31: a Null client someone minimized stopped
// answering the bridge, and a DirectX 11 client started minimized never reached the login screen). An agent client
// must keep running whoever minimizes its window, so that message becomes WM_NULL (0) before the engine sees it; the
// window itself still minimizes. The engine keeps its last size and runs at its background frame rate.
{
  const windowProc = checked('windowProc', '0x140e67380', '4883ec38488bc1488b0d926376034885');
  let reported = false;
  Interceptor.attach(windowProc, {onEnter(args) {
    if (args[1].toInt32() === 5 /* WM_SIZE */ && args[2].toInt32() === 1 /* SIZE_MINIMIZED */) {
      args[1] = ptr(0);
      if (!reported) { reported = true; log('window minimized: the engine is not told'); }
    }
  }});
}

// Engine::Init(engine, options) (0x1410048f0) copies the options' switches into the engine, renderer and texture
// manager and logs each ("[RENDER] Render: ON", "[TEXTURE] Upload: ON", ...). Options offsets, from that function:
// +0x2d5 Render (passed to the renderer's init 0x140faf1e0 as its 4th argument; OFF makes the device start
// 0x140fadb20 select device type 6, the Null device, whatever device_type the settings name), +0x2bf texture
// Upload, +0x2bc texture Streaming (also the renderer's Async), +0x306 Test Disable Frame Render Jobs.
if (OPTIONS.nullRenderer || OPTIONS.noRenderJobs || OPTIONS.noTextureUpload || OPTIONS.noTextureStreaming) {
  const engineInit = checked('engineInit', '0x1410048f0', '488954241048894c2408555356574154');
  Interceptor.attach(engineInit, {onEnter(args) {
    const options = args[1], set = [];
    const write = (offset, value, name) => { options.add(offset).writeU8(value); set.push(`${name}=${value}`); };
    if (OPTIONS.nullRenderer) write(0x2d5, 0, 'render');
    if (OPTIONS.noRenderJobs) write(0x306, 1, 'disableFrameRenderJobs');
    if (OPTIONS.noTextureUpload) write(0x2bf, 0, 'textureUpload');
    if (OPTIONS.noTextureStreaming) write(0x2bc, 0, 'textureStreaming');
    log(`engine options: ${set.join(', ')}`);
  }});
}

// The Null device's index buffer (vtable 0x14320e140, built in 0x140e939b0) keeps a zero-filled byte vector of the
// buffer's size at +0x130 (Map, slot 2, returns its start) and the index kind at +0x148 (1: 16-bit, 2: 32-bit, as the
// DirectX 12 buffer keeps it at +0x318), but its slot 4, "describe" (out: u32 format 0x65 = 16-bit / 0x66 = 32-bit,
// u32), is the shared no-op `ret`. Callers then read an uninitialised format: the mesh ray test 0x142be7270 took a
// 16-bit buffer for a 32-bit one, read past its end and computed vertex pointers gigabytes away (the crash at
// 0x142be6be4 on entering any area). Slot 4 of that vtable gets the DirectX 12 buffer's answer, from the Null
// buffer's own fields; the second u32 (the DirectX 12 buffer's +0x228) is given the index count.
if (OPTIONS.nullRenderer) {
  const vtable = at('0x14320e140');
  const map = checked('nullBufferMap', '0x140ee81b0', '488b8130010000498901c3');
  if (!vtable.add(0x10).readPointer().equals(map) || !vtable.add(0x20).readPointer().equals(at('0x1400f0f00'))) {
    throw new Error('Unsupported client: Null index buffer vtable differs from 3.29.3b');
  }
  const describe = Memory.alloc(Process.pageSize);
  keep.push(describe);
  {
    const w = new X86Writer(describe);
    // eax = kind; r8 = byte size (end - begin); format 0x65 with size/2 indices, 0x66 with size/4.
    w.putMovRegRegOffsetPtr('eax', 'rcx', 0x148);
    w.putMovRegRegOffsetPtr('r8', 'rcx', 0x138);
    w.putMovRegRegOffsetPtr('r9', 'rcx', 0x130);
    w.putSubRegReg('r8', 'r9');
    w.putCmpRegI32('eax', 1);
    w.putJccShortLabel('jne', 'wide', 'no-hint');
    w.putMovRegU32('eax', 0x65);
    w.putShrRegU8('r8', 1);
    w.putJmpShortLabel('store');
    w.putLabel('wide');
    w.putCmpRegI32('eax', 2);
    w.putJccShortLabel('jne', 'done', 'no-hint');
    w.putMovRegU32('eax', 0x66);
    w.putShrRegU8('r8', 2);
    w.putLabel('store');
    w.putMovRegOffsetPtrReg('rdx', 0, 'eax');
    w.putMovRegOffsetPtrReg('rdx', 4, 'r8d');
    w.putLabel('done');
    w.putRet();
    w.flush();
  }
  Memory.protect(describe, Process.pageSize, 'r-x');
  const protection = Process.findRangeByAddress(vtable.add(0x20)).protection;
  Memory.protect(vtable.add(0x20), 8, 'rw-');
  vtable.add(0x20).writePointer(describe);
  Memory.protect(vtable.add(0x20), 8, protection);
  log('Null index buffers describe their format');
}

// The state-stack update (0x141526c40), on the main thread once per frame: with frameSleepMs, a per-frame sleep. Its
// entry, not client-control.js's point 0x7f bytes in (0x141526cbf), so the bridge's code check there still sees the
// original. (The frame rate itself: client_control.py game:frames.)
if (OPTIONS.frameSleepMs > 0) {
  const frame = checked('stateStackUpdate', '0x141526c40', '48895c241048896c2418488974242057');
  let sleep = null;
  Interceptor.attach(frame, function () {
    sleep ??= new NativeFunction(Process.getModuleByName('kernel32.dll').getExportByName('Sleep'), 'void', ['uint32']);
    sleep(OPTIONS.frameSleepMs);
  });
}

if (OPTIONS.traceFiles) {
  const trace = new RegExp(OPTIONS.traceFiles, 'i');
  whenLoaded('kernelbase.dll', kernelbase => {
    Interceptor.attach(kernelbase.getExportByName('CreateFileW'), {onEnter(args) {
      if (!args[0].isNull()) { const path = args[0].readUtf16String(); if (trace.test(path)) log(`open ${path}`); }
    }});
  });
}

// A crash is written to OPTIONS.crashLog synchronously, with registers and image-relative return addresses, before
// the process dies as it would anyway.
if (OPTIONS.crashLog) {
  Process.setExceptionHandler(details => {
    // 'system' covers the C++ exceptions (0xe06d7363) the client throws and catches itself.
    if (details.type === 'system') return false;
    try {
      const rva = p => { const m = Process.findModuleByAddress(p); return m ? `${m.name}+0x${p.sub(m.base).toString(16)}` : `${p}`; };
      const c = details.context;
      const regs = ['rax', 'rbx', 'rcx', 'rdx', 'rsi', 'rdi', 'rbp', 'rsp', 'r8', 'r9', 'r12', 'r13', 'r14', 'r15']
        .map(r => `${r}=${c[r]}`).join(' ');
      const stack = Thread.backtrace(c, Backtracer.FUZZY).slice(0, 24).map(rva).join('\n  ');
      const file = new File(OPTIONS.crashLog, 'a');
      file.write(`${new Date().toISOString()} pid ${Process.id} thread ${Process.getCurrentThreadId()} ${details.type}` +
        ` at ${rva(details.address)} (${details.memory ? details.memory.operation + ' ' + details.memory.address : ''})` +
        `\n  ${regs}\n  ${stack}\n`);
      file.close();
    } catch (_) {}
    return false;
  });
}

rpc.exports.ready = () => ({pid: Process.id, options: OPTIONS});

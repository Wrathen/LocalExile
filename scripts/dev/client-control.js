// Local development RPC for the copied standalone 3.29.3b client. Loaded by client_control.py.
// Commands run inside the existing state-stack update, after its transition guard is entered.
// No input simulation, screen coordinates, credential persistence, or executable patching.
'use strict';
const module = Process.getModuleByName('Game.patched.exe');
const at = va => module.base.add(ptr(va).sub(ptr('0x140000000')));
const specs = {
  frame: ['0x141526cbf', '488b5d284883eb10483b5d200f848800'],
  connect: ['0x14213d9f0', '48895c2408488974241055574156488d'],
  login: ['0x14213ea10', '48895c24084889742410555741544156'],
  play: ['0x1421406f0', '48895c240848896c2410488974241857'],
  write: ['0x140dfb760', '48895c240848896c2410488974241857'],
  flush: ['0x140dfb8c0', '48895c24104889742418555741544156'],
  logout: ['0x140d0f740', '48895c24104889742418555741554156'],
  push: ['0x1415261c0', '48895c240848896c2410488974241857'],
  dispatch: ['0x14213f190', '48895c24084c89442418555657415441'],
  loginWriter: ['0x14213c660', '48895c241048896c2418488974242057'],
  assignText: ['0x1401346d0', '48895c241048896c2418565741574883'],
  lifeTick: ['0x141e6a190', '40534883ec3083b9a801000000488bd9'],
  poolUpdate: ['0x141e69fb0', '405341574883ec288b42204c8bfa448b'],
  // The client's own instance send: guards the socket offsets lockedWrite copies from it (+0x240, +0x242, +0x248).
  instanceSend: ['0x14216e940', '48895c241048896c2418488974242057'],
};
const addresses = {};
for (const [name, [va, expected]] of Object.entries(specs)) {
  const p = at(va);
  const actual = Array.from(new Uint8Array(p.readByteArray(expected.length / 2)))
    .map(x => x.toString(16).padStart(2, '0')).join('');
  if (actual !== expected) throw new Error(`Unsupported client: ${name} code differs from 3.29.3b`);
  addresses[name] = p;
}
const native = (name, args, returns = 'void') => new NativeFunction(addresses[name], returns, args,
  { abi: 'win64', scheduling: 'cooperative' });
const connect = native('connect', ['pointer', 'pointer', 'pointer']);
const login = native('login', ['pointer', 'pointer', 'pointer', 'uint8', 'uint8']);
const play = native('play', ['pointer', 'pointer', 'uint32']);
const write = native('write', ['pointer', 'pointer', 'uint64'], 'pointer');
const flush = native('flush', ['pointer', 'uint32', 'uint8']);
const logout = native('logout', ['pointer']);
const push = native('push', ['pointer', 'pointer']);
const assignText = native('assignText', ['pointer', 'pointer', 'uint64'], 'pointer');
const kernel32 = Process.getModuleByName('kernel32.dll');
const acquireLock = new NativeFunction(kernel32.getExportByName('AcquireSRWLockExclusive'), 'void', ['pointer']);
const releaseLock = new NativeFunction(kernel32.getExportByName('ReleaseSRWLockExclusive'), 'void', ['pointer']);
// Writes to a socket's send buffer as the client's own instance send (0x14216e940) does: it marks the socket as being
// written (+0x240) and, while another thread services the socket (+0x242), writes under the socket's SRW lock (+0x248),
// which the flush (0x140dfb8c0) takes too. The writer (0x140dfb760) itself does not lock: written without the lock, a
// message raced the network thread's flush and the client's cipher fell out of step with the server's for good (live
// 2026-10-30: right after the client's own 0x020a, every later message reached the Gateway as garbage).
function lockedWrite(socket, writeIt) {
  const threaded = socket.add(0x242).readU8() !== 0, lock = socket.add(0x248);
  socket.add(0x240).writeU8(1);
  if (threaded) acquireLock(lock);
  try { writeIt(); } finally { if (threaded) releaseLock(lock); socket.add(0x240).writeU8(0); }
}
const app = () => at('0x1445cbe68').readPointer();
const stack = () => at('0x1445cbe40').readPointer();
const loginObject = () => app().add(0x358).readPointer();
const instance = () => app().add(0x348).readPointer();
rpc.exports.identity = () => ({pid: Process.id, path: module.path, build: '3.29.3b'});
let lastLoginResult = null;
let credentials = null, loginSubmitted = false;
let life = null, resourceProbe = false;
const resourceUpdates = [];
const localLife = p => !p.isNull() && p.add(8).readPointer().add(0x88).readU32() === 0x1000;
function resources() {
  if (life === null || !localLife(life) || status().instanceState !== 3) return null;
  const pools = Object.fromEntries([['life',0x178],['mana',0x1c8],['es',0x210]].map(([name, offset]) => {
    const p=life.add(offset); return [name,{current:p.add(0x30).readS32(),fraction:p.add(0x28).readFloat()}];
  }));
  return {pools, updates:resourceUpdates.splice(0)};
}
function enableResources() {
  if (resourceProbe) return;
  resourceProbe=true;
  Interceptor.attach(addresses.lifeTick,{onEnter(args) {
    try {if(localLife(args[0])) life=args[0];} catch(_){life=null;}
  }});
  Interceptor.attach(addresses.poolUpdate,{onEnter(args) {
    try {
      const owner=args[0].add(8).readPointer(); if(!localLife(owner)) return;
      resourceUpdates.push({timeMs:Date.now(),pool:args[1].add(0x28).readU8(),current:args[1].add(0x20).readU32(),
        previousCurrent:args[0].add(0x30).readS32(),previousFraction:args[0].add(0x28).readFloat()});
      if(resourceUpdates.length>128) resourceUpdates.shift();
    } catch(_) {}
  }});
}
function text(s, wide = true) {
  const length = s.add(0x10).readU64().toNumber(), cap = s.add(0x18).readU64().toNumber();
  if (length > 4096) throw new Error('Invalid client string length');
  const data = cap > (wide ? 7 : 15) ? s.readPointer() : s;
  return length ? (wide ? data.readUtf16String(length) : data.readUtf8String(length)) : '';
}
function string(s, wide, keep) {
  const header = Memory.alloc(32), data = wide ? Memory.allocUtf16String(s) : Memory.allocUtf8String(s);
  const length = wide ? s.length : unescape(encodeURIComponent(s)).length;
  header.writePointer(data); header.add(0x10).writeU64(length);
  header.add(0x18).writeU64(Math.max(length, wide ? 8 : 16));
  keep.push(header, data); return header;
}
function characters() {
  const l = loginObject();
  if (l.isNull() || l.add(0xde8).readU32() !== 3) throw new Error('Not logged in');
  const begin = l.add(0xe68).readPointer(), end = l.add(0xe70).readPointer();
  const count = end.sub(begin).toInt32() / 0x50;
  if (!Number.isInteger(count) || count < 0 || count > 255) throw new Error('Invalid character vector');
  return Array.from({length: count}, (_, index) => {
    const p = begin.add(index * 0x50);
    return {index, name: text(p), league: text(p.add(0x20)), level: p.add(0x48).readU8(), classId: p.add(0x4a).readU8()};
  });
}
function status() {
  const a=app(),sm=stack();
  if(a.isNull() || sm.isNull()) return {pid:Process.id,build:'3.29.3b',screen:'starting',states:[],loginState:0,instanceState:0};
  const l=a.add(0x358).readPointer(),i=a.add(0x348).readPointer();
  const begin = sm.add(0x20).readPointer(), end = sm.add(0x28).readPointer();
  const count = end.sub(begin).toInt32() / 16;
  if (!Number.isInteger(count) || count < 0 || count > 32) throw new Error('Invalid state stack');
  const states = Array.from({length: count}, (_, n) => begin.add(n * 16).readPointer().add(0x209).readU8());
  const loginState = l.isNull() ? 0 : l.add(0xde8).readU32();
  return {pid: Process.id, build: '3.29.3b', states, screen: ({4:'game',6:'login',7:'login',9:'characters',11:'loading'})[states.at(-1)] || 'other',
    loginState, instanceState: i.isNull() ? 0 : i.add(0xc3a0).readU32(),
    account: loginState === 3 ? text(l.add(0xe10)) : null,
    character: i.isNull() || i.add(0xc3a0).readU32() !== 3 ? null : text(l.add(0xdf0)), lastLoginResult, loginSubmitted};
}
function state(id) {
  const sm = stack(), entry = sm.add(0x48 + id * 16);
  if (entry.readPointer().isNull()) throw new Error('Client state is not constructed yet');
  push(sm, entry);
}
function sendPacket(bytes, connection) {
  const p = Memory.alloc(bytes.length); p.writeByteArray(bytes);
  const socket = connection === 'login' ? loginObject().add(0xdd0).readPointer() : instance().add(0xc288).readPointer();
  if (socket.isNull()) throw new Error('Connection has no socket');
  lockedWrite(socket, () => write(socket, p, bytes.length)); flush(socket, 0, 0);
}
function execute(request) {
  const current = status(), keep = [];
  switch (request.op) {
    case 'status': return current;
    case 'resources':
      if(current.screen!=='game' || current.instanceState!==3) throw new Error('Resources require an active area');
      enableResources(); return resources();
    case 'characters': return {characters: characters()};
    case 'connect':
      if (current.screen !== 'login' || current.loginState !== 0) throw new Error('Connect requires a disconnected login screen');
      if (!Array.isArray(request.digest) || request.digest.length !== 32) throw new Error('Expected 32-byte digest');
      lastLoginResult = null;
      credentials = {account: request.account, digest: request.digest}; loginSubmitted = false;
      connect(loginObject(), string('127.0.0.1', true, keep), string(String(request.port), true, keep));
      return {submitted: true};
    case 'connectSaved':
      // The client's own login form (its remembered account and saved credential) goes out after the handshake.
      if (current.screen !== 'login' || current.loginState !== 0) throw new Error('Connect requires a disconnected login screen');
      lastLoginResult = null; credentials = null; loginSubmitted = false;
      connect(loginObject(), string('127.0.0.1', true, keep), string(String(request.port), true, keep));
      return {submitted: true};
    case 'login': {
      // Login's handshake listener may already have submitted the form in the same frame as the 0x02 reply.
      if (loginSubmitted) return {alreadySubmitted: true};
      if (current.screen !== 'login' || current.loginState !== 2) throw new Error('Login requires a completed local handshake');
      if (!credentials) throw new Error('No pending login');
      const digest = Memory.alloc(32); digest.writeByteArray(credentials.digest);
      login(loginObject(), string(credentials.account, false, keep), digest, 0, 0);
      digest.writeByteArray(new Uint8Array(32));
      return {submitted: true};
    }
    case 'play': {
      if (current.screen !== 'characters' || current.loginState !== 3) throw new Error('Play requires character selection');
      const chosen = characters().find(c => c.name === request.name);
      if (!chosen) throw new Error('Character not found in the client list');
      const data = Memory.allocUtf16String(chosen.name), view = Memory.alloc(16);
      view.writePointer(data); view.add(8).writeU64(chosen.name.length);
      // The Play callback sets the class on state 4, but pushes state 11 (the loading state), not state 4.
      // Loading installs the instance-assignment listener before it enters the in-game state.
      stack().add(0x88).readPointer().add(0xa48).writeU32(chosen.classId);
      play(loginObject(), view, chosen.index); state(11);
      return {submitted: true, character: chosen.name};
    }
    case 'select':
      if (current.screen === 'characters') return {alreadySelected: true};
      if (current.screen !== 'game' || current.instanceState !== 3) throw new Error('Not playing a character');
      sendPacket([0x01,0x53], 'instance'); return {submitted: true};
    case 'logout':
      if (current.screen === 'game') throw new Error('Return to character selection before logging out');
      logout(stack().add(0xb8).readPointer()); return {submitted: true};
    case 'chat':
      if (current.screen !== 'game' || current.instanceState !== 3) throw new Error('Chat requires an active area');
      if (!Array.isArray(request.bytes) || request.bytes.length > 8192) throw new Error('Invalid chat message');
      sendPacket(request.bytes, 'instance'); return {submitted: true};
    case 'interaction': {
      if (current.screen !== 'game' || current.instanceState !== 3) throw new Error('Interaction requires an active area');
      const b=request.bytes, id=Array.isArray(b) && b.length>=2 ? b[0]*256+b[1] : -1;
      const lengths={0x24:12,0x25:16,0x28:13,0x2a:18,0x4d:9,0x70:6,0x82:3,0x85:2,0x88:15};
      if (!lengths[id] || b.length!==lengths[id])
        throw new Error('Unsupported interaction message');
      sendPacket(b,'instance'); return {submitted:true};
    }
    default: throw new Error('Unsupported operation');
  }
}
let pending = null;
// Main-loop frames since this script attached (the frame point runs once per frame on the main thread), for
// game:frames: the client's frame rate, e.g. the cheap preset's 10-fps cap (Agents/docs/dev/headless-client.md).
const frameCount = {frames: 0, since: Date.now(), last: {time: Date.now(), frames: 0}};
Interceptor.attach(addresses.frame, function () {
  frameCount.frames++;
  if (!pending) return;
  const work = pending; pending = null; clearTimeout(work.timer);
  // Do not let a one-shot caller detach while this native hook is still unwinding.
  try { const result=execute(work.request); setImmediate(()=>work.resolve(result)); }
  catch (e) { setImmediate(()=>work.reject(e)); }
});
Interceptor.attach(addresses.dispatch, {onEnter(args) {
  try { const message = args[2].readPointer(); if (!message.isNull() && message.add(8).readU16() === 4) lastLoginResult = message.add(0x10).readU16(); } catch (_) {}
}});
Interceptor.attach(addresses.loginWriter, {onEnter(args) {
  if (!credentials) return;
  // Substitute the requested credentials in the one outgoing native form submission. The ordinary handshake
  // callback can race the RPC caller; replacing this message prevents an earlier saved/empty password winning.
  const value = credentials; credentials = null;
  const data = Memory.allocUtf8String(value.account);
  assignText(args[0].add(0x10), data, unescape(encodeURIComponent(value.account)).length);
  args[0].add(0x34).writeByteArray(value.digest);
  // AccountLogin +0x95 selects saved-token verification. This request supplied a password digest instead.
  args[0].add(0x95).writeU8(0);
  value.digest.fill(0); loginSubmitted = true;
}});
rpc.exports.frames = () => {
  const now = Date.now(), {frames, last} = frameCount;
  const fps = (frames - last.frames) * 1000 / Math.max(1, now - last.time);
  frameCount.last = {time: now, frames};
  return {frames, seconds: Math.round((now - frameCount.since) / 100) / 10, fpsSinceLastCall: Math.round(fps * 10) / 10};
};
rpc.exports.command = request => new Promise((resolve, reject) => {
  if (pending) { reject(new Error('Another command is pending')); return; }
  pending = {request, resolve, reject, timer: setTimeout(() => {
    pending = null; reject(new Error('No client frame within 5 seconds; command cancelled'));
  }, 5000)};
});

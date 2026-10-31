// Offline check of nullifier.js's always-on hooks, with Frida's API stubbed:
// - the two-copies mutexes: only Global\PoERunMutexA/B become session-local names, through the wide and the ANSI
//   variants; every other mutex name (single-instance hand-off, crash upload, LMDB) is left alone, and each rename is
//   reported ("run mutex -> ", which nullifier.py waits for);
// - top-level windows are created with WS_EX_NOACTIVATE;
// - the engine is never told its window was minimized.
//   node scripts/dev/test-nullifier-mutex.cjs
'use strict';
const fs = require('fs');
const path = require('path');
const assert = require('assert');

const text = (s, encoding = 'wide') => ({isNull: () => s === null, readUtf16String: () => s, readAnsiString: () => s,
  value: s, encoding});
const hooks = {};
const sent = [];
// The code nullifier.js checks before it hooks (the always-on hooks only), as 3.29.3b has it.
const code = {0x140e67380: '4883ec38488bc1488b0d926376034885'};
const number = v => (typeof v === 'object' && v !== null ? v.value : Number(v));
const pointer = v => ({value: number(v), toInt32: () => number(v) | 0, toUInt32: () => number(v) >>> 0,
  isNull: () => !number(v), add: x => pointer(number(v) + number(x)), sub: x => pointer(number(v) - number(x)),
  readByteArray: () => Uint8Array.from(Buffer.from(code[number(v)] || '', 'hex')).buffer});
const sandbox = {
  OPTIONS: {runMutexPrefix: 'Local\\LocalExile-pr-x-bot1-', window: 'normal'},
  ptr: pointer,
  send: message => sent.push(message.message),
  rpc: {exports: {}},
  Memory: {allocUtf16String: s => text(s, 'wide'), allocAnsiString: s => text(s, 'ansi')},
  Interceptor: {attach: (target, callbacks) => {
    hooks[typeof target === 'string' ? target : `0x${target.value.toString(16)}`] = callbacks;
  }},
  Process: {
    getModuleByName: () => ({base: pointer(0x140000000)}),
    findModuleByName: name => ({name, findExportByName: api => `${name}!${api}`, getExportByName: api => `${name}!${api}`}),
    attachModuleObserver: () => ({}),
  },
};
const source = fs.readFileSync(path.join(__dirname, 'nullifier.js'), 'utf8');
new Function(...Object.keys(sandbox), source)(...Object.values(sandbox));

function call(api, index, name, count = 3) {
  const args = Array.from({length: count}, () => text(null));
  args[index] = text(name);
  hooks[`kernelbase.dll!${api}`].onEnter(args);
  return args[index];
}
const A = 'Local\\LocalExile-pr-x-bot1-PoERunMutexA', B = 'Local\\LocalExile-pr-x-bot1-PoERunMutexB';
assert.strictEqual(call('CreateMutexW', 2, 'Global\\PoERunMutexA').value, A);
assert.strictEqual(call('CreateMutexW', 2, 'Global\\PoERunMutexB').value, B);
assert.strictEqual(call('CreateMutexExW', 1, 'global\\poerunmutexa', 4).value, A);
assert.strictEqual(call('OpenMutexW', 2, 'Global\\PoERunMutexB').value, B);
const ansi = call('CreateMutexA', 2, 'Global\\PoERunMutexA');
assert.strictEqual(ansi.value, A);
assert.strictEqual(ansi.encoding, 'ansi');  // an ANSI caller gets an ANSI string
assert.strictEqual(call('CreateMutexExA', 1, 'Global\\PoERunMutexB', 4).encoding, 'ansi');
assert.strictEqual(call('OpenMutexA', 2, 'Global\\PoERunMutexA').value, A);
assert.strictEqual(call('CreateMutexW', 2, 'Global\\PoERunMutexA').encoding, 'wide');
for (const other of ['PathOfExileSingleInstance', 'Global\\PoERunMutexCrashUpload', 'Global\\MDB12345', 'Global\\PoERunMutexAB']) {
  assert.strictEqual(call('CreateMutexW', 2, other).value, other);
  assert.strictEqual(call('OpenMutexA', 2, other).value, other);
}
assert.strictEqual(hooks['kernelbase.dll!CreateMutexW'].onEnter([text(null), text(null), text(null)]), undefined);
assert.ok(sent.includes(`run mutex -> ${A}`) && sent.includes(`run mutex -> ${B}`), 'each rename is reported');
assert.ok(!sent.some(m => /PoERunMutexCrashUpload|MDB|SingleInstance/.test(m)), 'other names are not reported');

// Top-level windows never activate (WS_EX_NOACTIVATE); child windows are left alone.
const topLevel = Array.from({length: 12}, () => pointer(0));
topLevel[0] = pointer(0x100);
hooks['user32.dll!CreateWindowExW'].onEnter(topLevel);
assert.strictEqual(topLevel[0].value, 0x08000100);
const child = Array.from({length: 12}, () => pointer(0));
child[8] = pointer(0x1234);
hooks['user32.dll!CreateWindowExW'].onEnter(child);
assert.strictEqual(child[0].value, 0);
// The engine is never told its window was minimized (WM_SIZE / SIZE_MINIMIZED becomes WM_NULL); other sizes pass.
const minimized = [pointer(0x10), pointer(5), pointer(1), pointer(0)];
hooks['0x140e67380'].onEnter(minimized);
assert.strictEqual(minimized[1].value, 0);
const restored = [pointer(0x10), pointer(5), pointer(0), pointer(0x02580320)];
hooks['0x140e67380'].onEnter(restored);
assert.strictEqual(restored[1].value, 5);
console.log('ok');

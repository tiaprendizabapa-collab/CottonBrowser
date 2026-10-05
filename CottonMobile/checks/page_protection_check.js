const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const script = fs.readFileSync(path.join(__dirname, '../app/src/main/assets/page_protection.js'), 'utf8');
const styles = [];
const listeners = {};
const document = {
  documentElement: null,
  getElementById: id => styles.find(style => style.id === id),
  createElement: () => ({}),
  addEventListener(name, fn) { listeners[name] = fn; }
};
const sandbox = { location: { protocol: 'https:' }, document, addEventListener() {} };
sandbox.window = sandbox;
const context = vm.createContext(sandbox);
vm.runInContext(script, context);
assert.equal(styles.length, 0, 'Document start can precede the root');
document.documentElement = { appendChild(style) { styles.push(style); } };
listeners.DOMContentLoaded();
assert.equal(styles.length, 1);
assert(styles[0].textContent.includes('ins.adsbygoogle'));
assert(styles[0].textContent.includes('[id^="div-gpt-ad-"]'));
assert(!styles[0].textContent.includes('video{'), 'Video content must not be hidden');
vm.runInContext(script, context);
assert.equal(styles.length, 1);
const offOrigin = { location: { protocol: 'file:' }, document };
offOrigin.window = offOrigin;
vm.runInNewContext(script, offOrigin);
assert.equal(offOrigin.__cottonPageProtectionV1, undefined);
console.log('Page protection checks passed.');

const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

const script = fs.readFileSync(path.join(__dirname, '../app/src/main/assets/youtube_protection.js'), 'utf8');
const videoUrl = 'https://rr1---sn.example.googlevideo.com/videoplayback?id=abc123';
const player = {
  videoDetails: { videoId: 'abc123' },
  streamingData: { formats: [{ url: videoUrl }] },
  playabilityStatus: { status: 'OK' },
  adPlacements: [{ ad: true }],
  adSlots: [{ ad: true }],
  playerAds: [{ ad: true }],
  adBreaks: [{ marker: 'keep' }],
  adBreakHeartbeatParams: 'ads'
};

function makeContext(hostname = 'www.youtube.com') {
  const listeners = {};
  const timers = [];
  const styles = [];
  const skip = {
    disabled: false,
    clicks: 0,
    getAttribute: () => null,
    getClientRects: () => [{ width: 10 }],
    click() { this.clicks++; }
  };
  class FakeResponse {
    constructor(url, body) { this.url = url; this.body = body; }
    json() { return Promise.resolve(JSON.parse(this.body)); }
    text() { return Promise.resolve(this.body); }
  }
  const document = {
    head: null,
    documentElement: { appendChild(style) { styles.push(style); } },
    getElementById: id => styles.find(style => style.id === id) || null,
    createElement: tag => ({ tagName: tag, id: '', textContent: '' }),
    querySelectorAll: () => [skip],
    addEventListener(name, listener) { listeners[name] = listener; }
  };
  const sandbox = {
    location: { hostname, href: `https://${hostname}/watch?v=abc123` },
    document,
    URL,
    Response: FakeResponse,
    MutationObserver: class { observe() {} },
    setTimeout(callback) { timers.push(callback); },
    getComputedStyle: () => ({ display: 'block', visibility: 'visible' }),
    addEventListener(name, listener) { listeners[name] = listener; }
  };
  sandbox.window = sandbox;
  const context = vm.createContext(sandbox);
  return { context, sandbox, listeners, timers, styles, skip };
}

async function main() {
  const env = makeContext();
  vm.runInContext(script, env.context);
  assert.equal(env.sandbox.__cottonYouTubeProtectionV1, true);
  assert.equal(env.styles.length, 1);

  env.sandbox.ytInitialPlayerResponse = structuredClone(player);
  const initial = env.sandbox.ytInitialPlayerResponse;
  assert.equal(initial.adPlacements, undefined);
  assert.equal(initial.playerAds, undefined);
  assert.equal(initial.adSlots, undefined);
  assert.equal(initial.streamingData.formats[0].url, videoUrl);
  assert.equal(initial.playabilityStatus.status, 'OK');

  env.sandbox.payload = JSON.stringify(player);
  const parsed = vm.runInContext('JSON.parse(payload)', env.context);
  assert.equal(parsed.adPlacements, undefined);
  assert.equal(parsed.adBreaks[0].marker, 'keep');
  assert.equal(parsed.adBreakHeartbeatParams, 'ads');
  assert.equal(parsed.streamingData.formats[0].url, videoUrl);

  env.sandbox.payload = JSON.stringify({ catalog: true, adPlacements: ['non-player'] });
  const unrelated = vm.runInContext('JSON.parse(payload)', env.context);
  assert.deepEqual(Array.from(unrelated.adPlacements), ['non-player']);

  env.sandbox.payload = JSON.stringify({ playerResponse: JSON.stringify(player) });
  const nested = vm.runInContext('JSON.parse(payload)', env.context);
  const nestedPlayer = JSON.parse(nested.playerResponse);
  assert.equal(nestedPlayer.adPlacements, undefined);
  assert.equal(nestedPlayer.streamingData.formats[0].url, videoUrl);

  const playerResponse = new env.sandbox.Response('https://www.youtube.com/youtubei/v1/player?key=x', JSON.stringify(player));
  const fromJson = await playerResponse.json();
  assert.equal(fromJson.adPlacements, undefined);
  assert.equal(fromJson.streamingData.formats[0].url, videoUrl);
  const fromText = JSON.parse(await playerResponse.text());
  assert.equal(fromText.playerAds, undefined);
  assert.equal(fromText.playabilityStatus.status, 'OK');

  const otherResponse = new env.sandbox.Response('https://www.youtube.com/youtubei/v1/browse', JSON.stringify({ adPlacements: ['keep'] }));
  assert.deepEqual((await otherResponse.json()).adPlacements, ['keep']);

  env.listeners['yt-navigate-finish']();
  assert.equal(env.timers.length, 1);
  env.timers.shift()();
  assert.equal(env.skip.clicks, 1);
  env.skip.disabled = true;
  env.listeners['yt-navigate-finish']();
  env.timers.shift()();
  assert.equal(env.skip.clicks, 1);

  const parseHook = vm.runInContext('JSON.parse', env.context);
  vm.runInContext(script, env.context);
  assert.equal(vm.runInContext('JSON.parse', env.context), parseHook);
  assert.equal(env.styles.length, 1);

  const offOrigin = makeContext('example.org');
  vm.runInContext(script, offOrigin.context);
  assert.equal(offOrigin.sandbox.__cottonYouTubeProtectionV1, undefined);

  const disabledSite = makeContext('m.youtube.com');
  vm.runInContext(`(function(){if(['m.youtube.com'].indexOf(location.hostname.toLowerCase())!==-1)return;${script}})();`, disabledSite.context);
  assert.equal(disabledSite.sandbox.__cottonYouTubeProtectionV1, undefined);

  const mobile = makeContext('m.youtube.com');
  vm.runInContext(script, mobile.context);
  assert.equal(mobile.sandbox.__cottonYouTubeProtectionV1, true);
  console.log('YouTube protection checks passed.');
}

main().catch(error => {
  console.error(error);
  process.exitCode = 1;
});

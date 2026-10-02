// Reproducible offline extraction from the EasyList snapshot shipped with desktop uBOL.
// Only unconditional domain blocks are imported. All allow rules are retained, with
// resource/party restrictions relaxed because WebView does not expose resource types.
const fs = require('node:fs');
const path = require('node:path');
const root = path.resolve(__dirname, '../..');
const source = path.join(root, 'LeanBrowser/Extensions/uBlockOriginLite/2026.914.1325');
const assets = path.join(root, 'CottonMobile/app/src/main/assets');
const rules = JSON.parse(fs.readFileSync(path.join(source, 'easylist.json'), 'utf8'));
const domains = new Set();
const exceptions = [];
for (const rule of rules) {
  const c = rule.condition;
  if (rule.action.type === 'block' && Object.keys(c).length === 1 && c.requestDomains) {
    c.requestDomains.forEach(domain => domains.add(domain));
  }
  if (rule.action.type === 'allow') {
    const supported = new Set(['urlFilter', 'requestDomains', 'initiatorDomains',
      'excludedInitiatorDomains', 'excludedRequestDomains', 'resourceTypes', 'domainType']);
    if (Object.keys(c).some(key => !supported.has(key))) throw new Error('Unsupported allow rule ' + rule.id);
    exceptions.push([c.urlFilter || '', (c.requestDomains || []).join(','),
      (c.initiatorDomains || []).join(','), (c.excludedInitiatorDomains || []).join(','),
      (c.excludedRequestDomains || []).join(',')].join('\t'));
  }
}
const domainText = '# EasyList authors https://easylist.to/ - GPL-3.0-or-later\n' +
  '# Derived from uBlock Origin Lite 2026.914.1325. See FILTERS-NOTICE.txt.\n' +
  [...domains].sort().join('\n') + '\n';
const allowText = '# URL filter, request hosts, initiator hosts, excluded initiators, excluded requests (TSV)\n' +
  exceptions.join('\n') + '\n';
const files = { 'easylist_domains.txt': domainText, 'easylist_allow.tsv': allowText };
for (const [name, text] of Object.entries(files)) {
  if (process.argv.includes('--check')) {
    if (fs.readFileSync(path.join(assets, name), 'utf8') !== text) throw new Error(name + ' is stale');
  } else fs.writeFileSync(path.join(assets, name), text);
}
console.log(`EasyList assets: ${domains.size} domains and ${exceptions.length} exceptions.`);

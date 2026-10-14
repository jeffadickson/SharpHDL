// Learner-route checks against the actual browser controls. No compiler required.
// node tests/check-learner-controls.js --browser
// CHROME_BIN optionally selects an installed Chromium executable.
const fs = require('fs'), path = require('path'), assert = require('assert'), vm = require('vm');
const file = path.resolve(__dirname, '../docs/interactive/SharpHDL_Learning_Lab.html');
const html = fs.readFileSync(file, 'utf8');
for (const script of html.matchAll(/<script>([\s\S]*?)<\/script>/g)) new vm.Script(script[1]);
assert(!/__DATA__|__UI__|__GRAPH_SIM__/.test(html), 'Unexpanded learning-lab placeholder');
console.log('PASS learner lab inline JavaScript parses');
// Separate graph checks provide useful evidence without pretending to exercise UI.
const GraphSim = require('../docs/interactive/graph-sim.js');
const data = JSON.parse(fs.readFileSync(path.resolve(__dirname, '../generated/learning/graphs.json')));
let graphChecks = 0;
for (let bits = 0; bits < 8; bits++) {
  const sim = new GraphSim(data.fsm.graph);
  for (const [index, id] of ['start', 'enable', 'stop'].entries()) sim.set(id, (bits >> index) & 1);
  const inputs = { ...sim.inputs };
  const check = (phase, ticks) => {
    assert.equal(sim.outputs().phase, BigInt(phase)); assert.equal(sim.outputs().ticks, BigInt(ticks)); graphChecks += 2;
  };
  sim.tick(); sim.tick(true); check(0, 0);
  assert.deepEqual(sim.inputs, inputs); graphChecks++;
  sim.set('stop', 0); sim.set('start', 1); sim.set('enable', 1);
  sim.tick(); check(1, 0); sim.set('start', 0);
  sim.tick(); check(1, 1); sim.set('enable', 0);
  sim.tick(); check(1, 1); sim.tick(); check(1, 1); sim.set('enable', 1);
  sim.tick(); check(1, 2); sim.tick(); check(1, 3); sim.tick(); check(2, 4);
  sim.tick(); check(0, 4); sim.tick(); check(0, 0);
}
console.log(`PASS ${graphChecks} graph-only learner-route assertions (not browser control tests)`);
if (!process.argv.includes('--browser')) {
  console.log('SKIP actual learner-control browser checks (add --browser)');
} else (async () => {
  const { chromium } = require('playwright');
  const browser = await chromium.launch({ headless: true,
    ...(process.env.CHROME_BIN ? { executablePath: process.env.CHROME_BIN } : {}), args: ['--no-sandbox'] });
  let checks = 0;
  try {
    const page = await browser.newPage();
    const errors = [];
    page.on('pageerror', error => errors.push(String(error)));
    // This self-contained graph simulator deliberately needs no WASM/network server.
    await page.goto(require('url').pathToFileURL(file).href);
    await page.clock.install();
    const read = () => page.evaluate(() => ({
      status: document.getElementById('status').textContent,
      ticks: Number(document.getElementById('tickValue').textContent)
    }));
    const state = async (name, ticks) => {
      const actual = await read();
      assert(actual.status.startsWith(`State: ${name}.`), actual.status);
      assert.equal(actual.ticks, ticks, name + ' count'); checks += 2;
    };
    const edge = () => page.click('#step');
    const set = (id, value) => page.locator('#' + id).setChecked(value);
    for (let bits = 0; bits < 8; bits++) {
      // Every possible starting checkbox combination must survive Reset edge.
      for (const [index, id] of ['start', 'enable', 'stop'].entries()) await set(id, !!(bits & (1 << index)));
      await page.click('#auto');
      assert.equal(await page.locator('#auto').getAttribute('aria-pressed'), 'true'); checks++;
      await page.clock.runFor(650);
      await page.click('#reset');
      await state('Ready', 0);
      assert.equal(await page.locator('#auto').getAttribute('aria-pressed'), 'false'); checks++;
      for (const [index, id] of ['start', 'enable', 'stop'].entries()) {
        assert.equal(await page.locator('#' + id).isChecked(), !!(bits & (1 << index))); checks++;
      }
      const resetSnapshot = await read();
      await page.clock.runFor(1300);
      assert.deepEqual(await read(), resetSnapshot, 'Reset must stop automatic clock edges'); checks++;
      // Teacher startup works regardless of the preserved inputs.
      await set('stop', false); await set('start', true); await set('enable', true);
      assert.deepEqual(await read(), resetSnapshot, 'Checkbox edits cannot clock registers'); checks++;
      await edge(); await state('Running', 0);
      await set('start', false);
      await edge(); await state('Running', 1);
      await set('enable', false);
      await edge(); await state('Running', 1);
      await edge(); await state('Running', 1);
      await set('enable', true);
      await edge(); await state('Running', 2);
      await edge(); await state('Running', 3);
      await edge(); await state('Done', 4);
      await edge(); await state('Ready', 4);
      await edge(); await state('Ready', 0);
    }
    // The extension uses combinational logic: input edits update without a clock.
    const beforeLoop = await read();
    await page.click('[data-tab="loops"]');
    for (const kind of ['forloop', 'whileloop']) {
      await page.selectOption('#loopKind', kind);
      await page.fill('#a', '2'); await page.fill('#b', '0');
      assert.equal(await page.locator('#loopResult').innerText(), '2'); checks++;
      await page.fill('#a', '7');
      assert.equal(await page.locator('#loopResult').innerText(), '7'); checks++;
      await page.fill('#b', '1');
      assert.equal(await page.locator('#loopResult').innerText(), kind === 'forloop' ? '19' : '11'); checks++;
    }
    assert.deepEqual(await read(), beforeLoop, 'Combinational lesson must not clock controller'); checks++;
    await page.click('[data-tab="fixed"]');
    await page.selectOption('#shift', '1');
    for (const [raw, floor, even] of [[-3, '-0.125', '-0.125'], [3, '0.0625', '0.125']]) {
      await page.locator('#raw').evaluate((slider, value) => {
        slider.value = String(value); slider.dispatchEvent(new Event('input', { bubbles: true }));
      }, raw);
      assert.equal(await page.locator('#floor').innerText(), floor); checks++;
      assert.equal(await page.locator('#even').innerText(), even); checks++;
    }
    assert(!(await page.locator('#fixed .challenge').innerText()).includes('0.135'), 'Fixed-point teaching text must use 0.125'); checks++;
    assert.deepEqual(errors, [], 'Browser JavaScript errors'); checks++;
    console.log(`PASS ${checks} actual Chromium learner-control checks: all 8 reset/start combinations, pause, completion, and immediate loop outputs`);
  } finally { await browser.close(); }
})().catch(error => { console.error(error); process.exitCode = 1; });

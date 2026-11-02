// make_sign_digits.js
// Makes Adaptive_ADAS/Assets/_Project/Textures/SpeedSignDigits.png, the digit texture of the
// speed-limit signs (SpeedLimitSigns.cs). Kept here so the texture can be re-made and checked.
// You do NOT need to run this: the PNG is already in the project.
//
// Needs Node.js, the "playwright" package with Chromium, and the Liberation Sans Bold font
// (free font, SIL Open Font License; on Linux usually in /usr/share/fonts/truetype/liberation/).
// Run:  node make_sign_digits.js <output.png> <any-local.html> [path-to-LiberationSans-Bold.ttf]
// (the HTML file is only needed so the browser may load the local font file).
//
// Result: 2048 x 256 px RGBA, black digits on a transparent background, digit d centred in
// cell d (cells 204.8 px wide). All digits are scaled together so that the tallest ink is
// 200 px (y 28 .. 228). The script prints the font size, the advance (distance from one digit
// to the next) and the widest ink: SpeedLimitSigns.cs uses AdvancePx = 154.75 and MaxInkWidthPx = 157.

const { chromium } = require('playwright');
const fs = require('fs');

(async () => {
  const [, , outPng, htmlPage, fontPath = '/usr/share/fonts/truetype/liberation/LiberationSans-Bold.ttf'] = process.argv;
  fs.writeFileSync(htmlPage,
    `<!doctype html><style>@font-face{font-family:LSB;src:url(file://${fontPath})}</style><div style="font-family:LSB">x</div>`);
  const browser = await chromium.launch({ args: ['--allow-file-access-from-files'] });
  const page = await browser.newPage();
  await page.goto('file://' + require('path').resolve(htmlPage));
  const r = await page.evaluate(async () => {
    await document.fonts.load('100px LSB');
    const W = 2048, H = 256, cells = 10, cellW = W / cells, inkH = 200;
    const c = document.createElement('canvas'); c.width = W; c.height = H;
    const g = c.getContext('2d');
    // Tallest ink over all digits at a reference size of 1000 px.
    g.font = '1000px LSB';
    let asc = 0, desc = 0;
    for (let d = 0; d < 10; d++) {
      const m = g.measureText(String(d));
      asc = Math.max(asc, m.actualBoundingBoxAscent); desc = Math.max(desc, m.actualBoundingBoxDescent);
    }
    const fontPx = 1000 * inkH / (asc + desc);
    g.font = fontPx + 'px LSB'; g.fillStyle = '#000'; g.textAlign = 'center'; g.textBaseline = 'alphabetic';
    const baseline = (H - inkH) / 2 + asc * fontPx / 1000;
    const inkW = [];
    for (let d = 0; d < 10; d++) {
      g.fillText(String(d), (d + 0.5) * cellW, baseline);
      const m = g.measureText(String(d)); inkW.push(m.actualBoundingBoxLeft + m.actualBoundingBoxRight);
    }
    return { url: c.toDataURL('image/png'), fontPx, advancePx: g.measureText('0').width, maxInkWidthPx: Math.max(...inkW) };
  });
  fs.writeFileSync(outPng, Buffer.from(r.url.split(',')[1], 'base64'));
  delete r.url;
  console.log(JSON.stringify(r));   // expected: fontPx ~278.3, advancePx ~154.75, maxInkWidthPx ~156.5
  await browser.close();
})();

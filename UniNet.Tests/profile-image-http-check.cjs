// Run only against the isolated API configured with the test JWT issuer/key.
const assert = require('node:assert/strict');
const crypto = require('node:crypto');
const fs = require('node:fs');
const path = require('node:path');
const origin = 'http://localhost:5091';
const encode = value => Buffer.from(JSON.stringify(value)).toString('base64url');
const unsigned = `${encode({ alg: 'HS256', typ: 'JWT' })}.${encode({ sub: '11111111-1111-1111-1111-111111111111', iss: 'image-tests', aud: 'image-tests', exp: Math.floor(Date.now() / 1000) + 300 })}`;
const token = `${unsigned}.${crypto.createHmac('sha256', 'image-test-key-only-01234567890123456789').update(unsigned).digest('base64url')}`;
const png = Buffer.from('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+aAd8AAAAASUVORK5CYII=', 'base64');
const generated = [];
async function upload(bytes, authenticated = true) {
  const body = new FormData(); body.append('file', new Blob([bytes], { type: 'image/png' }), '../../photo.html');
  return fetch(`${origin}/api/profile/images`, { method: 'POST', body, headers: authenticated ? { Authorization: `Bearer ${token}` } : {} });
}
(async () => {
  try {
    assert.equal((await upload(png, false)).status, 401);
    const response = await upload(png); assert.equal(response.status, 200);
    const { url } = await response.json(); assert.equal(new URL(url).origin, origin);
    const name = new URL(url).pathname.split('/').at(-1); assert.match(name, /^[a-f0-9]{32}\.png$/); generated.push(name);
    const image = await fetch(url); assert.equal(image.status, 200);
    assert.equal(image.headers.get('content-type'), 'image/png'); assert.equal(image.headers.get('x-content-type-options'), 'nosniff');
    assert.deepEqual(Buffer.from(await image.arrayBuffer()), png);
    assert.equal((await upload(Buffer.from('<svg></svg>'))).status, 400);
    assert.ok([400, 413].includes((await upload(Buffer.alloc(5 * 1024 * 1024 + 1))).status));
    assert.equal((await fetch(`${origin}/api/profile/images/secret.svg`)).status, 404);
    console.log('Isolated HTTP upload: authentication, multipart binding, persistent PNG retrieval, MIME/nosniff, invalid type, size limit and missing file checks pass.');
  } finally {
    const root = path.resolve(__dirname, '../UniNet.Api/App_Data/profile-images');
    for (const name of generated) {
      const target = path.resolve(root, name); assert.equal(path.dirname(target), root);
      fs.unlinkSync(target);
    }
  }
})().catch(error => { console.error(error.message); process.exitCode = 1; });

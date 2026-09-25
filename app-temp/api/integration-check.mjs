import assert from 'node:assert/strict'
import { createHash } from 'node:crypto'

const base = 'http://127.0.0.1:5078/api'
async function call(path, method = 'GET', body) {
  const response = await fetch(base + path, { method, headers: body ? { 'Content-Type': 'application/json' } : {}, body: body ? JSON.stringify(body) : undefined })
  return { status: response.status, data: await response.json() }
}
const health = (await call('/health')).data
if (!health.mongo) throw new Error('Local MongoDB must be running for this integration check')
const first = (await call('/sessions', 'POST', { title: 'Synthetic A' })).data
const second = (await call('/sessions', 'POST', { title: 'Synthetic B' })).data
await call(`/sessions/${first.id}/materials`, 'POST', { name: 'A.txt', text: 'A-only synthetic source' })
await call(`/sessions/${second.id}/materials`, 'POST', { name: 'B.txt', text: 'B-only synthetic source' })
const one = (await call(`/sessions/${first.id}`)).data
assert.deepEqual(one.materials.map(x => x.name), ['A.txt'])
const note1 = (await call(`/sessions/${first.id}/notes`, 'POST', { markdown: '# First' })).data
const note2 = (await call(`/sessions/${first.id}/notes`, 'POST', { markdown: '# Second' })).data
assert.equal(note1.version, 1); assert.equal(note2.version, 2)
assert.deepEqual((await call(`/sessions/${first.id}/notes`)).data.map(x => x.markdown), ['# First', '# Second'])
assert.equal((await call(`/sessions/${second.id}`)).data.noteVersion, 0)

const wav = Buffer.alloc(44 + 16000 * 2)
wav.write('RIFF', 0); wav.writeUInt32LE(wav.length - 8, 4); wav.write('WAVEfmt ', 8)
wav.writeUInt32LE(16, 16); wav.writeUInt16LE(1, 20); wav.writeUInt16LE(1, 22)
wav.writeUInt32LE(16000, 24); wav.writeUInt32LE(32000, 28); wav.writeUInt16LE(2, 32); wav.writeUInt16LE(16, 34)
wav.write('data', 36); wav.writeUInt32LE(32000, 40)
const hash = createHash('sha256').update(wav).digest('hex')
async function upload(sessionId, sourceId, sequence, startMs, endMs, bytes, sha256) {
  const form = new FormData()
  for (const [key, value] of Object.entries({ sessionId, sourceId, sequence, startMs, endMs, sha256 })) form.set(key, String(value))
  form.set('file', new Blob([bytes], { type: 'audio/wav' }), 'synthetic.wav')
  const response = await fetch(base + '/chunks', { method: 'POST', body: form })
  return { status: response.status, data: await response.json() }
}
const sent = await upload(first.id, 'system', 0, 0, 1000, wav, hash)
assert.equal(sent.status, 202)
assert.equal(sent.data.status, 'pending-asr')
const replay = await upload(first.id, 'system', 0, 0, 1000, wav, hash)
assert.equal(replay.data.id, sent.data.id)
const secondSource = await upload(first.id, 'microphone', 0, 100, 1100, wav, hash)
assert.notEqual(secondSource.data.id, sent.data.id)
const secondSession = await upload(second.id, 'system', 0, 0, 1000, wav, hash)
assert.notEqual(secondSession.data.id, sent.data.id)
const range = (await call(`/sessions/${first.id}`)).data.chunks
assert.deepEqual(range.map(x => [x.sourceId, x.startMs, x.endMs]), [['system', 0, 1000], ['microphone', 100, 1100]])
assert.equal((await call(`/sessions/${second.id}`)).data.chunks.length, 1)
const denied = await call(`/sessions/${first.id}/notes/generate`, 'POST')
assert.equal(denied.status, 409)
console.log('Integration check passed: source isolation, note versions, chunk replay, time ranges, provider consent failure')

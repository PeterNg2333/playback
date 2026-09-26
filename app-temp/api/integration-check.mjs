import assert from 'node:assert/strict'
import { createHash } from 'node:crypto'

const base = process.env.PLAYBACK_OFFLINE_TEST === 'yes' ? 'http://127.0.0.1:5079/api' : 'http://127.0.0.1:5078/api'
async function call(path, method = 'GET', body) {
  const response = await fetch(base + path, { method, headers: body ? { 'Content-Type': 'application/json' } : {}, body: body ? JSON.stringify(body) : undefined })
  return { status: response.status, data: await response.json() }
}
const health = (await call('/health')).data
if (!health.mongo) throw new Error('Local MongoDB must be running for this integration check')
const created = []
let groupId
try {
const group = (await call('/groups', 'POST', { name: 'E2E group synthetic' })).data
groupId = group.id
const first = (await call('/sessions', 'POST', { title: 'E2E demo synthetic A', groupId })).data
created.push(first.id)
const second = (await call('/sessions', 'POST', { title: 'E2E demo synthetic B' })).data
created.push(second.id)
assert.equal((await call(`/sessions/${first.id}`)).data.groupId, groupId)
await call(`/sessions/${second.id}/group`, 'PUT', { groupId })
assert.equal((await call(`/sessions/${second.id}`)).data.groupId, groupId)
await call(`/sessions/${second.id}/group`, 'PUT', { groupId: null })
assert.equal((await call(`/sessions/${second.id}`)).data.groupId, null)
await call(`/groups/${groupId}`, 'PUT', { name: 'E2E group renamed' })
await call(`/sessions/${first.id}/materials`, 'POST', { name: 'A.txt', text: 'A-only synthetic source' })
await call(`/sessions/${second.id}/materials`, 'POST', { name: 'B.txt', text: 'B-only synthetic source' })
const one = (await call(`/sessions/${first.id}`)).data
assert.deepEqual(one.materials.map(x => x.name), ['A.txt'])
const note1 = (await call(`/sessions/${first.id}/notes`, 'POST', { markdown: '# First' })).data
const note2 = (await call(`/sessions/${first.id}/notes`, 'POST', { markdown: '# Second' })).data
assert.equal(note1.version, 1); assert.equal(note2.version, 2)
const versions = (await call(`/sessions/${first.id}/notes`)).data
assert.deepEqual(versions.map(x => x.markdown), ['# First', '# Second'])
assert.match(versions[1].inputHash, /^[a-f0-9]{64}$/)
assert.equal(versions[1].basedOnVersion, 1)
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
async function waitForChunk(sessionId, chunkId, status) {
  for (let attempt = 0; attempt < 50; attempt++) {
    const session = (await call(`/sessions/${sessionId}`)).data
    if (session.chunks.find(x => x.id === chunkId)?.status === status) return
    await new Promise(resolve => setTimeout(resolve, 100))
  }
  throw new Error(`Chunk ${chunkId} never reached ${status}`)
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
if (process.env.PLAYBACK_OFFLINE_TEST === 'yes') {
  async function expectSafeChunk(sessionId, chunkId) {
    if (process.env.PLAYBACK_PAUSE_EXTERNAL_ASR === 'yes') {
      assert.equal((await call(`/sessions/${sessionId}`)).data.chunks.find(x => x.id === chunkId)?.status, 'pending-asr')
    } else {
      await waitForChunk(sessionId, chunkId, 'silent')
    }
  }
  const fixture = await call(`/testing/sessions/${first.id}/transcripts`, 'POST', { chunkId: sent.data.id, text: 'Synthetic lecture uses Fourier Transform and FFT bins.' })
  assert.equal(fixture.status, 200)
  await call(`/sessions/${first.id}/translation`, 'PUT', { enabled: true, language: 'zh-Hant' })
  const translated = (await call(`/sessions/${first.id}`)).data
  assert.equal(translated.translationEnabled, true)
  assert.equal(translated.transcripts[0].translationStatus, 'pending', 'Existing transcript must be queued for translation')
  assert.deepEqual((await call(`/testing/sessions/${first.id}/translations/pending`)).data, [sent.data.id])
  const lateChunk = await upload(first.id, 'late', 0, 1200, 2200, wav, hash)
  await expectSafeChunk(first.id, lateChunk.data.id)
  await call(`/testing/sessions/${first.id}/transcripts`, 'POST', { chunkId: lateChunk.data.id, text: 'Synthetic FFT appears after settings were enabled.' })
  const emptyChunk = await upload(first.id, 'empty', 0, 2300, 3300, wav, hash)
  await expectSafeChunk(first.id, emptyChunk.data.id)
  await call(`/testing/sessions/${first.id}/transcripts`, 'POST', { chunkId: emptyChunk.data.id, text: '' })
  const emptySession = (await call(`/sessions/${first.id}`)).data
  assert.equal(emptySession.chunks.find(x => x.id === emptyChunk.data.id).status, 'asr-empty')
  assert.equal(emptySession.transcripts.find(x => x.id === emptyChunk.data.id).uncertain, false)
  assert.equal(emptySession.transcripts.find(x => x.id === emptyChunk.data.id).noteStatus, 'skipped')
  const reviewChunk = await upload(first.id, 'review', 0, 3400, 4400, wav, hash)
  await expectSafeChunk(first.id, reviewChunk.data.id)
  await call(`/testing/sessions/${first.id}/transcripts`, 'POST', { chunkId: reviewChunk.data.id, text: 'Synthetic speech was [unclear].' })
  assert.equal((await call(`/sessions/${first.id}`)).data.transcripts.find(x => x.id === reviewChunk.data.id).recognitionStatus, 'review-needed')
  const pendingTranslations = (await call(`/testing/sessions/${first.id}/translations/pending`)).data
  assert.deepEqual(new Set(pendingTranslations), new Set([sent.data.id, lateChunk.data.id, reviewChunk.data.id]), 'Existing and new nonempty transcripts must be queued')
  assert.ok(translated.terms.some(term => term.text === 'Fourier Transform'))
  const firstFailure = await call(`/sessions/${first.id}/notes/generate`, 'POST')
  assert.equal(firstFailure.status, 409)
  let failed = (await call(`/sessions/${first.id}`)).data.transcripts.find(x => x.id === sent.data.id)
  assert.equal(failed.noteStatus, 'failed')
  assert.equal(failed.noteAttempts, 1)
  const retried = await call(`/sessions/${first.id}/notes/generate`, 'POST')
  assert.equal(retried.status, 409)
  failed = (await call(`/sessions/${first.id}`)).data.transcripts.find(x => x.id === sent.data.id)
  assert.equal(failed.noteAttempts, 2)
  assert.equal((await call(`/sessions/${first.id}`)).data.noteMarkdown, '# Second', 'Failed AI must preserve the user note')
  await call(`/sessions/${first.id}/translation`, 'PUT', { enabled: false, language: 'zh-Hant' })
  assert.equal((await call(`/sessions/${first.id}`)).data.translationEnabled, false)
}
console.log(`Integration check passed: source isolation, note versions, ${process.env.PLAYBACK_PAUSE_EXTERNAL_ASR === 'yes' ? 'paused ASR queue' : 'automatic ASR queue'}, time ranges`)
} finally {
  for (const id of created) {
    const response = await fetch(`${base}/testing/sessions/${id}`, { method: 'DELETE' })
    assert.equal(response.status, 204, `Test session cleanup failed for ${id}`)
  }
  if (groupId) {
    const response = await fetch(`${base}/testing/groups/${groupId}`, { method: 'DELETE' })
    assert.equal(response.status, 204, `Test group cleanup failed for ${groupId}`)
  }
}

\set ON_ERROR_STOP on
\echo '1) Insert one coherent spike example inside a transaction'

BEGIN;

INSERT INTO lecture_sessions (id, title, state)
VALUES (:'session_id'::uuid, :'lecture_title'::text, 'STOPPED');

INSERT INTO audio_chunks (
    session_id,
    source_id,
    sequence,
    captured_at,
    duration_ms,
    byte_length,
    storage_path,
    status
)
VALUES (
    :'session_id'::uuid,
    'study-script',
    0,
    CURRENT_TIMESTAMP,
    15000,
    480000,
    '/spike/not-a-real-audio.wav',
    'TRANSCRIBED'
);

INSERT INTO transcript_segments (
    session_id,
    source_id,
    sequence,
    revision,
    transcript_text,
    language_code,
    provider
)
VALUES (
    :'session_id'::uuid,
    'study-script',
    0,
    1,
    'This is explicit demo data, not an ASR result.',
    'en',
    'demo'
);

INSERT INTO note_revisions (
    session_id,
    revision,
    through_sequence,
    markdown_text,
    provider,
    model
)
VALUES (
    :'session_id'::uuid,
    1,
    0,
    '# Demo note' || E'\n\nGenerated only for the SQL lesson.',
    'demo',
    'none'
);

INSERT INTO lecture_materials (session_id, title, content_text)
VALUES (
    :'session_id'::uuid,
    'SQL lesson context',
    'Primary keys identify rows; foreign keys preserve relationships.'
);

COMMIT;

\echo ''
\echo '2) JOIN related tables through primary/foreign keys'

SELECT
    s.title,
    c.source_id,
    c.sequence,
    c.status AS chunk_status,
    t.revision AS transcript_revision,
    t.transcript_text
FROM lecture_sessions AS s
JOIN audio_chunks AS c
    ON c.session_id = s.id
JOIN transcript_segments AS t
    ON t.session_id = c.session_id
   AND t.source_id = c.source_id
   AND t.sequence = c.sequence
WHERE s.id = :'session_id'::uuid
ORDER BY c.sequence, t.revision;

\echo ''
\echo '3) PostgreSQL-style positional parameters ($1, $2)'

PREPARE recent_segments (uuid, bigint) AS
SELECT sequence, revision, transcript_text
FROM transcript_segments
WHERE session_id = $1
  AND sequence >= $2
ORDER BY sequence, revision;

EXECUTE recent_segments(:'session_id'::uuid, 0);
DEALLOCATE recent_segments;

\echo ''
\echo '4) Ask the planner how it would run the expected lookup'

EXPLAIN (COSTS OFF)
SELECT sequence, revision, transcript_text
FROM transcript_segments
WHERE session_id = :'session_id'::uuid
ORDER BY sequence, revision DESC;

\echo ''
\echo 'The planner may prefer a sequential scan for this tiny study dataset.'

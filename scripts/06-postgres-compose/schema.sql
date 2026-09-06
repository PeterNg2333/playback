\set ON_ERROR_STOP on

-- Learning schema only. It proves relational constraints for the spike; it is not
-- an application migration and is not yet connected to Playback runtime code.

CREATE TABLE IF NOT EXISTS lecture_sessions (
    id uuid PRIMARY KEY,
    title text NOT NULL CHECK (char_length(title) BETWEEN 1 AND 200),
    state text NOT NULL DEFAULT 'CREATED'
        CHECK (state IN ('CREATED', 'RECORDING', 'STOPPED')),
    created_at timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP
);

CREATE TABLE IF NOT EXISTS audio_chunks (
    session_id uuid NOT NULL,
    source_id text NOT NULL CHECK (char_length(source_id) BETWEEN 1 AND 100),
    sequence bigint NOT NULL CHECK (sequence >= 0),
    captured_at timestamptz NOT NULL,
    duration_ms integer NOT NULL CHECK (duration_ms > 0),
    byte_length bigint NOT NULL CHECK (byte_length > 0),
    storage_path text NOT NULL,
    status text NOT NULL DEFAULT 'STORED'
        CHECK (status IN ('STORED', 'TRANSCRIBING', 'TRANSCRIBED', 'FAILED')),
    stored_at timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP,
    PRIMARY KEY (session_id, source_id, sequence),
    CONSTRAINT fk_audio_chunk_session
        FOREIGN KEY (session_id)
        REFERENCES lecture_sessions (id)
        ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS transcript_segments (
    id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    session_id uuid NOT NULL,
    source_id text NOT NULL,
    sequence bigint NOT NULL,
    revision integer NOT NULL DEFAULT 1 CHECK (revision >= 1),
    transcript_text text NOT NULL,
    language_code text NOT NULL DEFAULT 'unknown',
    provider text NOT NULL,
    created_at timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP,
    CONSTRAINT uq_transcript_revision
        UNIQUE (session_id, source_id, sequence, revision),
    CONSTRAINT fk_transcript_audio_chunk
        FOREIGN KEY (session_id, source_id, sequence)
        REFERENCES audio_chunks (session_id, source_id, sequence)
        ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS note_revisions (
    id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    session_id uuid NOT NULL,
    revision integer NOT NULL CHECK (revision >= 1),
    through_sequence bigint NOT NULL CHECK (through_sequence >= 0),
    markdown_text text NOT NULL,
    provider text NOT NULL,
    model text NOT NULL,
    created_at timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP,
    CONSTRAINT uq_note_revision UNIQUE (session_id, revision),
    CONSTRAINT fk_note_session
        FOREIGN KEY (session_id)
        REFERENCES lecture_sessions (id)
        ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS lecture_materials (
    id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    session_id uuid NOT NULL,
    title text NOT NULL CHECK (char_length(title) BETWEEN 1 AND 200),
    content_text text NOT NULL,
    created_at timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP,
    CONSTRAINT fk_material_session
        FOREIGN KEY (session_id)
        REFERENCES lecture_sessions (id)
        ON DELETE CASCADE
);

-- PRIMARY KEY and UNIQUE constraints already create supporting unique B-tree
-- indexes. These extra indexes serve expected filtering/order paths and the
-- referencing side of foreign keys.
CREATE INDEX IF NOT EXISTS ix_audio_chunks_session_stored
    ON audio_chunks (session_id, stored_at);

CREATE INDEX IF NOT EXISTS ix_transcripts_session_sequence
    ON transcript_segments (session_id, sequence, revision DESC);

CREATE INDEX IF NOT EXISTS ix_materials_session_created
    ON lecture_materials (session_id, created_at);

COMMENT ON TABLE lecture_sessions IS
    'Spike-only lecture identity and lifecycle.';
COMMENT ON TABLE audio_chunks IS
    'Durably accepted audio chunk metadata; the composite primary key makes retry idempotent.';
COMMENT ON TABLE transcript_segments IS
    'Versioned ASR text derived from one audio chunk.';
COMMENT ON COLUMN note_revisions.through_sequence IS
    'Highest contiguous transcript sequence included by this note revision.';

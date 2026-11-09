-- Shazamzin — schema do SQLite (ver docs/architecture.md, seção 5.4)
-- Idempotente: pode ser reaplicado sem erro.

PRAGMA journal_mode = WAL;
PRAGMA foreign_keys = ON;

-- Vincula este catálogo ao índice Valkey e ao perfil exato de fingerprinting.
CREATE TABLE IF NOT EXISTS catalog_settings (
  key TEXT PRIMARY KEY,
  value TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS songs (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  title TEXT NOT NULL,
  artist TEXT NOT NULL,
  album TEXT,
  duration_sec INTEGER,
  file_path TEXT NOT NULL,
  file_hash TEXT NOT NULL UNIQUE,     -- SHA256 do arquivo, pra idempotência
  fingerprint_count INTEGER,
  added_at TEXT NOT NULL DEFAULT (datetime('now'))
);
CREATE INDEX IF NOT EXISTS idx_songs_file_hash ON songs(file_hash);

CREATE TABLE IF NOT EXISTS ingestion_log (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  file_path TEXT NOT NULL,
  file_hash TEXT,
  song_id INTEGER REFERENCES songs(id),
  started_at TEXT NOT NULL,
  finished_at TEXT,
  status TEXT NOT NULL,   -- 'pending' | 'done' | 'skipped' | 'failed'
  error_message TEXT
);
CREATE INDEX IF NOT EXISTS idx_ingestion_log_status ON ingestion_log(status);
CREATE INDEX IF NOT EXISTS idx_ingestion_log_song_status ON ingestion_log(song_id, status);

CREATE TABLE IF NOT EXISTS match_log (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  device_id TEXT NOT NULL,
  matched_song_id INTEGER REFERENCES songs(id),
  confidence REAL,
  hashes_received INTEGER,
  hashes_hit INTEGER,
  processing_time_ms INTEGER,
  timestamp TEXT NOT NULL DEFAULT (datetime('now'))
);

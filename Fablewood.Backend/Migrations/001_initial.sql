CREATE TABLE IF NOT EXISTS schema_migrations(version integer PRIMARY KEY, applied_at timestamptz NOT NULL DEFAULT clock_timestamp());
CREATE TABLE IF NOT EXISTS accounts (
 player_id uuid PRIMARY KEY, name text NOT NULL UNIQUE,
 password_salt bytea NOT NULL CHECK(octet_length(password_salt)=32),
 password_hash bytea NOT NULL CHECK(octet_length(password_hash)=32),
 password_iterations integer NOT NULL CHECK(password_iterations=600000),
 initial_class integer NOT NULL CHECK(initial_class BETWEEN 0 AND 2),
 created_at timestamptz NOT NULL DEFAULT clock_timestamp());
CREATE TABLE IF NOT EXISTS sessions (
 token_hash bytea PRIMARY KEY CHECK(octet_length(token_hash)=32), player_id uuid NOT NULL REFERENCES accounts,
 expires_at timestamptz NOT NULL, created_at timestamptz NOT NULL DEFAULT clock_timestamp());
CREATE INDEX IF NOT EXISTS sessions_expiry ON sessions(expires_at);
CREATE TABLE IF NOT EXISTS join_tickets (
 token_hash bytea PRIMARY KEY CHECK(octet_length(token_hash)=32), player_id uuid NOT NULL REFERENCES accounts,
 expires_at timestamptz NOT NULL, consumed_at timestamptz, created_at timestamptz NOT NULL DEFAULT clock_timestamp());
CREATE INDEX IF NOT EXISTS tickets_expiry ON join_tickets(expires_at);
CREATE TABLE IF NOT EXISTS profiles (
 player_id uuid PRIMARY KEY REFERENCES accounts, revision bigint NOT NULL DEFAULT 0 CHECK(revision>=0),
 profile bytea CHECK(profile IS NULL OR octet_length(profile) BETWEEN 1 AND 262144),
 gold integer NOT NULL DEFAULT 0 CHECK(gold BETWEEN 0 AND 1000000),
 ore integer NOT NULL DEFAULT 0 CHECK(ore BETWEEN 0 AND 100000),
 essence integer NOT NULL DEFAULT 0 CHECK(essence BETWEEN 0 AND 100000),
 root_cores integer NOT NULL DEFAULT 0 CHECK(root_cores BETWEEN 0 AND 100000),
 moon_crystals integer NOT NULL DEFAULT 0 CHECK(moon_crystals BETWEEN 0 AND 100000),
 health_potions integer NOT NULL DEFAULT 3 CHECK(health_potions BETWEEN 0 AND 20),
 mana_potions integer NOT NULL DEFAULT 2 CHECK(mana_potions BETWEEN 0 AND 20),
 lease_id uuid, lease_server text, lease_expires_at timestamptz,
 CHECK((lease_id IS NULL AND lease_server IS NULL AND lease_expires_at IS NULL) OR (lease_id IS NOT NULL AND lease_server IS NOT NULL AND lease_expires_at IS NOT NULL)),
 updated_at timestamptz NOT NULL DEFAULT clock_timestamp());
CREATE TABLE IF NOT EXISTS operations (
 player_id uuid NOT NULL REFERENCES accounts, operation_id uuid NOT NULL,
 request_hash bytea NOT NULL CHECK(octet_length(request_hash)=32), revision bigint NOT NULL,
 created_at timestamptz NOT NULL DEFAULT clock_timestamp(), PRIMARY KEY(player_id,operation_id));
CREATE TABLE IF NOT EXISTS economy_ledger (
 player_id uuid NOT NULL, operation_id uuid NOT NULL, revision bigint NOT NULL,
 gold_delta integer NOT NULL, ore_delta integer NOT NULL, essence_delta integer NOT NULL,
 root_cores_delta integer NOT NULL, moon_crystals_delta integer NOT NULL,
 health_potions_delta integer NOT NULL, mana_potions_delta integer NOT NULL,
 created_at timestamptz NOT NULL DEFAULT clock_timestamp(), PRIMARY KEY(player_id,operation_id),
 FOREIGN KEY(player_id,operation_id) REFERENCES operations(player_id,operation_id));
INSERT INTO schema_migrations(version) VALUES(1) ON CONFLICT DO NOTHING;

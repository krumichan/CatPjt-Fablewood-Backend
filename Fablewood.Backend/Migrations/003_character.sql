-- Add projections without rewriting legacy blob, wallet or inventory rows.
CREATE TABLE character_summaries (
 player_id uuid PRIMARY KEY REFERENCES profiles(player_id),
 version integer NOT NULL CHECK(version=1), hero_class integer NOT NULL CHECK(hero_class BETWEEN 0 AND 2),
 level integer NOT NULL CHECK(level BETWEEN 1 AND 20), experience integer NOT NULL CHECK(experience>=0),
 class_tier integer NOT NULL CHECK(class_tier BETWEEN 1 AND 3),
 CHECK((level<20 AND experience<level*100) OR (level=20 AND experience=0)),
 CHECK(class_tier<2 OR level>=2), CHECK(class_tier<3 OR level>=4));
CREATE TABLE character_journal (
 player_id uuid NOT NULL, operation_id uuid NOT NULL, revision bigint NOT NULL,
 before_summary jsonb, after_summary jsonb NOT NULL,
 created_at timestamptz NOT NULL DEFAULT clock_timestamp(), PRIMARY KEY(player_id,operation_id),
 FOREIGN KEY(player_id,operation_id) REFERENCES operations(player_id,operation_id));
-- Legacy tickets retain their original expiry. All newly issued tickets are tied to a session.
ALTER TABLE join_tickets ADD COLUMN session_token_hash bytea REFERENCES sessions(token_hash);
CREATE INDEX tickets_session ON join_tickets(session_token_hash);
CREATE TABLE released_leases (
 player_id uuid NOT NULL REFERENCES accounts(player_id), lease_id uuid NOT NULL,
 released_at timestamptz NOT NULL DEFAULT clock_timestamp(), PRIMARY KEY(player_id,lease_id));
INSERT INTO schema_migrations(version) VALUES(3);

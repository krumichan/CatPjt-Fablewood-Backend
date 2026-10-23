ALTER TABLE profiles ADD COLUMN inventory_version integer NOT NULL DEFAULT 0;
ALTER TABLE profiles ADD COLUMN hero_class integer;
ALTER TABLE profiles ADD COLUMN next_local_id integer;
ALTER TABLE profiles ADD COLUMN equipped_weapon_instance_id uuid;
ALTER TABLE profiles ADD COLUMN equipped_armor_instance_id uuid;
ALTER TABLE profiles ADD CONSTRAINT profiles_inventory_metadata CHECK (
 (inventory_version=0 AND hero_class IS NULL AND next_local_id IS NULL AND equipped_weapon_instance_id IS NULL AND equipped_armor_instance_id IS NULL)
 OR (inventory_version=1 AND hero_class IS NOT NULL AND hero_class BETWEEN 0 AND 2
     AND next_local_id IS NOT NULL AND next_local_id BETWEEN 5 AND 999999
     AND equipped_weapon_instance_id IS NOT NULL AND equipped_armor_instance_id IS NOT NULL
     AND equipped_weapon_instance_id<>'00000000-0000-0000-0000-000000000000'
     AND equipped_armor_instance_id<>'00000000-0000-0000-0000-000000000000'));
CREATE TABLE inventory_items (
 instance_id uuid PRIMARY KEY CHECK(instance_id<>'00000000-0000-0000-0000-000000000000'),
 owner_player_id uuid NOT NULL REFERENCES profiles(player_id),
 local_id integer NOT NULL CHECK(local_id BETWEEN 1 AND 999998),
 retired boolean NOT NULL DEFAULT false,
 location integer, position integer,
 slot integer NOT NULL CHECK(slot BETWEEN 0 AND 1),
 hero_class integer NOT NULL CHECK(hero_class BETWEEN 0 AND 2),
 rarity integer NOT NULL CHECK(rarity BETWEEN 0 AND 4),
 power integer NOT NULL CHECK(power BETWEEN 0 AND 104),
 upgrade integer NOT NULL CHECK(upgrade BETWEEN 0 AND 5),
 roll_version integer NOT NULL CHECK(roll_version BETWEEN 0 AND 2),
 quality integer NOT NULL CHECK(quality BETWEEN 0 AND 4),
 nominal_power integer NOT NULL CHECK(nominal_power BETWEEN 0 AND 100),
 roll_percent integer NOT NULL CHECK(roll_percent BETWEEN 80 AND 140),
 set_id integer NOT NULL CHECK(set_id BETWEEN 0 AND 2),
 durability_version integer NOT NULL CHECK(durability_version BETWEEN 0 AND 1),
 durability integer NOT NULL CHECK(durability>=0),
 max_durability integer NOT NULL CHECK(max_durability BETWEEN 1 AND 150 AND durability<=max_durability),
 created_revision bigint NOT NULL CHECK(created_revision>0),
 updated_revision bigint NOT NULL CHECK(updated_revision>=created_revision),
 UNIQUE(owner_player_id,local_id),
 UNIQUE(owner_player_id,instance_id),
 -- Deferred uniqueness permits an atomic swap/reorder of two active positions.
 UNIQUE(owner_player_id,location,position) DEFERRABLE INITIALLY DEFERRED,
 CHECK((retired AND location IS NULL AND position IS NULL) OR
       (NOT retired AND location IS NOT NULL AND position IS NOT NULL AND
        ((location=0 AND position BETWEEN 0 AND 31) OR (location=1 AND position BETWEEN 0 AND 47))))
);
CREATE TABLE inventory_affixes (
 instance_id uuid NOT NULL REFERENCES inventory_items(instance_id),
 affix_index integer NOT NULL CHECK(affix_index BETWEEN 0 AND 2),
 stat integer NOT NULL CHECK(stat BETWEEN 0 AND 3),
 value integer NOT NULL,
 PRIMARY KEY(instance_id,affix_index), UNIQUE(instance_id,stat),
 CHECK((stat=0 AND value BETWEEN 2 AND 6) OR (stat=1 AND value BETWEEN 1 AND 4)
    OR (stat=2 AND value BETWEEN 5 AND 20) OR (stat=3 AND value BETWEEN 5 AND 15))
);
CREATE FUNCTION prevent_inventory_identity_change() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
 IF NEW.instance_id IS DISTINCT FROM OLD.instance_id OR NEW.owner_player_id IS DISTINCT FROM OLD.owner_player_id
    OR NEW.local_id IS DISTINCT FROM OLD.local_id OR (OLD.retired AND NOT NEW.retired) THEN
  RAISE EXCEPTION 'Inventory identity and retirement are immutable' USING ERRCODE='23514';
 END IF;
 RETURN NEW;
END;
$$;
CREATE TRIGGER inventory_identity_immutable BEFORE UPDATE ON inventory_items
 FOR EACH ROW EXECUTE FUNCTION prevent_inventory_identity_change();
ALTER TABLE profiles ADD CONSTRAINT profiles_weapon_owned
 FOREIGN KEY(player_id,equipped_weapon_instance_id) REFERENCES inventory_items(owner_player_id,instance_id) DEFERRABLE INITIALLY DEFERRED;
ALTER TABLE profiles ADD CONSTRAINT profiles_armor_owned
 FOREIGN KEY(player_id,equipped_armor_instance_id) REFERENCES inventory_items(owner_player_id,instance_id) DEFERRABLE INITIALLY DEFERRED;
CREATE TABLE inventory_journal (
 player_id uuid NOT NULL, operation_id uuid NOT NULL,
 revision bigint NOT NULL CHECK(revision>0),
 before_snapshot jsonb, after_snapshot jsonb NOT NULL,
 created_at timestamptz NOT NULL DEFAULT clock_timestamp(),
 PRIMARY KEY(player_id,operation_id),
 FOREIGN KEY(player_id,operation_id) REFERENCES operations(player_id,operation_id)
);
INSERT INTO schema_migrations(version) VALUES(2);

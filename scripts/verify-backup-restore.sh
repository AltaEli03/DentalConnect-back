#!/usr/bin/env bash
set -euo pipefail

: "${PGHOST:=127.0.0.1}"
: "${PGPORT:=5432}"
: "${PGUSER:=postgres}"
export PGHOST PGPORT PGUSER


source_db="dentalconnect_backup_source"
restore_db="dentalconnect_backup_restored"
dump_file="$(mktemp --suffix=.dump)"
trap 'rm -f "$dump_file"' EXIT

psql --set=ON_ERROR_STOP=1 --host="$PGHOST" --port="$PGPORT" --username="$PGUSER" --dbname=postgres \
  --command="CREATE DATABASE $source_db"
psql --set=ON_ERROR_STOP=1 --host="$PGHOST" --port="$PGPORT" --username="$PGUSER" --dbname="$source_db" <<'SQL'
CREATE TABLE clinics (
  id text PRIMARY KEY,
  display_name text NOT NULL
);
CREATE TABLE dentists (
  id text PRIMARY KEY,
  clinic_id text NOT NULL REFERENCES clinics(id),
  display_name text NOT NULL,
  UNIQUE (id, clinic_id)
);
CREATE TABLE appointment_requests (
  id text PRIMARY KEY,
  clinic_id text NOT NULL REFERENCES clinics(id),
  dentist_id text NOT NULL,
  status text NOT NULL CHECK (status IN ('PENDING', 'CONFIRMED')),
  starts_at timestamptz NOT NULL,
  CONSTRAINT fk_dentist_in_same_clinic
    FOREIGN KEY (dentist_id, clinic_id) REFERENCES dentists(id, clinic_id)
);

INSERT INTO clinics VALUES ('clinic-fake-001', 'Consultorio ficticio de pruebas');
INSERT INTO dentists VALUES ('dentist-fake-001', 'clinic-fake-001', 'Dentista ficticio');
INSERT INTO appointment_requests VALUES (
  'appointment-fake-001', 'clinic-fake-001', 'dentist-fake-001', 'PENDING', '2030-01-02T10:00:00Z'
);
SQL

pg_dump --format=custom --no-owner --no-acl \
  --host="$PGHOST" --port="$PGPORT" --username="$PGUSER" \
  --dbname="$source_db" --file="$dump_file"

psql --set=ON_ERROR_STOP=1 --host="$PGHOST" --port="$PGPORT" --username="$PGUSER" --dbname=postgres \
  --command="CREATE DATABASE $restore_db"
pg_restore --exit-on-error --no-owner --no-acl \
  --host="$PGHOST" --port="$PGPORT" --username="$PGUSER" \
  --dbname="$restore_db" "$dump_file"

assert_count() {
  local table="$1"
  local expected="$2"
  local actual
  actual="$(psql --no-psqlrc --tuples-only --no-align \
    --host="$PGHOST" --port="$PGPORT" --username="$PGUSER" --dbname="$restore_db" \
    --command="SELECT count(*) FROM $table")"
  if [[ "$actual" != "$expected" ]]; then
    echo "Restore verification failed: $table expected $expected rows, got $actual." >&2
    exit 1
  fi
}

assert_count clinics 1
assert_count dentists 1
assert_count appointment_requests 1

consistent_relations="$(psql --no-psqlrc --tuples-only --no-align \
  --host="$PGHOST" --port="$PGPORT" --username="$PGUSER" --dbname="$restore_db" \
  --command="SELECT count(*) FROM appointment_requests a JOIN dentists d ON d.id = a.dentist_id AND d.clinic_id = a.clinic_id")"
if [[ "$consistent_relations" != "1" ]]; then
  echo "Restore verification failed: relational references did not survive." >&2
  exit 1
fi

echo "Backup/restore passed: 1 fictitious clinic, 1 dentist, 1 pending request, relationships intact."
if [[ -n "${GITHUB_STEP_SUMMARY:-}" ]]; then
  {
    echo "## PostgreSQL fictitious-data backup and restore"
    echo "- Created a PostgreSQL custom-format dump."
    echo "- Restored into a separate empty database."
    echo "- Verified 1 clinic, 1 dentist, 1 pending request, and foreign-key relationships."
    echo "- This run used only the ephemeral PostgreSQL CI service; it did not access AWS/RDS."
  } >> "$GITHUB_STEP_SUMMARY"
fi

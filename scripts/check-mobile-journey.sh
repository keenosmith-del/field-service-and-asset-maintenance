#!/bin/sh
set -eu
cd "$(dirname "$0")/.."
docker build -f infra/Dockerfile.journey -t fieldservice-journey .
database_id=$(docker compose ps -q database)
[ -n "$database_id" ] || { echo 'Start docker compose first.' >&2; exit 1; }
network=$(docker inspect "$database_id" --format '{{range $name, $network := .NetworkSettings.Networks}}{{$name}}{{end}}')
docker run --rm --network "$network" --env-file .env -e API_URL=http://api:8080/ fieldservice-journey

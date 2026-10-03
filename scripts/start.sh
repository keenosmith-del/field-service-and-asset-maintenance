#!/bin/sh
set -eu
cd "$(dirname "$0")/.."
./scripts/setup.sh
docker compose up -d --build
cd web
npm ci
npm start

#!/bin/sh
set -eu
cd "$(dirname "$0")/.."
if [ ! -f .env ]; then
  umask 077
  printf 'POSTGRES_PASSWORD=%s\nJWT_KEY=%s\nDEMO_PASSWORD=%s\n' "$(openssl rand -hex 24)" "$(openssl rand -hex 32)" "$(openssl rand -hex 12)" > .env
fi
printf 'Configuration ready. Demo password is in .env (DEMO_PASSWORD).\n'

# Database Backups

## Creating a Backup

```bash
docker exec pulpmx-postgres pg_dump -U postgres pulpmx_fantasy > backups/pulpmx_fantasy_$(date +%Y%m%d_%H%M%S).sql
```

## Restoring from Backup

### Option 1: Restore to existing database (overwrites data)

```bash
# Stop the web and worker containers first
docker compose stop web worker

# Drop and recreate the database
docker exec -i pulpmx-postgres psql -U postgres -c "DROP DATABASE IF EXISTS pulpmx_fantasy;"
docker exec -i pulpmx-postgres psql -U postgres -c "CREATE DATABASE pulpmx_fantasy;"

# Restore from backup
docker exec -i pulpmx-postgres psql -U postgres pulpmx_fantasy < backups/pulpmx_fantasy_YYYYMMDD_HHMMSS.sql

# Restart containers
docker compose up -d web worker
```

### Option 2: Restore to a new database (for testing)

```bash
# Create a new database
docker exec -i pulpmx-postgres psql -U postgres -c "CREATE DATABASE pulpmx_fantasy_restored;"

# Restore to the new database
docker exec -i pulpmx-postgres psql -U postgres pulpmx_fantasy_restored < backups/pulpmx_fantasy_YYYYMMDD_HHMMSS.sql
```

## Listing Available Backups

```bash
ls -lh backups/*.sql
```

## Notes

- Replace `YYYYMMDD_HHMMSS` with the actual backup filename timestamp
- Backups include both schemas: `public` (write model) and `read_model` (read model)
- The postgres user password is defined in `.env` as `POSTGRES_PASSWORD`

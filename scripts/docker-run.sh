#!/bin/bash
# PulpMX Fantasy - Docker Build and Run Script
#
# USAGE:
#   ./scripts/docker-run.sh              # Build and start all services
#   ./scripts/docker-run.sh --no-build   # Start without rebuilding
#   ./scripts/docker-run.sh --down       # Stop all services
#   ./scripts/docker-run.sh --logs       # View logs
#   ./scripts/docker-run.sh --clean      # Stop and remove volumes (DELETES DATA!)

set -e

cd "$(dirname "$0")/.."

# Load .env file if it exists
if [ -f .env ]; then
    set -a  # automatically export all variables
    source .env
    set +a
fi

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
NC='\033[0m' # No Color

# Parse arguments
ACTION="up"
BUILD_FLAG="--build"

for arg in "$@"; do
    case $arg in
        --no-build)
            BUILD_FLAG=""
            ;;
        --down)
            ACTION="down"
            ;;
        --logs)
            ACTION="logs"
            ;;
        --clean)
            ACTION="clean"
            ;;
        *)
            ;;
    esac
done

case $ACTION in
    up)
        echo -e "${GREEN}Building and starting PulpMX Fantasy services...${NC}"
        echo ""

        # Check for API token
        if [ -z "$PULPMX_API_TOKEN" ]; then
            echo -e "${YELLOW}WARNING: PULPMX_API_TOKEN not set. API calls will fail.${NC}"
            echo -e "${YELLOW}Add it to .env file: PULPMX_API_TOKEN=your-token-here${NC}"
            echo ""
        fi

        # Build and start
        docker-compose up -d $BUILD_FLAG

        echo ""
        echo -e "${GREEN}Services started!${NC}"
        echo ""
        echo "Access points:"
        echo "  Web App:         http://localhost:8080"
        echo "  RabbitMQ UI:     http://localhost:15672 (admin/admin)"
        echo "  PostgreSQL:      localhost:5432 (postgres/postgres)"
        echo ""
        echo "Commands:"
        echo "  View logs:       docker-compose logs -f"
        echo "  Stop services:   ./scripts/docker-run.sh --down"
        echo "  Rebuild:         ./scripts/docker-run.sh"
        ;;

    down)
        echo -e "${YELLOW}Stopping services...${NC}"
        docker-compose down
        echo -e "${GREEN}Services stopped.${NC}"
        ;;

    logs)
        docker-compose logs -f
        ;;

    clean)
        echo -e "${RED}WARNING: This will delete all data (database, queues, models)!${NC}"
        read -p "Are you sure? (y/N) " -n 1 -r
        echo
        if [[ $REPLY =~ ^[Yy]$ ]]; then
            docker-compose down -v
            echo -e "${GREEN}Services stopped and volumes removed.${NC}"
        else
            echo "Cancelled."
        fi
        ;;
esac

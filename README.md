# BurmaProjectIdeasYarp

A YARP (Yet Another Reverse Proxy) gateway that routes API requests to multiple backend services.

## Overview

This project acts as a reverse proxy gateway that routes requests to the following backend APIs:

1. **BurmaCalendar** - Calendar API (configured in `api-burma-calendar-routes.json`)
2. **BurmeseRecipes** - Recipes API (configured in `api-burmese-recipes-routes.json`)
3. **MovieTicketOnlineBookingSystem** - Movie booking API (configured in `api-movie-ticket-online-booking-system-routes.json`)
4. **Snake** - Snake game API (configured in `api-snake-routes.json`)

## Configuration

The project uses split JSON configuration files for each backend service with `api-` prefix and kebab-case naming:

- `api-burma-calendar-routes.json` - Routes for BurmaCalendar API
- `api-burmese-recipes-routes.json` - Routes for BurmeseRecipes API  
- `api-movie-ticket-online-booking-system-routes.json` - Routes for MovieTicketOnlineBookingSystem API
- `api-snake-routes.json` - Routes for Snake API

Each configuration file defines:
- **Routes**: URL patterns that match incoming requests (using snake_case for route names)
- **Clusters**: Backend service destinations with addresses (using snake_case for cluster names)

## Backend Service Ports

- **BurmeseRecipes**: `http://localhost:5238`
- **MovieTicketOnlineBookingSystem**: `http://localhost:5015`
- **Snake**: `http://localhost:5036`
- **BurmaCalendar**: `http://localhost:5000` (default, update as needed)

## API Routes

All routes use kebab-case for the gateway paths and transform to the backend controller routes.

### BurmeseRecipes
- `/api/burmese-recipes/*` → `http://localhost:5238/api/BurmeseRecipe/*`

### Snake
- `/api/snake/*` → `http://localhost:5036/api/Snake/*`

### MovieTicketOnlineBookingSystem
- `/api/booking/*` → `http://localhost:5015/api/Booking/*`
- `/api/cinema/*` → `http://localhost:5015/api/Cinema/*`
- `/api/cinema-room/*` → `http://localhost:5015/api/CinemaRoom/*`
- `/api/movie/*` → `http://localhost:5015/api/Movie/*`
- `/api/movie-schedule/*` → `http://localhost:5015/api/MovieSchedule/*`
- `/api/room-seat/*` → `http://localhost:5015/api/RoomSeat/*`
- `/api/seat-price/*` → `http://localhost:5015/api/SeatPrice/*`
- `/api/show-date/*` → `http://localhost:5015/api/ShowDate/*`

### BurmaCalendar
- `/api/burma-calendar/*` → `http://localhost:5000/api/BurmaCalendar/*` (update port as needed)

## Running the Gateway

1. Ensure all backend services are running on their respective ports
2. Run the gateway:
   ```bash
   dotnet run
   ```
3. The gateway will be available at `http://localhost:5138` (HTTP) or `https://localhost:7093` (HTTPS)

## Adding New Routes

To add routes for a new service:

1. Create a new JSON configuration file following the naming convention: `api-{service-name}-routes.json` (use kebab-case)
2. Define the routes and clusters in the file using snake_case for route and cluster names
3. Add the file name to the `configFiles` array in `Program.cs`
4. Rebuild and run the project

Example file structure:
```json
{
  "ReverseProxy": {
    "Routes": {
      "new_service_route": {
        "ClusterId": "new_service_cluster",
        "Match": {
          "Path": "/api/new-service/{**catch-all}"
        },
        "Transforms": [
          {
            "PathPattern": "/api/NewService/{**catch-all}"
          }
        ]
      }
    },
    "Clusters": {
      "new_service_cluster": {
        "Destinations": {
          "new_service_destination": {
            "Address": "http://localhost:PORT/"
          }
        }
      }
    }
  }
}
```

## Naming Conventions

- **File names**: `api-{kebab-case-name}-routes.json`
- **Route names**: `{snake_case}_route`
- **Cluster names**: `{snake_case}_cluster`
- **Gateway paths**: `/api/{kebab-case-path}`
- **Backend paths**: `/api/{PascalCaseControllerName}` (transformed via PathPattern)

## Notes

- The configuration files are automatically merged at startup
- Route matching is case-insensitive
- All routes use the `{**catch-all}` pattern to forward the entire path to the backend
- Gateway paths use kebab-case for consistency, while backend controller routes use PascalCase

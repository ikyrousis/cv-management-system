# CV Management System

A simple CV management application built with ASP.NET Core 8.0 and React.

## Features

### Candidates Management
- Create, read, update, and delete candidates
- Required fields: First Name, Last Name, Email
- Optional fields: Mobile (10-digit validation), Degree selection
- CV file upload (PDF/Word documents)
- CV download and deletion functionality
- Email validation
- Mobile number validation (exactly 10 digits)

### Degrees Management
- Create, read, update, and delete degrees
- Unique degree names validation
- Cannot delete degrees that are assigned to candidates
- Cleanup functionality to remove unused degrees
- Shows candidate count for each degree

## Running the App

Prerequisites: .NET 8 SDK, Docker Desktop, and Node.js 14+ (frontend only).

### Full stack with Docker

```bash
cp .env.example .env   # then change MSSQL_SA_PASSWORD
docker compose up --build
```

Serves the frontend at http://localhost:3000, the API at http://localhost:5293 (Swagger is Development-only), and SQL Server at localhost:1433.

### Backend on the host

1. Start the database: `docker compose up -d sql`
2. Set the connection string once (Development only):
   ```bash
   dotnet user-secrets set "ConnectionStrings:CVManagement" "Server=localhost,1433;Database=CVManagementDB;User Id=sa;Password=<your .env password>;TrustServerCertificate=True"
   ```
3. `dotnet run --project backend/CVManagement`

### Tests

```bash
dotnet test backend/CVManagement.Tests
```

Unit tests always run. Integration tests spin up a SQL Server container via Testcontainers and skip themselves (logged as SKIPPED) when Docker is not running.

### Frontend on the host (development)

1. Navigate to the `frontend` directory
2. Run `npm install` to install dependencies
3. Run `npm start` to start the development server
4. Frontend will be available at `http://localhost:3000`

With the database only: `docker compose up -d sql`.

## Technical Requirements Met

### Backend (ASP.NET Core 8.0)
- Entity Framework Core with SQL Server (Dockerized)
- Proper validation on all required fields
- Email validation
- Mobile number validation (10 digits)
- File upload for CV (PDF/Word)
- RESTful API endpoints
- CORS configuration for frontend
- Swagger documentation

### Database Schema
- Candidates table with all required fields
- Degrees table with required fields
- Proper relationships between tables
- Auto-generated IDs
- CreationTime with default values
- CV blob storage

### Frontend (React)
- Complete CRUD operations for both entities
- Form validation
- File upload interface
- Responsive design with Bootstrap
- Error handling and user feedback
- Navigation between different sections

## Project Structure

The backend is a single project with three internal tiers. Dependencies point inward through
namespaces: controllers and services never touch `Data/` or `Repositories/` implementations
beyond their interfaces, and an architecture test enforces that for controllers.

| Folder | Tier | Responsibility |
| --- | --- | --- |
| `Controllers/`, `Extensions/`, `Program.cs` | Web | Thin controllers, HTTP routing, binding request DTOs, rendering service results as status codes, composition root |
| `DTOs/`, `Entities/`, `Results/`, `Services/`, `Validation/`, `Abstractions/` | Service | Business rules, entities, DTOs, service implementations, and the persistence interfaces they depend on |
| `Data/`, `Repositories/` | Persistence | EF Core context, repository implementations, unit of work, database initialiser, health check |

```
cv-management-system/
├── docker-compose.yml                   # SQL Server + API + frontend
├── .env.example                         # Template for MSSQL_SA_PASSWORD
├── .github/workflows/ci.yml             # Build + test backend, build frontend
├── backend/
│   ├── CVManagement.sln                 # Solution, at the backend root
│   ├── CVManagement/                    # Single backend project
│   │   ├── Abstractions/                # Repository and unit-of-work interfaces, owned by the service side
│   │   ├── Controllers/                 # Thin controllers, no EF and no business rules
│   │   ├── DTOs/                        # Request contracts and response read models
│   │   ├── Data/                        # ApplicationDbContext, initialiser, health check
│   │   ├── Entities/                    # Persistence-agnostic entities
│   │   ├── Extensions/                  # Service result -> IActionResult mapping
│   │   ├── Repositories/                # EF repository implementations, unit of work, projections
│   │   ├── Results/                     # Result / Result<T> and failure kinds
│   │   ├── Services/                    # Service interfaces + implementations
│   │   ├── Validation/                  # Shared input patterns
│   │   ├── Dockerfile
│   │   └── Program.cs                   # Composition root
│   └── CVManagement.Tests/              # Unit tests (NSubstitute) + integration tests (Testcontainers)
├── frontend/                            # React Frontend
│   ├── src/
│   │   ├── components/                  # React Components
│   │   ├── services/                    # API Service layer
│   │   └── App.js                       # Main App component
│   ├── public/                          # Static files
│   ├── Dockerfile                       # Node build stage -> nginx serving the bundle
│   └── nginx.conf                       # SPA fallback routing
└── README.md
```

DTOs are split by direction: `*Request` types carry only client-settable fields and are the sole
place validation attributes are declared, while `*Dto` types are response read models produced by
the query projections.

## API Endpoints

### Candidates
- `GET /api/candidates` - Get all candidates
- `GET /api/candidates/{id}` - Get candidate by ID
- `POST /api/candidates` - Create new candidate
- `PUT /api/candidates/{id}` - Update candidate
- `DELETE /api/candidates/{id}` - Delete candidate
- `POST /api/candidates/{id}/cv` - Upload CV file
- `GET /api/candidates/{id}/cv` - Download CV file
- `DELETE /api/candidates/{id}/cv` - Delete CV file

### Degrees
- `GET /api/degrees` - Get all degrees
- `GET /api/degrees/{id}` - Get degree by ID
- `POST /api/degrees` - Create new degree
- `PUT /api/degrees/{id}` - Update degree
- `DELETE /api/degrees/{id}` - Delete degree
- `DELETE /api/degrees/cleanup` - Delete unused degrees

## Features Implemented

1. **Candidate Management**
   - Full CRUD operations
   - Form validation (required fields, email format, mobile format)
   - CV file upload/download/delete
   - Degree selection from dropdown

2. **Degree Management**
   - Full CRUD operations
   - Unique name validation
   - Protection against deleting degrees in use
   - Bulk cleanup of unused degrees

3. **Data Validation**
   - Server-side validation with proper error messages
   - Client-side validation for better UX
   - Email format validation
   - Mobile number format validation (exactly 10 digits)

4. **File Management**
   - CV upload with file type restrictions
   - File download functionality
   - File deletion with confirmation

5. **User Experience**
   - Responsive design
   - Loading states
   - Error and success messages
   - Confirmation dialogs for destructive actions
   - Intuitive navigation

## Notes

- Data persists in SQL Server; the Docker volume `sqldata` keeps it across container restarts
- CV files are stored in the database as `varbinary`; uploads are capped at 20 MB
- `GET /health` reports Healthy while the app can reach SQL Server
- The database starts empty; degrees are created through the UI or API
- The frontend includes proper error handling and user feedback
- All validation requirements from the assignment are implemented
- The cleanup functionality prevents orphaned degree records

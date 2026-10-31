# CV Management System

A simple CV management application built with ASP.NET Core 8.0 and React.

## Features

### Candidates Management
- Create, read, update, and delete candidates
- Required fields: First Name, Last Name, Email (each trimmed on the way in; names up to 50 characters, email up to 100)
- Optional fields: Mobile (10-digit validation), Degree selection
- CV file upload (PDF/Word documents, file name up to 255 characters)
- CV download and deletion functionality
- Email validation
- Mobile number validation (exactly 10 digits; surrounding whitespace is ignored)

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

Compose mode and host mode both bind ports 5293 and 3000, so they cannot run at the same time. Run `docker compose down` before starting the backend or frontend on the host.

### Backend on the host

Requires no other stack running: `docker compose down` first (see port note above).

1. Start the database: `docker compose up -d sql`
2. Set the connection string and encryption key once (Development only):
   ```bash
   dotnet user-secrets set "ConnectionStrings:CVManagement" "Server=localhost,1433;Database=CVManagementDB;User Id=sa;Password=<your .env password>;TrustServerCertificate=True"
   dotnet user-secrets set "CvEncryption:Key" "$(openssl rand -base64 32)"
   ```
3. `dotnet run --project backend/CVManagement`

### Tests

```bash
dotnet test backend/CVManagement.Tests
```

Unit tests always run. Integration tests boot the real API against a SQL Server container via Testcontainers and require Docker; without it they fail.

Frontend tests are deferred: `react-scripts` test ships jest 27, which cannot resolve `react-router-dom` v7, and every component imports the router. Revisit by downgrading the router or replacing the CRA toolchain.

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
| `Controllers/`, `Extensions/`, `Program.cs` | Web | Thin controllers, HTTP routing, binding request DTOs, rendering service results as status codes and decrypt faults as sanitized errors, composition root |
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
│   │   ├── Extensions/                  # Service result -> IActionResult mapping, decrypt fault -> sanitized 500
│   │   ├── Repositories/                # EF repository implementations, unit of work, projections
│   │   ├── Results/                     # Result / Result<T> and failure kinds
│   │   ├── Services/                    # Service interfaces + implementations
│   │   ├── Validation/                  # Shared input patterns and the CV size limit
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

List endpoints accept optional `?page=&pageSize=` (pageSize defaults to 10 when paging, max 100; without params the full list is returned). The total count is always sent in the `X-Total-Count` response header, which is exposed to cross-origin scripts through `Access-Control-Expose-Headers`.

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
   - Inbound text is trimmed at the request contract, so the rules are checked against the value that gets stored

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

- Structured logging via Serilog to the console (docker logs). We log requests (method, path, status, duration), startup, and infrastructure warnings; services and controllers contain no log statements.
- Privacy rule for any future log line: identify by numeric ID, never by content. Names, emails, mobiles, and CV file names are never logged; request bodies are never logged.
- App-level symmetric encryption (AES-256-GCM): candidate first/last name, email, mobile, CV file name, and CV bytes are encrypted by the application before storage; SQL Server holds only ciphertext for those columns. The key is a 32-byte base64 value from the environment or user-secrets, never committed
- Stored values are versioned: marker(1) | nonce(12) | tag(16) | ciphertext, base64 for text columns and raw bytes for the CV. The column name is authenticated data, so ciphertext copied from one protected column into another does not verify. Unversioned values written before that layout stay readable, and the fallback can go once no such row remains
- Copying a ciphertext between two rows of the same column still verifies: a value converter sees only the value, never the row id, so per-row binding needs either a MAC column over the row or encryption outside the converter layer
- Deleting a candidate and clearing its CV work from the key alone, so a row whose protected columns cannot be decrypted can still be removed and its CV still detached. Updating a candidate reads the protected columns, because clearing a stored mobile to empty has to be distinguishable from leaving it untouched.
- Encrypted columns carry no queryable meaning: a predicate over one is translated with a freshly encrypted operand, so it matches nothing and returns an empty result instead of failing, and ordering by one orders ciphertext. Every read therefore goes by `Id`, which stays plaintext, and `ArchitectureTests` keeps the queryable surface inside `Data/` and `Repositories/` so no other tier can write such a predicate by accident.
- Decrypt faults (`CryptographicException`, `FormatException`) escape the value converters during materialization. `Extensions/EncryptionExceptionHandler` answers them with a fixed 500 ProblemDetails carrying no stored content and no exception detail, in Development and in Production alike. Any request that reads an unreadable row fails as a whole, which for a list means the entire page, so the affected row has to be repaired or removed.
- The tracked CV byte array is only ever replaced with a new array, never edited in place: EF's change-tracking snapshot for `Candidate.CVFile` aliases the stored array, so an in-place edit would not be seen as a change.
- The stored payload has no key identifier, so rotating the key needs an offline re-encryption pass over every row. `AesGcmFormatCompatibilityTests` freezes the payload layout; changing it intentionally means updating that test in the same commit.
- Each value carries a fresh random 12-byte nonce under a single key, which keeps AES-GCM within its random-nonce safety bound only while the total number of encryptions per key stays far below 2^32. Rotation is what resets that budget.
- Because the converters capture the encryptor instance while EF builds the model, the model cache is keyed on that instance (`EncryptedModelCacheKeyFactory`), so a context configured with a different key cannot inherit a model bound to the first key.
- Losing the encryption key makes stored CVs and personal fields permanently unreadable; there is no recovery path
- `Microsoft.*` logging is pinned to Warning because EF Core logs SQL statements with parameter values at Information level
- Data persists in SQL Server; the Docker volume `sqldata` keeps it across container restarts
- CV files are stored in the database as encrypted `varbinary` (AES-256-GCM); uploads are capped at 20 MB and file names at 255 characters by `Validation/CvLimits`, enforced both in the request pipeline and in the service
- Encrypted string columns are `nvarchar(max)` holding base64, which costs about 2.7 times the raw ciphertext bytes (4/3 for base64, then 2 bytes per character) and cannot be indexed in SQL Server. `CVFile` is `varbinary(max)` and costs 29 bytes over the original. Because ciphertext length tracks plaintext length exactly, field lengths and CV sizes are visible to anyone who can read the table
- `GET /health` reports Healthy while the app can reach SQL Server
- The database starts empty; degrees are created through the UI or API
- The frontend includes proper error handling and user feedback
- All validation requirements from the assignment are implemented
- The cleanup functionality prevents orphaned degree records

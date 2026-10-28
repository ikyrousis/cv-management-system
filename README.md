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

## Technical Requirements Met

### Backend (ASP.NET Core 8.0)
- ✅ Entity Framework Core with In-Memory database
- ✅ Proper validation on all required fields
- ✅ Email validation
- ✅ Mobile number validation (10 digits)
- ✅ File upload for CV (PDF/Word)
- ✅ RESTful API endpoints
- ✅ CORS configuration for frontend
- ✅ Swagger documentation

### Database Schema
- ✅ Candidates table with all required fields
- ✅ Degrees table with required fields
- ✅ Proper relationships between tables
- ✅ Auto-generated IDs
- ✅ CreationTime with default values
- ✅ CV blob storage

### Frontend (React)
- ✅ Complete CRUD operations for both entities
- ✅ Form validation
- ✅ File upload interface
- ✅ Responsive design with Bootstrap
- ✅ Error handling and user feedback
- ✅ Navigation between different sections

## Project Structure

The backend is a three-tier solution. Dependencies point inward, so the service tier owns the
repository interfaces and the persistence tier implements them.

| Project | Tier | Responsibility |
| --- | --- | --- |
| `CVManagement.Api` | Web | Controllers, HTTP routing, binding request DTOs, rendering service results as status codes |
| `CVManagement.Application` | Service | Business rules, entities, DTOs, repository and unit-of-work interfaces |
| `CVManagement.Infrastructure` | Repository | EF Core context, query projections, repository implementations |

```
cv-management-system/
├── CVManagement.sln
├── backend/
│   ├── CVManagement.Api/                # Web tier
│   │   ├── Controllers/                 # Thin controllers, no EF and no business rules
│   │   ├── Extensions/                  # Service result -> IActionResult mapping
│   │   └── Program.cs                   # Composition root
│   ├── CVManagement.Application/        # Service tier
│   │   ├── Abstractions/                # ICandidateRepository, IDegreeRepository, IUnitOfWork
│   │   ├── Dtos/                        # Request contracts and response read models
│   │   ├── Entities/                    # Persistence-agnostic entities
│   │   ├── Results/                     # Result / Result<T> and failure kinds
│   │   ├── Services/                    # CandidateService, DegreeService
│   │   └── Validation/                  # Shared input patterns
│   └── CVManagement.Infrastructure/     # Repository tier
│       └── Persistence/                 # ApplicationDbContext, projections, repositories
├── frontend/                            # React Frontend
│   ├── src/
│   │   ├── components/                  # React Components
│   │   ├── services/                    # API Service layer
│   │   └── App.js                       # Main App component
│   └── public/                          # Static files
└── README.md
```

DTOs are split by direction: `*Request` types carry only client-settable fields and are the sole
place validation attributes are declared, while `*Dto` types are response read models produced by
the query projections.

## How to Run

### Prerequisites
- .NET 8.0 SDK
- Node.js (v14 or higher)
- npm

### Backend (API)
1. Open `CVManagement.sln`, or navigate to the `backend/CVManagement.Api` directory
2. Run `dotnet restore` to restore packages
3. Run `dotnet run` to start the API
4. API will be available at `http://localhost:5293`
5. Swagger documentation at `http://localhost:5293/swagger`

### Frontend (React)
1. Navigate to the frontend directory
2. Run `npm install` to install dependencies
3. Run `npm start` to start the development server
4. Frontend will be available at `http://localhost:3000`

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

## Database Seeding

The application automatically seeds the database with sample degrees:
- BSc (Bachelor of Science)
- MSc (Master of Science)
- PhD (Doctor of Philosophy)

## Notes

- The application uses an in-memory database, so data will be lost when the API is restarted
- CV files are stored in memory as byte arrays
- The frontend includes proper error handling and user feedback
- All validation requirements from the assignment are implemented
- The cleanup functionality prevents orphaned degree records

================================================================================
                                BMSL TRACKER
================================================================================

Overview
--------
BMSL Tracker is a real-time employee location tracking web application 
developed for Billing Master Software Ltd. It enables live geolocation 
monitoring, route history logging, and field team management using modern 
.NET technologies and real-time WebSocket communication.

--------------------------------------------------------------------------------

Tech Stack & Frameworks
-----------------------
- Framework:               ASP.NET Core 9.0 (MVC)
- Real-Time Communication: ASP.NET Core SignalR (TrackerHub)
- Database & ORM:          SQLite (app.db) with Entity Framework Core 9.0
- Authentication:          ASP.NET Core Identity
- UI & Frontend:           Razor Views (.cshtml), HTML5, CSS3, JavaScript

--------------------------------------------------------------------------------

Key Features
------------
1. Real-Time Geolocation Updates: 
   Uses SignalR WebSockets via `TrackerHub` to broadcast field personnel 
   location updates to the administrative monitoring dashboard instantly.

2. User Authentication & Security: 
   Integrated ASP.NET Core Identity for user registration, secure login, 
   and role-based authorization.

3. Location History & Logging: 
   `LocationService` handles background processing and persistent storage of 
   historical coordinate data inside the SQLite database (`app.db`).

--------------------------------------------------------------------------------

Repository Structure
--------------------
BMSL_Tracker/
├── Areas/
│   └── Identity/        # ASP.NET Core Identity pages and account views
├── Controllers/         # MVC Controllers (AccountController, HomeController, etc.)
├── Data/                # ApplicationDbContext and EF Core DB migrations
├── Hubs/                # SignalR Hubs (TrackerHub for live geolocation)
├── Models/              # Data models and ViewModels (UserLocation, etc.)
├── Services/            # Core business logic (LocationService)
├── Views/               # Razor Views and Shared Layouts
├── app.db               # SQLite Database file
└── Program.cs           # Application entry point & dependency injection setup

--------------------------------------------------------------------------------

Getting Started
---------------

Prerequisites:
- .NET 9.0 SDK (https://dotnet.microsoft.com/download/dotnet/9.0)
- Visual Studio 2022 (v17.12+) or Visual Studio Code

Installation Steps:

1. Extract/Navigate to the project directory:
   cd BMSL_Tracker

2. Restore NuGet dependencies:
   dotnet restore

3. Apply Database Migrations (if needed):
   dotnet ef database update

4. Run the application:
   dotnet run

5. Open your browser and navigate to:
   https://localhost:5001  or  http://localhost:5000

================================================================================

# BMSL Tracker

A real-time location tracking application built with Node.js, Socket.io, Leaflet, and OpenStreetMap.

## Features

- **Real-time Tracking**: See user locations update live on an interactive map.
- **Leaflet + OpenStreetMap**: Uses free, open-source mapping tools (replacing Google Maps).
- **Socket.io Integration**: Efficient, real-time communication between client and server.
- **Persistent Storage**: Stores user data and historical locations in a database.
- **Authentication**: Secure login and session management.

## Project Structure

```text
.
├── client/           # React frontend (Vite)
│   ├── src/
│   │   ├── components/  # Map and Login components
│   │   └── main.jsx     # App entry point
├── server/           # Node.js backend
│   ├── config/       # Database configuration
│   ├── models/       # Database models
│   ├── routes/       # API routes
│   └── index.js      # Server entry point
└── README.md
```

## Getting Started

### Prerequisites

- Node.js (v16 or higher)
- npm or yarn

### Installation

1. **Clone the repository**:
   ```bash
   git clone https://github.com/BigSmoke4/BMSL_Tracker.git
   cd BMSL_Tracker
   ```

2. **Setup the Server**:
   ```bash
   cd server
   npm install
   # Create a .env file with your database credentials
   node index.js
   ```

3. **Setup the Client**:
   ```bash
   cd ../client
   npm install
   npm run dev
   ```

## Usage

- Open your browser to `http://localhost:5173`.
- Register/Login to start sharing your location.
- Other active users will appear as markers on the map.

## License

Distributed under the MIT License. See `LICENSE` for more information.

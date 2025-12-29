# BMSL Tracker

BMSL Tracker is a robust application for tracking assets and monitoring data. This project has been updated to use OpenStreetMap and Leaflet for a free and flexible mapping solution, replacing the previous Google Maps implementation.

## Features
- Real-time asset tracking
- Map visualization using OpenStreetMap and Leaflet
- User-friendly interface

## Installation
1. Clone the repository:
   ```bash
      git clone https://github.com/BigSmoke4/BMSL_Tracker.git
         ```
         2. Navigate to the client directory and install dependencies:
            ```bash
               cd client
                  npm install
                     ```
                     3. Navigate to the server directory and install dependencies:
                        ```bash
                           cd ../server
                              npm install
                                 ```

                                 ## Running the Application
                                 ### Start the Server
                                 ```bash
                                 cd server
                                 node index.js
                                 ```
                                 ### Start the Client
                                 ```bash
                                 cd client
                                 npm run dev
                                 ```
                                 The application will be available at http://localhost:5173.
                                 
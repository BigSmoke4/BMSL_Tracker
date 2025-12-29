const express = require('express');
const http = require('http');
const cors = require('cors');
const { Server } = require('socket.io');
const sequelize = require('./config/database');
const authRoutes = require('./routes/auth');
const socketHandler = require('./socket/handler');
require('dotenv').config();

const app = express();
const server = http.createServer(app);

// Middleware
app.use(cors());
app.use(express.json());

// Routes
app.use('/api/auth', authRoutes);

// Socket.io Setup
const io = new Server(server, {
  cors: {
    origin: "*", // Allow all origins for simplicity in this task
    methods: ["GET", "POST"]
  }
});

io.on('connection', (socket) => {
  socketHandler(io, socket);
});

// Database & Server Start
const PORT = process.env.PORT || 3000;

async function startServer() {
  try {
    await sequelize.authenticate();
    console.log('Database connected.');
    // Sync models (force: false to avoid dropping tables, alter: true to update schema)
    await sequelize.sync({ alter: true });
    
    server.listen(PORT, () => {
      console.log(`Server running on port ${PORT}`);
    });
  } catch (error) {
    console.error('Unable to connect to the database:', error);
  }
}

startServer();

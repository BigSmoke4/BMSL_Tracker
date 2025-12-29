const { Sequelize } = require('sequelize');
const path = require('path');

// SQLite Setup - Zero Config
// The database file will be created at e:\Tracker\server\tracker.sqlite
const sequelize = new Sequelize({
    dialect: 'sqlite',
    storage: path.join(__dirname, '..', 'tracker.sqlite'), // Store in server root
    logging: false
});

module.exports = sequelize;

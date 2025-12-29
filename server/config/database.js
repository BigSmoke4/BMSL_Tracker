const { Sequelize } = require('sequelize');
const path = require('path');

const sequelize = new Sequelize({
    dialect: 'sqlite',
    storage: path.join(__dirname, '..', 'tracker.sqlite'),
    logging: (msg) => {
        if (msg.includes('ERROR')) console.error(msg);
    }
});

module.exports = sequelize;

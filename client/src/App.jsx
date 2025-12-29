import { useState, useEffect } from 'react';
import Login from './components/Login';
import MapHandler from './components/MapHandler';
import './index.css';

const App = () => {
  const [token, setToken] = useState(localStorage.getItem('token'));
  const [user, setUser] = useState(JSON.parse(localStorage.getItem('user') || 'null'));

  useEffect(() => {
    if (token) {
      localStorage.setItem('token', token);
    } else {
      localStorage.removeItem('token');
    }
  }, [token]);

  useEffect(() => {
    if (user) {
      localStorage.setItem('user', JSON.stringify(user));
    } else {
      localStorage.removeItem('user');
    }
  }, [user]);

  const handleLogin = (newToken, newUser) => {
    setToken(newToken);
    setUser(newUser);
  };

  const handleLogout = () => {
    setToken(null);
    setUser(null);
  };

  return (
    <div className="app-container">
      {!token ? (
        <Login onLogin={handleLogin} />
      ) : (
        <MapHandler user={user} onLogout={handleLogout} />
      )}
    </div>
  );
};

export default App;

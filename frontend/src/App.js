import React from 'react';
import { BrowserRouter as Router, Routes, Route, Link, Navigate, useNavigate } from 'react-router-dom';
import 'bootstrap/dist/css/bootstrap.min.css';
import CandidatesList from 'components/CandidatesList';
import CandidateForm from 'components/CandidateForm';
import DegreesList from 'components/DegreesList';
import DegreeForm from 'components/DegreeForm';
import LoginPage from 'components/LoginPage';
import { hasToken, clearToken } from 'services/api';
import './App.css';

// The token lives in memory, so this check is honest: without one, the API answers 401 for
// everything. The guard sends the visitor to the login page instead of letting every page
// fail the same way.
function RequireAuth({ children }) {
  if (!hasToken()) {
    return <Navigate to="/login" replace />;
  }

  return children;
}

function App() {
  return (
    <Router>
      <div className="App">
        <Navbar />

        <div className="container mt-4">
          <Routes>
            <Route path="/login" element={<LoginPage />} />
            <Route path="/" element={<RequireAuth><Home /></RequireAuth>} />
            <Route path="/candidates" element={<RequireAuth><CandidatesList /></RequireAuth>} />
            <Route path="/candidates/new" element={<RequireAuth><CandidateForm /></RequireAuth>} />
            <Route path="/candidates/edit/:id" element={<RequireAuth><CandidateForm /></RequireAuth>} />
            <Route path="/degrees" element={<RequireAuth><DegreesList /></RequireAuth>} />
            <Route path="/degrees/new" element={<RequireAuth><DegreeForm /></RequireAuth>} />
            <Route path="/degrees/edit/:id" element={<RequireAuth><DegreeForm /></RequireAuth>} />
          </Routes>
        </div>
      </div>
    </Router>
  );
}

function Navbar() {
  const navigate = useNavigate();
  const authenticated = hasToken();

  const handleLogout = () => {
    // Stateless tokens: the API cannot revoke anything, so logout means the client discards
    // the token. The token stays valid until it expires, which is the documented tradeoff.
    clearToken();
    navigate('/login');
  };

  return (
    <nav className="navbar navbar-expand-lg navbar-dark bg-primary">
      <div className="container">
        <Link className="navbar-brand" to="/">
          CV Management System
        </Link>
        <div className="navbar-nav">
          {authenticated && (
            <>
              <Link className="nav-link" to="/candidates">
                Candidates
              </Link>
              <Link className="nav-link" to="/degrees">
                Degrees
              </Link>
              <button type="button" className="btn btn-link nav-link" onClick={handleLogout}>
                Logout
              </button>
            </>
          )}
        </div>
      </div>
    </nav>
  );
}

function Home() {
  return (
    <div className="row">
      <div className="col-md-12">
        <h1>Welcome to CV Management System</h1>
        <p className="lead">
          This application allows you to manage candidates and their degrees.
        </p>
        <div className="row mt-4">
          <div className="col-md-6">
            <div className="card">
              <div className="card-body">
                <h5 className="card-title">Manage Candidates</h5>
                <p className="card-text">
                  Add, edit, and delete candidates. Upload and manage their CVs.
                </p>
                <Link to="/candidates" className="btn btn-primary">
                  View Candidates
                </Link>
              </div>
            </div>
          </div>
          <div className="col-md-6">
            <div className="card">
              <div className="card-body">
                <h5 className="card-title">Manage Degrees</h5>
                <p className="card-text">
                  Add, edit, and delete degree types. Clean up unused degrees.
                </p>
                <Link to="/degrees" className="btn btn-primary">
                  View Degrees
                </Link>
              </div>
            </div>
          </div>
        </div>
      </div>
    </div>
  );
}

export default App;

import React from 'react';
import { BrowserRouter as Router, Routes, Route, Link, Navigate, useNavigate } from 'react-router-dom';
import 'bootstrap/dist/css/bootstrap.min.css';
import CandidatesList from 'components/CandidatesList';
import CandidateForm from 'components/CandidateForm';
import DegreesList from 'components/DegreesList';
import DegreeForm from 'components/DegreeForm';
import LoginPage from 'components/LoginPage';
import { hasToken, clearSession, hasStoredRefreshToken, resumeSession, authAPI } from 'services/api';
import './App.css';

// The token lives in memory, and the app attempts one silent refresh on load when a stored
// credential exists, so this check is honest: without a usable session, the API answers 401 for
// everything. The guard sends the visitor to the login page instead of letting every page
// fail the same way.
function RequireAuth({ children }) {
  if (!hasToken()) {
    return <Navigate to="/login" replace />;
  }

  return children;
}

function App() {
  // A stored refresh credential means a session may be resumable, so the routes wait for the
  // one silent refresh instead of bouncing a returning user to the login page.
  const [authReady, setAuthReady] = React.useState(() => hasToken() || !hasStoredRefreshToken());

  React.useEffect(() => {
    if (authReady) {
      return;
    }

    let cancelled = false;

    resumeSession().finally(() => {
      if (!cancelled) {
        setAuthReady(true);
      }
    });

    return () => {
      cancelled = true;
    };
  }, [authReady]);

  return (
    <Router>
      <div className="App">
        <Navbar />

        <div className="container mt-4">
          {authReady ? (
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
          ) : (
            <p className="text-center mt-5">Restoring session...</p>
          )}
        </div>
      </div>
    </Router>
  );
}

function Navbar() {
  const navigate = useNavigate();
  const authenticated = hasToken();

  const handleLogout = async () => {
    // Logout is a server-side revocation: the API burns the refresh credential, so nothing can
    // rotate it again and the stored value is discarded here as well.
    try {
      await authAPI.logout();
    } catch {
      // The session ends locally regardless of whether the API call reached it.
    }

    clearSession();
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

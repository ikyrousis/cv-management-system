import React from 'react';
import { BrowserRouter as Router, Routes, Route, Link } from 'react-router-dom';
import 'bootstrap/dist/css/bootstrap.min.css';
import CandidatesList from 'components/CandidatesList';
import CandidateForm from 'components/CandidateForm';
import DegreesList from 'components/DegreesList';
import DegreeForm from 'components/DegreeForm';
import './App.css';

function App() {
  return (
    <Router>
      <div className="App">
        <nav className="navbar navbar-expand-lg navbar-dark bg-primary">
          <div className="container">
            <Link className="navbar-brand" to="/">
              CV Management System
            </Link>
            <div className="navbar-nav">
              <Link className="nav-link" to="/candidates">
                Candidates
              </Link>
              <Link className="nav-link" to="/degrees">
                Degrees
              </Link>
            </div>
          </div>
        </nav>

        <div className="container mt-4">
          <Routes>
            <Route path="/" element={<Home />} />
            <Route path="/candidates" element={<CandidatesList />} />
            <Route path="/candidates/new" element={<CandidateForm />} />
            <Route path="/candidates/edit/:id" element={<CandidateForm />} />
            <Route path="/degrees" element={<DegreesList />} />
            <Route path="/degrees/new" element={<DegreeForm />} />
            <Route path="/degrees/edit/:id" element={<DegreeForm />} />
          </Routes>
        </div>
      </div>
    </Router>
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

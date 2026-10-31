import React, { useState, useEffect, useCallback } from 'react';
import { Link } from 'react-router-dom';
import { candidatesAPI, readTotal, DEFAULT_PAGE_SIZE } from 'services/api';
import Pager from 'components/Pager';

function CandidatesList() {
  const [candidates, setCandidates] = useState([]);
  const [total, setTotal] = useState(0);
  const [page, setPage] = useState(1);
  const [pageSize] = useState(DEFAULT_PAGE_SIZE);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');

  const fetchCandidates = useCallback(async (pageToLoad) => {
    try {
      setLoading(true);
      const response = await candidatesAPI.getPage(pageToLoad, pageSize);
      setCandidates(response.data);
      setTotal(readTotal(response));
      setError('');
    } catch (err) {
      setError('Failed to fetch candidates');
      console.error('Error fetching candidates:', err);
    } finally {
      setLoading(false);
    }
  }, [pageSize]);

  useEffect(() => {
    fetchCandidates(page);
  }, [page, fetchCandidates]);

  // Removing the only row on a page would otherwise leave the viewer on an empty page.
  const reloadAfterDelete = () => {
    if (candidates.length === 1 && page > 1) {
      setPage(page - 1);
    } else {
      fetchCandidates(page);
    }
  };

  const handleDelete = async (id) => {
    if (window.confirm('Are you sure you want to delete this candidate?')) {
      try {
        await candidatesAPI.delete(id);
        reloadAfterDelete();
      } catch (err) {
        setError('Failed to delete candidate');
        console.error('Error deleting candidate:', err);
      }
    }
  };

  const handleDownloadCV = async (id, fileName) => {
    try {
      const response = await candidatesAPI.downloadCV(id);
      const url = window.URL.createObjectURL(new Blob([response.data]));
      const link = document.createElement('a');
      link.href = url;
      link.setAttribute('download', fileName || 'cv.pdf');
      document.body.appendChild(link);
      link.click();
      link.remove();
      window.URL.revokeObjectURL(url);
    } catch (err) {
      setError('Failed to download CV');
      console.error('Error downloading CV:', err);
    }
  };

  const handleDeleteCV = async (id) => {
    if (window.confirm('Are you sure you want to delete this CV?')) {
      try {
        await candidatesAPI.deleteCV(id);
        fetchCandidates(page);
      } catch (err) {
        setError('Failed to delete CV');
        console.error('Error deleting CV:', err);
      }
    }
  };

  if (loading) {
    return (
      <div className="d-flex justify-content-center">
        <div className="spinner-border" role="status">
          <span className="visually-hidden">Loading...</span>
        </div>
      </div>
    );
  }

  return (
    <div>
      <div className="d-flex justify-content-between align-items-center mb-4">
        <h2>Candidates</h2>
        <Link to="/candidates/new" className="btn btn-primary">
          Add New Candidate
        </Link>
      </div>

      {error && (
        <div className="alert alert-danger" role="alert">
          {error}
        </div>
      )}

      {total === 0 ? (
        <div className="alert alert-info">
          No candidates found. <Link to="/candidates/new">Add the first candidate</Link>.
        </div>
      ) : (
        <>
          <div className="table-responsive">
            <table className="table table-striped">
              <thead>
                <tr>
                  <th>Name</th>
                  <th>Email</th>
                  <th>Mobile</th>
                  <th>Degree</th>
                  <th>CV</th>
                  <th>Created</th>
                  <th>Actions</th>
                </tr>
              </thead>
              <tbody>
                {candidates.map(candidate => (
                  <tr key={candidate.id}>
                    <td>{candidate.fullName}</td>
                    <td>{candidate.email}</td>
                    <td>{candidate.mobile || '-'}</td>
                    <td>{candidate.degreeName || '-'}</td>
                    <td>
                      {candidate.hasCV ? (
                        <div className="btn-group btn-group-sm">
                          <button
                            className="btn btn-outline-primary"
                            onClick={() => handleDownloadCV(candidate.id, candidate.cvFileName)}
                          >
                            Download
                          </button>
                          <button
                            className="btn btn-outline-danger"
                            onClick={() => handleDeleteCV(candidate.id)}
                          >
                            Delete CV
                          </button>
                        </div>
                      ) : (
                        <span className="text-muted">No CV</span>
                      )}
                    </td>
                    <td>{new Date(candidate.creationTime).toLocaleDateString()}</td>
                    <td>
                      <div className="btn-group btn-group-sm">
                        <Link
                          to={`/candidates/edit/${candidate.id}`}
                          className="btn btn-outline-primary"
                        >
                          Edit
                        </Link>
                        <button
                          className="btn btn-outline-danger"
                          onClick={() => handleDelete(candidate.id)}
                        >
                          Delete
                        </button>
                      </div>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>

          <Pager
            page={page}
            pageSize={pageSize}
            total={total}
            onPageChange={setPage}
            singular="candidate"
          />
        </>
      )}
    </div>
  );
}

export default CandidatesList;

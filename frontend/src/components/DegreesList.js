import React, { useState, useEffect, useCallback } from 'react';
import { Link } from 'react-router-dom';
import { degreesAPI, readTotal, DEFAULT_PAGE_SIZE } from 'services/api';
import Pager from 'components/Pager';

function DegreesList() {
  const [degrees, setDegrees] = useState([]);
  const [total, setTotal] = useState(0);
  const [page, setPage] = useState(1);
  const [pageSize] = useState(DEFAULT_PAGE_SIZE);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');
  const [success, setSuccess] = useState('');

  const fetchDegrees = useCallback(async (pageToLoad) => {
    try {
      setLoading(true);
      const response = await degreesAPI.getPage(pageToLoad, pageSize);
      setDegrees(response.data);
      setTotal(readTotal(response));
      setError('');
    } catch (err) {
      setError('Failed to fetch degrees');
      console.error('Error fetching degrees:', err);
    } finally {
      setLoading(false);
    }
  }, [pageSize]);

  useEffect(() => {
    fetchDegrees(page);
  }, [page, fetchDegrees]);

  // Setting the page fires the effect; staying on page 1 needs the fetch called directly.
  const goOrFetch = (target) => {
    if (target === page) {
      fetchDegrees(target);
    } else {
      setPage(target);
    }
  };

  const handleDelete = async (id) => {
    if (window.confirm('Are you sure you want to delete this degree?')) {
      try {
        await degreesAPI.delete(id);
        setSuccess('Degree deleted successfully');
        setTimeout(() => setSuccess(''), 3000);
        goOrFetch(degrees.length === 1 && page > 1 ? page - 1 : page);
      } catch (err) {
        if (err.response?.status === 400) {
          setError('Cannot delete degree that is assigned to candidates');
        } else {
          setError('Failed to delete degree');
        }
        console.error('Error deleting degree:', err);
        setTimeout(() => setError(''), 5000);
      }
    }
  };

  const handleCleanup = async () => {
    if (window.confirm('Are you sure you want to delete all unused degrees?')) {
      try {
        const response = await degreesAPI.cleanup();
        setSuccess(response.data.message);
        goOrFetch(1);
        setTimeout(() => setSuccess(''), 5000);
      } catch (err) {
        setError('Failed to cleanup degrees');
        console.error('Error cleaning up degrees:', err);
        setTimeout(() => setError(''), 5000);
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
        <h2>Degrees</h2>
        <div className="d-flex gap-2">
          <button
            className="btn btn-warning"
            onClick={handleCleanup}
            disabled={total === 0}
            title="Removes degrees that no candidate is using, across all pages"
          >
            Cleanup Unused Degrees
          </button>
          <Link to="/degrees/new" className="btn btn-primary">
            Add New Degree
          </Link>
        </div>
      </div>

      {error && (
        <div className="alert alert-danger" role="alert">
          {error}
        </div>
      )}

      {success && (
        <div className="alert alert-success" role="alert">
          {success}
        </div>
      )}

      {total === 0 ? (
        <div className="alert alert-info">
          No degrees found. <Link to="/degrees/new">Add the first degree</Link>.
        </div>
      ) : (
        <>
          <div className="table-responsive">
            <table className="table table-striped">
              <thead>
                <tr>
                  <th>Name</th>
                  <th>Candidates Count</th>
                  <th>Created</th>
                  <th>Actions</th>
                </tr>
              </thead>
              <tbody>
                {degrees.map(degree => (
                  <tr key={degree.id}>
                    <td>{degree.name}</td>
                    <td>
                      <span className={`badge ${degree.candidateCount > 0 ? 'bg-primary' : 'bg-secondary'}`}>
                        {degree.candidateCount}
                      </span>
                    </td>
                    <td>{new Date(degree.creationTime).toLocaleDateString()}</td>
                    <td>
                      <div className="btn-group btn-group-sm">
                        <Link
                          to={`/degrees/edit/${degree.id}`}
                          className="btn btn-outline-primary"
                        >
                          Edit
                        </Link>
                        <button
                          className="btn btn-outline-danger"
                          onClick={() => handleDelete(degree.id)}
                          disabled={degree.candidateCount > 0}
                          title={degree.candidateCount > 0 ? 'Cannot delete degree with assigned candidates' : 'Delete degree'}
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
            singular="degree"
          />
        </>
      )}

      <div className="mt-4">
        <div className="card">
          <div className="card-body">
            <h5 className="card-title">About Degree Management</h5>
            <p className="card-text">
              • Degrees can only be deleted if they are not assigned to any candidates<br/>
              • Use the "Cleanup Unused Degrees" button to remove all degrees that have no candidates<br/>
              • The badge shows how many candidates are assigned to each degree
            </p>
          </div>
        </div>
      </div>
    </div>
  );
}

export default DegreesList;

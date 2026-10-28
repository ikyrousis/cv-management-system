import React, { useState, useEffect, useCallback } from 'react';
import { useNavigate, useParams } from 'react-router-dom';
import { degreesAPI } from 'services/api';

function DegreeForm() {
  const navigate = useNavigate();
  const { id } = useParams();
  const isEdit = Boolean(id);

  const [formData, setFormData] = useState({
    name: ''
  });
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState('');
  const [success, setSuccess] = useState('');

  const fetchDegree = useCallback(async () => {
    try {
      const response = await degreesAPI.getById(id);
      const degree = response.data;
      setFormData({
        name: degree.name
      });
    } catch (err) {
      setError('Failed to fetch degree');
      console.error('Error fetching degree:', err);
    }
  }, [id]);

  useEffect(() => {
    if (isEdit) {
      fetchDegree();
    }
  }, [isEdit, fetchDegree]);

  const handleInputChange = (e) => {
    const { name, value } = e.target;
    setFormData(prev => ({
      ...prev,
      [name]: value
    }));
  };

  const validateForm = () => {
    if (!formData.name.trim()) {
      setError('Degree name is required');
      return false;
    }
    if (formData.name.length > 100) {
      setError('Degree name cannot exceed 100 characters');
      return false;
    }
    return true;
  };

  const handleSubmit = async (e) => {
    e.preventDefault();
    setError('');
    setSuccess('');

    if (!validateForm()) {
      return;
    }

    try {
      setLoading(true);
      
      const degreeData = {
        name: formData.name.trim()
      };

      if (isEdit) {
        await degreesAPI.update(id, degreeData);
        setSuccess('Degree updated successfully');
      } else {
        await degreesAPI.create(degreeData);
        setSuccess('Degree created successfully');
      }

      setTimeout(() => {
        navigate('/degrees');
      }, 1500);

    } catch (err) {
      if (err.response?.status === 400 && err.response?.data?.includes('already exists')) {
        setError('A degree with this name already exists');
      } else {
        setError(isEdit ? 'Failed to update degree' : 'Failed to create degree');
      }
      console.error('Error saving degree:', err);
    } finally {
      setLoading(false);
    }
  };

  return (
    <div>
      <h2>{isEdit ? 'Edit Degree' : 'Add New Degree'}</h2>

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

      <form onSubmit={handleSubmit}>
        <div className="mb-3">
          <label htmlFor="name" className="form-label">Degree Name *</label>
          <input
            type="text"
            className="form-control"
            id="name"
            name="name"
            value={formData.name}
            onChange={handleInputChange}
            required
            maxLength="100"
            placeholder="e.g., BSc, MSc, PhD, Bachelor of Science, etc."
          />
          <div className="form-text">Enter the degree name (max 100 characters)</div>
        </div>

        <div className="d-flex gap-2">
          <button
            type="submit"
            className="btn btn-primary"
            disabled={loading}
          >
            {loading ? 'Saving...' : (isEdit ? 'Update Degree' : 'Create Degree')}
          </button>
          <button
            type="button"
            className="btn btn-secondary"
            onClick={() => navigate('/degrees')}
          >
            Cancel
          </button>
        </div>
      </form>

      <div className="mt-4">
        <div className="card">
          <div className="card-body">
            <h6 className="card-title">Tips:</h6>
            <ul className="card-text mb-0">
              <li>Degree names should be unique</li>
              <li>Common examples: BSc, MSc, PhD, Bachelor of Arts, Master of Science</li>
              <li>You can edit degree names later if needed</li>
              <li>Degrees cannot be deleted if they are assigned to candidates</li>
            </ul>
          </div>
        </div>
      </div>
    </div>
  );
}

export default DegreeForm;

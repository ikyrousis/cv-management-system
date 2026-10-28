import React, { useState, useEffect, useCallback } from 'react';
import { useNavigate, useParams } from 'react-router-dom';
import { candidatesAPI, degreesAPI } from 'services/api';

function CandidateForm() {
  const navigate = useNavigate();
  const { id } = useParams();
  const isEdit = Boolean(id);

  const [formData, setFormData] = useState({
    firstName: '',
    lastName: '',
    email: '',
    mobile: '',
    degreeId: ''
  });
  const [degrees, setDegrees] = useState([]);
  const [cvFile, setCvFile] = useState(null);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState('');
  const [success, setSuccess] = useState('');

  const fetchDegrees = useCallback(async () => {
    try {
      const response = await degreesAPI.getAll();
      setDegrees(response.data);
    } catch (err) {
      setError('Failed to fetch degrees');
      console.error('Error fetching degrees:', err);
    }
  }, []);

  const fetchCandidate = useCallback(async () => {
    try {
      const response = await candidatesAPI.getById(id);
      const candidate = response.data;
      setFormData({
        firstName: candidate.firstName,
        lastName: candidate.lastName,
        email: candidate.email,
        mobile: candidate.mobile || '',
        degreeId: candidate.degreeId || ''
      });
    } catch (err) {
      setError('Failed to fetch candidate');
      console.error('Error fetching candidate:', err);
    }
  }, [id]);

  useEffect(() => {
    fetchDegrees();
    if (isEdit) {
      fetchCandidate();
    }
  }, [isEdit, fetchDegrees, fetchCandidate]);

  const handleInputChange = (e) => {
    const { name, value } = e.target;
    setFormData(prev => ({
      ...prev,
      [name]: value
    }));
  };

  const handleFileChange = (e) => {
    setCvFile(e.target.files[0]);
  };

  const validateForm = () => {
    if (!formData.firstName.trim()) {
      setError('First name is required');
      return false;
    }
    if (!formData.lastName.trim()) {
      setError('Last name is required');
      return false;
    }
    if (!formData.email.trim()) {
      setError('Email is required');
      return false;
    }
    if (!/^[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,}$/.test(formData.email)) {
      setError('Please enter a valid email address (emailname@domain.com)');
      return false;
    }
    if (formData.mobile && !/^\d{10}$/.test(formData.mobile)) {
      setError('Mobile must be exactly 10 digits');
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
      
      const candidateData = {
        ...formData,
        degreeId: formData.degreeId ? parseInt(formData.degreeId) : null
      };

      let candidateId;
      if (isEdit) {
        await candidatesAPI.update(id, candidateData);
        candidateId = id;
        setSuccess('Candidate updated successfully');
      } else {
        const response = await candidatesAPI.create(candidateData);
        candidateId = response.data.id;
        setSuccess('Candidate created successfully');
      }

      if (cvFile) {
        try {
          await candidatesAPI.uploadCV(candidateId, cvFile);
          setSuccess(prev => prev + ' and CV uploaded');
        } catch (cvErr) {
          setError('Candidate saved but CV upload failed');
          console.error('Error uploading CV:', cvErr);
        }
      }

      setTimeout(() => {
        navigate('/candidates');
      }, 1500);

    } catch (err) {
      setError(isEdit ? 'Failed to update candidate' : 'Failed to create candidate');
      console.error('Error saving candidate:', err);
    } finally {
      setLoading(false);
    }
  };

  return (
    <div>
      <h2>{isEdit ? 'Edit Candidate' : 'Add New Candidate'}</h2>

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
        <div className="row">
          <div className="col-md-6">
            <div className="mb-3">
              <label htmlFor="firstName" className="form-label">First Name *</label>
              <input
                type="text"
                className="form-control"
                id="firstName"
                name="firstName"
                value={formData.firstName}
                onChange={handleInputChange}
                required
                maxLength="50"
              />
            </div>
          </div>
          <div className="col-md-6">
            <div className="mb-3">
              <label htmlFor="lastName" className="form-label">Last Name *</label>
              <input
                type="text"
                className="form-control"
                id="lastName"
                name="lastName"
                value={formData.lastName}
                onChange={handleInputChange}
                required
                maxLength="50"
              />
            </div>
          </div>
        </div>

        <div className="mb-3">
          <label htmlFor="email" className="form-label">Email *</label>
          <input
            type="email"
            className="form-control"
            id="email"
            name="email"
            value={formData.email}
            onChange={handleInputChange}
            pattern="[a-zA-Z0-9._\%\+\-]+@[a-zA-Z0-9.\-]+\.[a-zA-Z]{2,}"
            placeholder="example@domain.com"
            required
            maxLength="100"
          />
          <div className="form-text">Enter a valid email address (emailname@domain.com)</div>
        </div>

        <div className="mb-3">
          <label htmlFor="mobile" className="form-label">Mobile (10 digits)</label>
          <input
            type="text"
            className="form-control"
            id="mobile"
            name="mobile"
            value={formData.mobile}
            onChange={handleInputChange}
            pattern="\d{10}"
            placeholder="1234567890"
            maxLength="10"
          />
          <div className="form-text">Enter exactly 10 digits (optional)</div>
        </div>

        <div className="mb-3">
          <label htmlFor="degreeId" className="form-label">Degree</label>
          <select
            className="form-select"
            id="degreeId"
            name="degreeId"
            value={formData.degreeId}
            onChange={handleInputChange}
          >
            <option value="">Select a degree (optional)</option>
            {degrees.map(degree => (
              <option key={degree.id} value={degree.id}>
                {degree.name}
              </option>
            ))}
          </select>
        </div>

        <div className="mb-3">
          <label htmlFor="cvFile" className="form-label">CV File</label>
          <input
            type="file"
            className="form-control"
            id="cvFile"
            onChange={handleFileChange}
            accept=".pdf,.doc,.docx"
          />
          <div className="form-text">Upload PDF or Word document</div>
        </div>

        <div className="d-flex gap-2">
          <button
            type="submit"
            className="btn btn-primary"
            disabled={loading}
          >
            {loading ? 'Saving...' : (isEdit ? 'Update Candidate' : 'Create Candidate')}
          </button>
          <button
            type="button"
            className="btn btn-secondary"
            onClick={() => navigate('/candidates')}
          >
            Cancel
          </button>
        </div>
      </form>
    </div>
  );
}

export default CandidateForm;

import axios from 'axios';

const API_BASE_URL = 'http://localhost:5293/api';

const api = axios.create({
  baseURL: API_BASE_URL,
  headers: {
    'Content-Type': 'application/json',
  },
});

export const candidatesAPI = {
  getAll: () => api.get('/candidates'),
  getById: (id) => api.get(`/candidates/${id}`),
  create: (candidate) => api.post('/candidates', candidate),
  update: (id, candidate) => api.put(`/candidates/${id}`, candidate),
  delete: (id) => api.delete(`/candidates/${id}`),
  uploadCV: (id, file) => {
    const formData = new FormData();
    formData.append('file', file);
    return api.post(`/candidates/${id}/cv`, formData, {
      headers: {
        'Content-Type': 'multipart/form-data',
      },
    });
  },
  downloadCV: (id) => api.get(`/candidates/${id}/cv`, { responseType: 'blob' }),
  deleteCV: (id) => api.delete(`/candidates/${id}/cv`),
};

export const degreesAPI = {
  getAll: () => api.get('/degrees'),
  getById: (id) => api.get(`/degrees/${id}`),
  create: (degree) => api.post('/degrees', degree),
  update: (id, degree) => api.put(`/degrees/${id}`, degree),
  delete: (id) => api.delete(`/degrees/${id}`),
  cleanup: () => api.delete('/degrees/cleanup'),
};

export default api;

import axios from 'axios';

const API_BASE_URL = 'http://localhost:5293/api';

// Mirrors backend Validation/PagingLimits.cs. A list request always carries a page size,
// so the UI and the API never disagree about how much one request returns.
export const DEFAULT_PAGE_SIZE = 20;
export const MAX_PAGE_SIZE = 100;

const api = axios.create({
  baseURL: API_BASE_URL,
  headers: {
    'Content-Type': 'application/json',
  },
});

// The API exposes this header through Access-Control-Expose-Headers.
export const readTotal = (response) => {
  const raw = response.headers['x-total-count'];
  const parsed = Number(raw);

  return Number.isFinite(parsed) ? parsed : response.data.length;
};

const pagedGet = (path, page, pageSize) =>
  api.get(path, { params: { page, pageSize } });

// A dropdown needs every option rather than one page, so it walks the pages until the
// reported total is covered. The page cap keeps a wrong total from looping forever.
const fetchEvery = async (path, pageSize = MAX_PAGE_SIZE, maxPages = 50) => {
  const items = [];
  let page = 1;
  let total = Infinity;

  while (items.length < total && page <= maxPages) {
    const response = await pagedGet(path, page, pageSize);
    total = readTotal(response);
    if (response.data.length === 0) {
      break;
    }
    items.push(...response.data);
    page += 1;
  }

  return { data: items, total: Number.isFinite(total) ? total : items.length };
};

export const candidatesAPI = {
  getPage: (page = 1, pageSize = DEFAULT_PAGE_SIZE) => pagedGet('/candidates', page, pageSize),
  getAllComplete: () => fetchEvery('/candidates'),
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
  getPage: (page = 1, pageSize = DEFAULT_PAGE_SIZE) => pagedGet('/degrees', page, pageSize),
  getAllComplete: () => fetchEvery('/degrees'),
  getById: (id) => api.get(`/degrees/${id}`),
  create: (degree) => api.post('/degrees', degree),
  update: (id, degree) => api.put(`/degrees/${id}`, degree),
  delete: (id) => api.delete(`/degrees/${id}`),
  cleanup: () => api.delete('/degrees/cleanup'),
};

export default api;

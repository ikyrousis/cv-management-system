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

// The access token lives in memory only. Nothing writes it to localStorage or a cookie, so a
// page refresh ends the session and the user logs in again: the deliberate XSS tradeoff until
// the API grows refresh tokens.
let accessToken = null;

export const setToken = (token) => {
  accessToken = token;
};

export const clearToken = () => {
  accessToken = null;
};

export const hasToken = () => Boolean(accessToken);

// Every request carries the bearer token while a session is open.
api.interceptors.request.use((config) => {
  if (accessToken) {
    config.headers.Authorization = `Bearer ${accessToken}`;
  }

  return config;
});

// A 401 from a protected endpoint means the session is gone: expired, or the API refuses it.
// The token is discarded and the user lands on the login page. The login endpoint itself is
// excluded, otherwise wrong credentials would bounce the user off the page that reports them.
api.interceptors.response.use(
  (response) => response,
  (error) => {
    const status = error.response?.status;
    const url = error.config?.url || '';

    if (status === 401 && !url.includes('/auth/login')) {
      clearToken();
      window.location.assign('/login');
    }

    return Promise.reject(error);
  }
);

export const authAPI = {
  login: (username, password) => api.post('/auth/login', { username, password }),
};

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

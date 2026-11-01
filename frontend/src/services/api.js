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

// The access token lives in memory only, so it is out of reach for XSS. The refresh token sits in
// localStorage, which is XSS-readable: that tradeoff buys the session surviving a page refresh,
// and it is bounded by the API's rotation and replay containment (a stolen token dies on first
// use, and replaying it kills the account's whole chain) plus server-side revocation at logout.
let accessToken = null;

let refreshInFlight = null;

const REFRESH_STORAGE_KEY = 'cv-management-refresh-token';

const readStoredRefreshToken = () => window.localStorage.getItem(REFRESH_STORAGE_KEY);

export const hasStoredRefreshToken = () => Boolean(readStoredRefreshToken());

export const setSession = ({ token, refreshToken }) => {
  accessToken = token;
  window.localStorage.setItem(REFRESH_STORAGE_KEY, refreshToken);
};

export const clearSession = () => {
  accessToken = null;
  window.localStorage.removeItem(REFRESH_STORAGE_KEY);
};

export const hasToken = () => Boolean(accessToken);

// Every request carries the bearer token while a session is open.
api.interceptors.request.use((config) => {
  if (accessToken) {
    config.headers.Authorization = `Bearer ${accessToken}`;
  }

  return config;
});

const requestRefresh = async () => {
  const stored = readStoredRefreshToken();
  if (!stored) {
    return null;
  }

  const response = await axios.post('/auth/refresh', { refreshToken: stored });
  setSession(response.data);

  return response.data;
};

// Single-flight: the refresh credential is single-use, so parallel refreshes with one token
// would each consume it and trigger the API's replay containment, logging the user out. Every
// caller shares one in-flight refresh instead.
const refreshOnce = () => {
  if (!refreshInFlight) {
    refreshInFlight = requestRefresh().finally(() => {
      refreshInFlight = null;
    });
  }

  return refreshInFlight;
};

// Called once on app load: with no access token in memory but a stored refresh credential, the
// session resumes silently instead of dropping the user on the login page.
export const resumeSession = async () => {
  if (hasToken() || !hasStoredRefreshToken()) {
    return hasToken();
  }

  try {
    return Boolean(await refreshOnce());
  } catch {
    clearSession();
    return false;
  }
};

// A 401 from a protected endpoint means the access token expired or the API refuses it: the
// stored refresh credential buys one silent retry before the user lands on the login page. The
// auth endpoints are excluded, otherwise wrong credentials would bounce the user off the page
// that reports them.
api.interceptors.response.use(
  (response) => response,
  async (error) => {
    const status = error.response?.status;
    const url = error.config?.url || '';

    if (status === 401 && !url.includes('/auth/login') && !url.includes('/auth/refresh')) {
      try {
        const session = await refreshOnce();

        if (session) {
          error.config.headers.Authorization = `Bearer ${session.token}`;
          return api.request(error.config);
        }
      } catch {
        // The refresh itself failed: the session is gone.
      }

      clearSession();
      window.location.assign('/login');
    }

    return Promise.reject(error);
  }
);

export const authAPI = {
  login: (username, password) => api.post('/auth/login', { username, password }),

  // Server-side revocation: the API burns the refresh credential so nothing can rotate it again.
  logout: () => {
    const stored = readStoredRefreshToken();
    return stored
      ? api.post('/auth/logout', { refreshToken: stored })
      : Promise.resolve({ data: null });
  },
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

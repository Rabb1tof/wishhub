import axios, { AxiosError } from 'axios'
import type { AxiosRequestConfig } from 'axios'
import { useAuthStore, mapAuthResponseFromApi } from '@/store/auth'
import type { AuthResponseRaw } from '@/types/auth'

const api = axios.create({
  baseURL: '/api',
  withCredentials: false,
})

api.interceptors.request.use((config) => {
  const token = useAuthStore.getState().accessToken
  if (token) {
    config.headers = config.headers ?? {}
    config.headers.Authorization = `Bearer ${token}`
  }
  return config
})

let isRefreshing = false
let failedQueue: { resolve: (value: AxiosRequestConfig | PromiseLike<AxiosRequestConfig>) => void; reject: (error: unknown) => void }[] = []

const processQueue = (error: unknown, token?: string) => {
  failedQueue.forEach(({ resolve, reject }) => {
    if (error) {
      reject(error)
    } else {
      resolve({ headers: { Authorization: `Bearer ${token}` } })
    }
  })
  failedQueue = []
}

api.interceptors.response.use(
  (response) => response,
  async (error: AxiosError) => {
    const originalRequest = error.config
    if (!originalRequest || error.response?.status !== 401) {
      return Promise.reject(error)
    }

    const authStore = useAuthStore.getState()

    if (isRefreshing) {
      return new Promise((resolve, reject) => {
        failedQueue.push({ resolve, reject })
      })
        .then(() => api(originalRequest))
        .catch((err) => Promise.reject(err))
    }

    isRefreshing = true

    try {
      const refreshResponse = await api.post<AuthResponseRaw>('/auth/refresh', {
        refreshToken: authStore.refreshToken,
      })

      const mapped = mapAuthResponseFromApi(refreshResponse.data)
      useAuthStore.getState().setAuth(mapped)
      processQueue(null, mapped.accessToken)
      return api(originalRequest)
    } catch (refreshError) {
      processQueue(refreshError)
      useAuthStore.getState().clearAuth()
      window.location.href = '/login'
      return Promise.reject(refreshError)
    } finally {
      isRefreshing = false
    }
  },
)

export default api

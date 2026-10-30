import api from './client'
import type { MetadataResult } from '@/types/metadata'

export async function resolveMetadata(url: string, signal: AbortSignal): Promise<MetadataResult> {
  const response = await api.post<MetadataResult>('/metadata/resolve', { url }, { signal })
  return response.data
}

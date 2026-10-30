export interface MetadataResult {
  status: 'ok' | 'partial' | 'unavailable' | 'unsupported'
  name: string | null
  imageUrl: string | null
  source: string
  reason: string | null
}

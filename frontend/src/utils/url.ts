export function isHttpUrl(value: string): boolean {
  if (!/^https?:\/\//i.test(value.trim())) return false
  try {
    const url = new URL(value.trim())
    return (url.protocol === 'http:' || url.protocol === 'https:') && !!url.hostname
  } catch {
    return false
  }
}

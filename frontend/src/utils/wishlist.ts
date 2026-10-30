import { isHttpUrl } from './url'

export function formatItemSource(source: string, url: string): string {
  if (source === 'YandexMarket') return 'Яндекс Маркет'
  if (source === 'Unknown') return isHttpUrl(url) ? new URL(url.trim()).hostname : 'Другой магазин'
  return source
}

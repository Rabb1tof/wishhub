export function requestNotificationPermission(): void {
  if (!('Notification' in window)) return
  if (Notification.permission === 'default') {
    Notification.requestPermission()
  }
}

export function showBrowserNotification(title: string, body: string, onClick?: () => void): void {
  if (!('Notification' in window)) return
  if (Notification.permission !== 'granted') return

  try {
    const notification = new Notification(title, { body })

    if (onClick) {
      notification.onclick = () => {
        window.focus()
        onClick()
        notification.close()
      }
    }

    setTimeout(() => notification.close(), 5000)
  } catch {
    // Notification API may throw in some environments
  }
}

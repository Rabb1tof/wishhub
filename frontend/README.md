# WishHub Frontend

React 18 + TypeScript + Vite frontend для WishHub - сервиса управления вишлистами.

## Стек

- **React 18** - UI библиотека
- **TypeScript** - типизация
- **Vite** - сборщик и dev-сервер
- **Tailwind CSS** - стилизация
- **TanStack Query (React Query)** - серверное состояние
- **Zustand** - клиентское состояние (auth)
- **Axios** - HTTP клиент
- **SignalR** - real-time чат
- **React Router v6** - роутинг

## Быстрый старт

### 1. Установка зависимостей

```bash
npm install
```

### 2. Настройка окружения

Создай `.env.local` (не коммитится):

```bash
cp .env.example .env.local
```

Пример `.env.local`:
```env
# API URL
VITE_API_URL=http://localhost:5000/api

# SignalR WebSocket URL
VITE_HUB_URL=http://localhost:5000/hubs/chat
```

### 3. Запуск dev-сервера

```bash
npm run dev
```

Приложение доступно на `http://localhost:5173`

API проксируется автоматически через Vite (см. `vite.config.ts`).

## Структура проекта

```
frontend/
├── src/
│   ├── api/           # API клиент и хуки (TanStack Query)
│   ├── components/    # React компоненты
│   ├── hooks/         # Кастомные хуки
│   ├── pages/         # Страницы приложения
│   ├── store/         # Zustand сторы
│   ├── types/         # TypeScript типы
│   └── utils/         # Утилиты
├── public/            # Статические файлы
├── index.html         # HTML точка входа
└── vite.config.ts     # Конфигурация Vite
```

## Основные скрипты

```bash
npm run dev          # Dev сервер с HMR
npm run build        # Production сборка
npm run preview      # Просмотр production сборки
npm run lint         # ESLint проверка
npm run typecheck    # TypeScript проверка
```

## Особенности

### Прокси API

В `vite.config.ts` настроен прокси для API:
- `/api/*` → `http://localhost:5000/api/*`
- `/hubs/*` → `ws://localhost:5000/hubs/*` (SignalR WebSocket)

### Туннели для разработки

При использовании OAuth (VK/Telegram) нужен публичный URL:

**VS Code Dev Tunnels (рекомендуется):**
1. Открой Ports панель (Cmd+Shift+P → "Ports: Focus on Ports View")
2. Пробрось порт 5173
3. Сделай Public visibility
4. Используй URL вида `https://xxxx.euw.devtunnels.ms`

**ngrok (альтернатива):**
```bash
ngrok http 5173
```

### Telegram Login Widget

Для работы Telegram OAuth на localhost используется прокси через `telegram-widget-proxy.html`.

## Environment Variables

| Переменная | Описание | По умолчанию |
|-----------|---------|-------------|
| `VITE_API_URL` | URL backend API | `http://localhost:5000/api` |
| `VITE_HUB_URL` | URL SignalR hub | `ws://localhost:5000/hubs/chat` |

Переменные должны начинаться с `VITE_` чтобы быть доступны в коде через `import.meta.env`.

## TypeScript

Проект использует строгую конфигурацию TypeScript. Все API ответы типизированы в `src/types/`.

## Полезные ссылки

- [Vite Docs](https://vitejs.dev/)
- [React Query](https://tanstack.com/query/latest)
- [Tailwind CSS](https://tailwindcss.com/)
- [Zustand](https://github.com/pmndrs/zustand)

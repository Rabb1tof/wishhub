# WishHub Production Deployment Guide

> **📌 Для EasyPanel**: см. `EASYPANEL.md` (упрощённый деплой с авто SSL)
> Этот гайд для ручного деплоя через Docker Compose на собственный сервер.

## Быстрый старт

### 1. Подготовка сервера

```bash
# Установи Docker и Docker Compose
sudo apt update
sudo apt install docker.io docker-compose-plugin

# Добавь пользователя в docker group
sudo usermod -aG docker $USER
# Перелогинься или:
newgrp docker
```

### 2. Клонирование и настройка

```bash
# Клонируй репозиторий
git clone <your-repo-url> wishhub
cd wishhub

# Создай .env из шаблона
cp .env.example .env

# Отредактируй .env (обязательно поменяй пароли и секреты!)
nano .env
```

**Важно**: `.env` НЕ должен быть в git (уже в `.gitignore`)

### 3. Получение SSL сертификатов (Let's Encrypt)

```bash
# Установи certbot
sudo apt install certbot

# Получи сертификаты
sudo certbot certonly --standalone -d wishhub.rabb1t.tech

# Создай директорию для сертификатов
mkdir -p ssl
sudo cp /etc/letsencrypt/live/wishhub.rabb1t.tech/fullchain.pem ssl/
sudo cp /etc/letsencrypt/live/wishhub.rabb1t.tech/privkey.pem ssl/
sudo chown -R $USER:$USER ssl/
```

### 4. Запуск

```bash
# Собери и запусти
docker-compose -f docker-compose.prod.yml up -d --build

# Проверь логи
docker-compose -f docker-compose.prod.yml logs -f api
docker-compose -f docker-compose.prod.yml logs -f nginx

# Проверь статус
docker-compose -f docker-compose.prod.yml ps
```

### 5. Настройка автоматического обновления SSL

```bash
# Создай скрипт для обновления
sudo nano /usr/local/bin/update-wishhub-ssl.sh
```

```bash
#!/bin/bash
certbot renew --quiet
cp /etc/letsencrypt/live/wishhub.rabb1t.tech/fullchain.pem /path/to/wishhub/ssl/
cp /etc/letsencrypt/live/wishhub.rabb1t.tech/privkey.pem /path/to/wishhub/ssl/
cd /path/to/wishhub && docker-compose -f docker-compose.prod.yml restart nginx
```

```bash
chmod +x /usr/local/bin/update-wishhub-ssl.sh

# Добавь в crontab
sudo crontab -e
# Добавь строку:
# 0 3 * * * /usr/local/bin/update-wishhub-ssl.sh
```

## Переменные окружения

Все важные настройки в `.env`:

| Переменная | Описание | Обязательная |
|-----------|---------|--------------|
| `POSTGRES_PASSWORD` | Пароль PostgreSQL | ✅ |
| `JWT_SECRET` | Секретный ключ JWT | ✅ |
| `VK_CLIENT_ID` | ID приложения VK | Для OAuth |
| `VK_CLIENT_SECRET` | Секрет VK | Для OAuth |
| `TELEGRAM_BOT_TOKEN` | Токен бота Telegram | Для OAuth |
| `PUBLIC_URL` | Публичный URL | По умолчанию your_domain.com |

## Обновление приложения

```bash
cd wishhub

# Получи изменения
git pull

# Пересобери и перезапусти
docker-compose -f docker-compose.prod.yml down
docker-compose -f docker-compose.prod.yml up -d --build

# Очисти старые образы
docker image prune -f
```

## Бэкапы

```bash
# Бэкап базы данных
docker exec wishhub-postgres pg_dump -U wishlist wishlist > backup_$(date +%Y%m%d).sql

# Восстановление
docker exec -i wishhub-postgres psql -U wishlist wishlist < backup_YYYYMMDD.sql
```

## Типичные проблемы

### 502 Bad Gateway
- Проверь, что api контейнер запущен: `docker-compose -f docker-compose.prod.yml ps`
- Проверь логи api: `docker-compose -f docker-compose.prod.yml logs api`

### SSL ошибки
- Проверь пути к сертификатам в `ssl/`
- Проверь права доступа: `ls -la ssl/`

### OAuth не работает
- Проверь `PUBLIC_URL` в `.env`
- Проверь настройки в VK/Telegram (должен совпадать домен)

## Полезные команды

```bash
# Посмотреть логи
docker-compose -f docker-compose.prod.yml logs -f [service]

# Перезапустить сервис
docker-compose -f docker-compose.prod.yml restart [service]

# Выполнить команду внутри контейнера
docker exec -it wishhub-api sh

# Очистить всё (осторожно!)
docker-compose -f docker-compose.prod.yml down -v
```

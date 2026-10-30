# WishHub в EasyPanel

Используйте [инструкцию registry deploy](DEPLOY-OPTIMIZED.md) и `docker-compose.registry.yml`.

Нужны PostgreSQL, API и frontend, постоянные `pgdata`/`avatars`, домен на frontend:80 и переменные из `.env.example`. API не требует отдельного браузерного контейнера, Redis, CDP-порта или расписания цен.

При переходе с предыдущей версии остановите старые API workers перед миграцией и обновите API/frontend вместе. [Подробный порядок](docs/lightweight-rollout.md). Для сборки на сервере существует `docker-compose.prod.yml`; при нём нужно отдельно обеспечить домен и TLS-конфигурацию nginx.

FROM php:8.4-cli
RUN docker-php-ext-install pdo_mysql
WORKDIR /site
CMD ["php", "-S", "0.0.0.0:8080", "-t", "public_html"]

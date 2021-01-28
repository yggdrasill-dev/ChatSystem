#! /bin/bash

envsubst '${PROXY_SITE}' < /etc/nginx/conf.d/default.conf.template > /etc/nginx/conf.d/default.conf

export AppConfig="$(envsubst < /usr/share/nginx/html/appSettings.json | tr -d '\r')"
envsubst '${AppConfig},${BASE_HREF}' < /usr/share/nginx/html/index.config.html > /usr/share/nginx/html/index.html
unset AppConfig

rm -f /usr/share/nginx/html/index.config.html
rm -f /usr/share/nginx/html/appSettings.json

if [ $# = 0 ]
then
    exec nginx -g 'daemon off;'
else
    exec "$@"
fi

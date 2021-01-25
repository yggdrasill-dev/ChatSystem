#! /bin/bash

envsubst '${PROXY_SITE}' < /etc/nginx/conf.d/default.conf.template > /etc/nginx/conf.d/default.conf

if [ $# = 0 ]
then
    exec nginx -g 'daemon off;'
else
    exec "$@"
fi

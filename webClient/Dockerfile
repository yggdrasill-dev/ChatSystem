FROM nginx AS base
EXPOSE 80

FROM node AS sdk
WORKDIR /src
COPY . .
RUN npm i
RUN npx ng build --prod --base-href=/webclient/

FROM base AS final
ENV PROXY_SITE=http://localhost
COPY nginx/default.conf.template /etc/nginx/conf.d/
COPY nginx/nginx.conf /etc/nginx/
COPY nginx/entry.bash /
COPY --from=sdk /src/dist/webclient /usr/share/nginx/html
ENTRYPOINT [ "/entry.bash" ]

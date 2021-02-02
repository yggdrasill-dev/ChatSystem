FROM nginx AS base
EXPOSE 80

FROM node AS sdk
WORKDIR /src
COPY . .
RUN npm i
RUN node stringify.mjs > temp.json && cat temp.json > src/appSettings.json
RUN npx ng build --prod --index src/index.config.html

FROM base AS final
ENV PROXY_SITE=http://localhost
ENV CHAT_ENDPOINT=wss://localhost:17001/ws
ENV BASE_HREF=/
COPY nginx/default.conf.template /etc/nginx/conf.d/
COPY nginx/nginx.conf /etc/nginx/
COPY nginx/entry.bash /
COPY --from=sdk /src/dist/webclient /usr/share/nginx/html
ENTRYPOINT [ "/entry.bash" ]
